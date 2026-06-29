using SakugaVault.Extensions;
using SakugaVault.Data;
using Microsoft.AspNetCore.Diagnostics;
using SakugaVault.Middleware;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SakugaVault.Options;

// This file is now the composition root for an API-first backend instead of an MVC site.
// The main change in this refactor was removing Razor/static-asset startup and replacing it with
// controller mapping plus layered service registration for a separate React frontend.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApiLayer(builder.Configuration, builder.Environment);
builder.Services.AddApplicationLayer();
builder.Services.AddInfrastructureLayer(builder.Configuration);

var app = builder.Build();

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.DocumentTitle = "SakugaVault API Docs";
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "SakugaVault API v1");
    });
}

app.UseExceptionHandler(errorApp =>
    errorApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerPathFeature>()?.Error;
        var (statusCode, title, detail) = exception switch
        {
            KeyNotFoundException => (
                StatusCodes.Status404NotFound,
                "Not found",
                "The requested item could not be found."),
            UnauthorizedAccessException => (
                StatusCodes.Status403Forbidden,
                "Forbidden",
                "You do not have permission to perform this action."),
            ArgumentException or FormatException => (
                StatusCodes.Status400BadRequest,
                "Invalid request",
                "Some parts of the request could not be processed."),
            _ => (
                StatusCodes.Status500InternalServerError,
                "Unexpected error",
                "Something went wrong on the server. Please try again in a moment.")
        };

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Title = title,
            Detail = detail,
            Status = statusCode
        };

        if (context.Items.TryGetValue(HttpContextItemKeys.CorrelationId, out var correlationId) &&
            correlationId is string correlationIdValue &&
            !string.IsNullOrWhiteSpace(correlationIdValue))
        {
            problem.Extensions["correlationId"] = correlationIdValue;
        }

        await context.Response.WriteAsJsonAsync(problem);
    }));

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
    context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
    await next();
});

{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<SakugaVaultDbContext>();

    // Apply EF migrations on every startup (dev AND prod) so a fresh database
    // gets its schema created automatically. Previously this was gated to
    // Development, which left production with no tables.
    await dbContext.Database.MigrateAsync(app.Lifetime.ApplicationStopping);

    // Sample catalog seed data is for local development only.
    if (app.Environment.IsDevelopment())
    {
        var catalogOptions = scope.ServiceProvider.GetRequiredService<IOptions<CatalogOptions>>().Value;
        if (catalogOptions.EnableDevelopmentSeedData)
        {
            var seeder = scope.ServiceProvider.GetRequiredService<SakugaVaultSeeder>();
            await seeder.SeedAsync(app.Lifetime.ApplicationStopping);
        }
    }
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseHttpsRedirection();
app.UseRouting();
app.UseCors(CorsPolicyNames.Frontend);
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

// Controllers are the HTTP entry points, but business logic stays in services.
app.MapControllers();
app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Ok(new
{
    service = "SakugaVault API",
    status = "online",
    architecture = "thin-controller-fat-service",
    docs = "/swagger"
})).ExcludeFromDescription();

RegisterScraperStartupChecks(app);

app.Run();

static void RegisterScraperStartupChecks(WebApplication app)
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        _ = Task.Run(async () =>
        {
            var logger = app.Services
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("ConsumetStartupCheck");
            var scraperOptions = app.Services
                .GetRequiredService<IOptions<ScraperOptions>>()
                .Value;

            var configuredResolvers = scraperOptions.PlaybackResolvers
                .Where(resolver => resolver.Enabled)
                .Where(resolver => !string.IsNullOrWhiteSpace(resolver.Name))
                .Where(resolver => Uri.TryCreate(resolver.BaseUrl, UriKind.Absolute, out _))
                .Select(resolver => (resolver.Name, BaseUrl: resolver.BaseUrl.TrimEnd('/')))
                .ToArray();
            var resolvers = configuredResolvers.Length > 0
                ? configuredResolvers
                : [("consumet-compatible", scraperOptions.ConsumetBaseUrl.TrimEnd('/'))];

            foreach (var resolver in resolvers)
            {
                try
                {
                    var client = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient("scraper-client");
                    using var response = await client.GetAsync(
                        $"{resolver.BaseUrl}/anime/gogoanime",
                        app.Lifetime.ApplicationStopping);

                    if (response.IsSuccessStatusCode)
                    {
                        continue;
                    }

                    logger.LogWarning(
                        "WARNING: Playback resolver {ResolverName} is not reachable at {ResolverBaseUrl}. It will be skipped until the resolver is running.",
                        resolver.Name,
                        resolver.BaseUrl);
                }
                catch (OperationCanceledException) when (app.Lifetime.ApplicationStopping.IsCancellationRequested)
                {
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        exception,
                        "WARNING: Playback resolver {ResolverName} is not reachable at {ResolverBaseUrl}. It will be skipped until the resolver is running.",
                        resolver.Name,
                        resolver.BaseUrl);
                }
            }
        });
    });
}

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

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<SakugaVaultDbContext>();
    await dbContext.Database.MigrateAsync(app.Lifetime.ApplicationStopping);

    var catalogOptions = scope.ServiceProvider.GetRequiredService<IOptions<CatalogOptions>>().Value;
    if (catalogOptions.EnableDevelopmentSeedData)
    {
        var seeder = scope.ServiceProvider.GetRequiredService<SakugaVaultSeeder>();
        await seeder.SeedAsync(app.Lifetime.ApplicationStopping);
    }
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseHttpsRedirection();
app.UseRouting();
app.UseCors(CorsPolicyNames.Frontend);
app.UseRateLimiter();
app.UseAuthentication();
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

RegisterConsumetStartupCheck(app);

app.Run();

static void RegisterConsumetStartupCheck(WebApplication app)
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

            try
            {
                var client = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient("scraper-client");
                var consumetBaseUrl = scraperOptions.ConsumetBaseUrl.TrimEnd('/');
                using var response = await client.GetAsync(
                    $"{consumetBaseUrl}/anime/gogoanime",
                    app.Lifetime.ApplicationStopping);

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning(
                        "WARNING: Consumet-compatible scraper is not reachable at {ConsumetBaseUrl}. Playback resolution will fail until the scraper is running.",
                        consumetBaseUrl);
                }
            }
            catch (OperationCanceledException) when (app.Lifetime.ApplicationStopping.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "WARNING: Consumet-compatible scraper is not reachable at {ConsumetBaseUrl}. Playback resolution will fail until the scraper is running.",
                    scraperOptions.ConsumetBaseUrl.TrimEnd('/'));
            }
        });
    });
}

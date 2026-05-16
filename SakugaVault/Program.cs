using SakugaVault.Extensions;
using SakugaVault.Data;
using SakugaVault.Middleware;
using Microsoft.EntityFrameworkCore;

// This file is now the composition root for an API-first backend instead of an MVC site.
// The main change in this refactor was removing Razor/static-asset startup and replacing it with
// controller mapping plus layered service registration for a separate React frontend.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApiLayer(builder.Configuration, builder.Environment);
builder.Services.AddApplicationLayer();
builder.Services.AddInfrastructureLayer(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.DocumentTitle = "SakugaVault API Docs";
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "SakugaVault API v1");
    });
}
else
{
    app.UseExceptionHandler();
    app.UseHsts();
}

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<SakugaVaultDbContext>();
    await dbContext.Database.MigrateAsync(app.Lifetime.ApplicationStopping);

    var seeder = scope.ServiceProvider.GetRequiredService<SakugaVaultSeeder>();
    await seeder.SeedAsync(app.Lifetime.ApplicationStopping);
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseHttpsRedirection();
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

app.Run();

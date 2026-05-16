using System.Reflection;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using SakugaVault.Data;
using SakugaVault.Infrastructure.Logging;
using SakugaVault.Models;
using SakugaVault.Options;
using SakugaVault.Services.Auth;
using SakugaVault.Services.Catalog;
using SakugaVault.Services.Metadata;
using SakugaVault.Services.Scraping;
using SakugaVault.Services.Users;
using SakugaVault.Services.Watch;

namespace SakugaVault.Extensions;

/// <summary>
/// Organizes dependency registration by layer.
/// The refactor moved startup concerns out of Program.cs so the composition root stays readable
/// as authentication, EF Core, MySQL, and scraper integrations are added later.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers API-layer concerns: controllers, routing, health checks, auth hooks, OpenAPI, and options binding.
    /// This replaced the old MVC view registration because React now owns the UI.
    /// </summary>
    public static IServiceCollection AddApiLayer(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var jwtOptions = BuildValidatedJwtOptions(configuration);
        var frontendOptions = configuration.GetSection(FrontendOptions.SectionName).Get<FrontendOptions>() ?? new FrontendOptions();
        var allowedOrigins = frontendOptions.AllowedOrigins
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (allowedOrigins.Length == 0 && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Frontend:AllowedOrigins must be configured outside Development. " +
                "Set at least one allowed origin such as https://your-frontend.example.");
        }

        if (allowedOrigins.Length == 0 && environment.IsDevelopment())
        {
            using var bootstrapLoggerFactory = LoggerFactory.Create(logging => logging.AddSimpleConsole());
            var bootstrapLogger = bootstrapLoggerFactory.CreateLogger("Startup");
            bootstrapLogger.LogWarning(
                "Frontend:AllowedOrigins is empty in Development. Falling back to AllowAnyOrigin() for local development only.");
        }

        services.AddProblemDetails();
        services.AddControllers();
        services.AddEndpointsApiExplorer();
        services.AddHttpContextAccessor();
        services.AddRouting(options => options.LowercaseUrls = true);
        services.AddHealthChecks();
        services.AddOptions<FrontendOptions>()
            .Bind(configuration.GetSection(FrontendOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<ScraperOptions>()
            .Bind(configuration.GetSection(ScraperOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<CatalogOptions>()
            .Bind(configuration.GetSection(CatalogOptions.SectionName))
            .Validate(options => options.HomeCatalogCacheMinutes > 0, "Catalog:HomeCatalogCacheMinutes must be greater than zero.")
            .ValidateOnStart();
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .PostConfigure(options =>
            {
                options.SigningKey = Environment.GetEnvironmentVariable(JwtOptions.SigningKeyEnvironmentVariable)
                    ?? options.SigningKey;
            })
            .Validate(options => !string.IsNullOrWhiteSpace(options.Issuer), "Jwt:Issuer is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Audience), "Jwt:Audience is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.SigningKey), $"JWT signing key must come from the {JwtOptions.SigningKeyEnvironmentVariable} environment variable.")
            .Validate(options => options.SigningKey.Length >= 32, "JWT signing key must be at least 32 characters long.")
            .ValidateOnStart();

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidAudience = jwtOptions.Audience,
                    IssuerSigningKey = signingKey,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
            });

        services.AddAuthorization();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                var problemDetails = new ProblemDetails
                {
                    Title = "Too many requests",
                    Detail = "Rate limit exceeded for this endpoint. Please wait before retrying.",
                    Status = StatusCodes.Status429TooManyRequests
                };

                context.HttpContext.Response.ContentType = "application/problem+json";
                await context.HttpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
            };

            options.AddPolicy("auth-login", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            options.AddPolicy("auth-register", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(10),
                        QueueLimit = 0
                    }));
        });

        services.AddCors(options =>
        {
            options.AddPolicy(CorsPolicyNames.Frontend, policy =>
            {
                if (allowedOrigins.Length == 0)
                {
                    policy
                        .AllowAnyOrigin()
                        .AllowAnyHeader()
                        .AllowAnyMethod();

                    return;
                }

                policy
                    .WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "SakugaVault API",
                Version = "v1",
                Description = "API surface for authentication, catalog browsing, watch history, metadata sync, and playback resolution."
            });

            var bearerScheme = new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Description = "JWT Bearer token. Example: 'Bearer {token}'",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            };

            options.AddSecurityDefinition("Bearer", bearerScheme);
            options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", hostDocument: null, externalResource: null)] = []
            });

            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }
        });

        return services;
    }

    /// <summary>
    /// Registers application services where business logic lives.
    /// Thin controllers depend on these interfaces so the codebase can scale without moving logic back into endpoints.
    /// </summary>
    public static IServiceCollection AddApplicationLayer(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IBatchMetadataSyncService, BatchMetadataSyncService>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<IMetadataSyncService, MetadataSyncService>();
        services.AddScoped<IPlaybackResolutionService, PlaybackResolutionService>();
        services.AddScoped<IStreamScraperService, StreamScraperService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IWatchHistoryService, WatchHistoryService>();
        services.AddScoped<IWatchPageService, WatchPageService>();
        services.AddScoped<SakugaVaultSeeder>();

        return services;
    }

    /// <summary>
    /// Registers infrastructure concerns such as MySQL persistence, external HTTP clients, and shared runtime services.
    /// This is where provider-specific wiring belongs so controllers and services stay provider-agnostic.
    /// </summary>
    public static IServiceCollection AddInfrastructureLayer(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MySql")
            ?? throw new InvalidOperationException("Connection string 'MySql' is missing.");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'MySql' is empty. Set ConnectionStrings__MySql through the environment or your secret store.");
        }

        services.AddDbContext<SakugaVaultDbContext>(options =>
        {
            options.UseMySQL(connectionString);
        });

        services.AddHttpClient("scraper-client", client =>
        {
            var baseUrl = configuration[$"{ScraperOptions.SectionName}:{nameof(ScraperOptions.ConsumetBaseUrl)}"];
            if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            {
                client.BaseAddress = uri;
            }

            var timeoutValue = configuration[$"{ScraperOptions.SectionName}:{nameof(ScraperOptions.RequestTimeoutSeconds)}"];
            var timeoutSeconds = int.TryParse(timeoutValue, out var parsedTimeout) && parsedTimeout > 0
                ? parsedTimeout
                : 15;

            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        })
        .AddHttpMessageHandler<LoggingDelegatingHandler>();

        services.AddTransient<LoggingDelegatingHandler>();
        services.AddScoped<IPasswordHasher<ApplicationUser>, PasswordHasher<ApplicationUser>>();

        // TimeProvider is registered now so time-sensitive services can be tested later without
        // hard-coding DateTime.UtcNow throughout the application layer.
        services.AddSingleton(TimeProvider.System);

        return services;
    }

    private static JwtOptions BuildValidatedJwtOptions(IConfiguration configuration)
    {
        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        jwtOptions.SigningKey = Environment.GetEnvironmentVariable(JwtOptions.SigningKeyEnvironmentVariable)
            ?? jwtOptions.SigningKey;

        if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey))
        {
            throw new InvalidOperationException(
                $"JWT signing key is missing. Set the {JwtOptions.SigningKeyEnvironmentVariable} environment variable with a value at least 32 characters long.");
        }

        if (jwtOptions.SigningKey.Length < 32)
        {
            throw new InvalidOperationException(
                $"JWT signing key is too short. The {JwtOptions.SigningKeyEnvironmentVariable} environment variable must be at least 32 characters long.");
        }

        return jwtOptions;
    }
}

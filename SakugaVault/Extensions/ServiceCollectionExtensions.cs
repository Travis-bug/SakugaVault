using System.Reflection;
using System.Threading.RateLimiting;
using System.Text.Json.Serialization;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using SakugaVault.Data;
using SakugaVault.Infrastructure.Logging;
using SakugaVault.Models;
using SakugaVault.Options;
using SakugaVault.Services.Auth;
using SakugaVault.Services.Catalog;
using SakugaVault.Services.Downloads;
using SakugaVault.Services.Metadata;
using SakugaVault.Services.Profile;
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
        var authCookieOptions = configuration.GetSection(AuthCookieOptions.SectionName).Get<AuthCookieOptions>() ?? new AuthCookieOptions();
        var jwtOptions = BuildValidatedJwtOptions(configuration);
        var frontendOptions = configuration.GetSection(FrontendOptions.SectionName).Get<FrontendOptions>() ?? new FrontendOptions();
        var scraperOptions = configuration.GetSection(ScraperOptions.SectionName).Get<ScraperOptions>() ?? new ScraperOptions();
        var allowedOrigins = frontendOptions.AllowedOrigins
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (allowedOrigins.Length == 0)
        {
            throw new InvalidOperationException(
                "Frontend:AllowedOrigins must be configured with explicit frontend origins. Refresh-cookie auth cannot use wildcard CORS.");
        }

        if (!environment.IsDevelopment())
        {
            foreach (var origin in allowedOrigins)
            {
                if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri) || originUri.Scheme != Uri.UriSchemeHttps)
                {
                    throw new InvalidOperationException(
                        $"Production CORS origin '{origin}' is not HTTPS. Only HTTPS frontend origins are allowed outside development.");
                }
            }

            var scraperBaseUrls = scraperOptions.PlaybackResolvers
                .Where(resolver => resolver.Enabled)
                .Select(resolver => resolver.BaseUrl)
                .Append(scraperOptions.ConsumetBaseUrl);
            if (scraperBaseUrls.Any(baseUrl =>
                    Uri.TryCreate(baseUrl, UriKind.Absolute, out var scraperBaseUri) &&
                    string.Equals(scraperBaseUri.Host, "api.consumet.org", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "Production cannot use the public api.consumet.org endpoint. Point scraper resolvers at self-hosted or dedicated instances.");
            }
        }

        services.AddProblemDetails();
        services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            });
        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var validationProblem = new ValidationProblemDetails(context.ModelState)
                {
                    Title = "Some fields need attention.",
                    Detail = GetFirstModelValidationMessage(context.ModelState) ??
                             "Review the highlighted fields and try again.",
                    Status = StatusCodes.Status400BadRequest
                };

                return new BadRequestObjectResult(validationProblem);
            };
        });
        services.AddEndpointsApiExplorer();
        services.AddHttpContextAccessor();
        services.AddRouting(options => options.LowercaseUrls = true);
        services.AddHealthChecks();
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });
        services.AddHsts(options =>
        {
            options.Preload = true;
            options.IncludeSubDomains = true;
            options.MaxAge = TimeSpan.FromDays(365);
        });
        services.AddOptions<FrontendOptions>()
            .Bind(configuration.GetSection(FrontendOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<ScraperOptions>()
            .Bind(configuration.GetSection(ScraperOptions.SectionName))
            .Validate(options => Uri.TryCreate(options.ConsumetBaseUrl, UriKind.Absolute, out _), "Scrapers:ConsumetBaseUrl must be an absolute URL.")
            .Validate(
                options => options.PlaybackResolvers.All(resolver =>
                    !resolver.Enabled ||
                    (!string.IsNullOrWhiteSpace(resolver.Name) &&
                     Uri.TryCreate(resolver.BaseUrl, UriKind.Absolute, out _))),
                "Every enabled Scrapers:PlaybackResolvers entry needs a name and absolute BaseUrl.")
            .Validate(
                options => options.PlaybackResolvers.All(resolver => resolver.RequestTimeoutSeconds >= 0),
                "Scrapers:PlaybackResolvers request timeouts cannot be negative.")
            .Validate(options => options.RequestTimeoutSeconds > 0, "Scrapers:RequestTimeoutSeconds must be greater than zero.")
            .Validate(options => options.InterRequestDelayMilliseconds >= 0, "Scrapers:InterRequestDelayMilliseconds cannot be negative.")
            .ValidateOnStart();
        services.AddOptions<CatalogOptions>()
            .Bind(configuration.GetSection(CatalogOptions.SectionName))
            .Validate(options => options.HomeCatalogCacheMinutes > 0, "Catalog:HomeCatalogCacheMinutes must be greater than zero.")
            .Validate(options => !options.UseLiveProviderCatalog || options.HomePageCount > 0, "Catalog:HomePageCount must be greater than zero when live provider catalog is enabled.")
            .Validate(options => !options.UseLiveProviderCatalog || options.LiveCatalogTitleLimit > 0, "Catalog:LiveCatalogTitleLimit must be greater than zero when live provider catalog is enabled.")
            .ValidateOnStart();
        services.AddOptions<AuthCookieOptions>()
            .Bind(configuration.GetSection(AuthCookieOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.CookieName), "Authentication:CookieName is required.")
            .Validate(options => options.RefreshTokenDays > 0, "Authentication:RefreshTokenDays must be greater than zero.")
            .ValidateOnStart();
        services.AddOptions<JwtOptions>()
            .Configure(options =>
            {
                var boundOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
                options.Issuer = boundOptions.Issuer;
                options.Audience = boundOptions.Audience;
                options.AccessTokenMinutes = boundOptions.AccessTokenMinutes;
                options.SigningKey = jwtOptions.SigningKey;
            })
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = !environment.IsDevelopment();
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidAudience = jwtOptions.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var authorizationHeader = context.Request.Headers.Authorization.ToString();
                        if (authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        {
                            context.Token = authorizationHeader["Bearer ".Length..].Trim();
                        }

                        return Task.CompletedTask;
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = "application/problem+json";
                        await context.Response.WriteAsJsonAsync(new ProblemDetails
                        {
                            Title = "Authentication required",
                            Detail = "Your session is missing or expired. Sign in again to continue.",
                            Status = StatusCodes.Status401Unauthorized
                        });
                    },
                    OnForbidden = async context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        context.Response.ContentType = "application/problem+json";
                        await context.Response.WriteAsJsonAsync(new ProblemDetails
                        {
                            Title = "Forbidden",
                            Detail = "You do not have permission to perform this action.",
                            Status = StatusCodes.Status403Forbidden
                        });
                    }
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
                policy
                    .WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
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
                Description = "Paste the short-lived JWT access token returned by login or refresh. The refresh token lives in an HttpOnly cookie.",
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
        services.AddScoped<ICatalogImportService, CatalogImportService>();
        services.AddScoped<IDownloadQueueService, DownloadQueueService>();
        services.AddScoped<IMetadataSyncService, MetadataSyncService>();
        services.AddScoped<IPlaybackResolutionService, PlaybackResolutionService>();
        services.AddScoped<IPlaybackStreamProxyService, PlaybackStreamProxyService>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<IAnimeProviderClient, AnimeProviderClient>();
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

        services.AddHttpClient("stream-proxy-client", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(10);
        });

        services.AddTransient<LoggingDelegatingHandler>();
        services.AddScoped<IPasswordHasher<ApplicationUser>, PasswordHasher<ApplicationUser>>();

        // TimeProvider is registered now so time-sensitive services can be tested later without
        // hard-coding DateTime.UtcNow throughout the application layer.
        services.AddSingleton(TimeProvider.System);

        return services;
    }

    private static string? GetFirstModelValidationMessage(ModelStateDictionary modelState)
    {
        return modelState.Values
            .SelectMany(entry => entry.Errors)
            .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                ? "One of the submitted fields is invalid."
                : error.ErrorMessage)
            .FirstOrDefault();
    }

    private static JwtOptions BuildValidatedJwtOptions(IConfiguration configuration)
    {
        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        jwtOptions = new JwtOptions
        {
            Issuer = jwtOptions.Issuer,
            Audience = jwtOptions.Audience,
            AccessTokenMinutes = jwtOptions.AccessTokenMinutes,
            SigningKey = configuration["ASPNETCORE_JWT_SIGNINGKEY"]?.Trim() ?? string.Empty
        };

        if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey))
        {
            throw new InvalidOperationException("ASPNETCORE_JWT_SIGNINGKEY is missing. Set a real signing key through the environment or your secret store.");
        }

        if (jwtOptions.SigningKey.Length < 32)
        {
            throw new InvalidOperationException("ASPNETCORE_JWT_SIGNINGKEY must be at least 32 characters long.");
        }

        if (jwtOptions.AccessTokenMinutes <= 0)
        {
            throw new InvalidOperationException("Jwt:AccessTokenMinutes must be greater than zero.");
        }

        return jwtOptions;
    }
}

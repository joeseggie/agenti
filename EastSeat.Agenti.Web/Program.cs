using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using EastSeat.Agenti.Web.Components;
using EastSeat.Agenti.Web.Components.Account;
using EastSeat.Agenti.Web.Data;
using Serilog;
using Serilog.Events;
using Microsoft.ApplicationInsights.Extensibility;
using EastSeat.Agenti.Web.Features.Dashboard;
using EastSeat.Agenti.Web.Features.CashCounts;
using EastSeat.Agenti.Web.Features.CashSessions;
using EastSeat.Agenti.Web.Features.Agents;
using EastSeat.Agenti.Web.Features.WalletTypes;
using EastSeat.Agenti.Web.Features.Vaults;
using EastSeat.Agenti.Web.Features.Users;
using EastSeat.Agenti.Web.Features.Setup;
using EastSeat.Agenti.Web.Features.Theme;
using EastSeat.Agenti.Web.Features.Api;
using EastSeat.Agenti.Web.Features.Notifications;
using EastSeat.Agenti.Shared.Domain.Enums;
using MudBlazor.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
var serilogConfig = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "Agenti")
    .WriteTo.Console();

var serilogAiConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
if (!string.IsNullOrEmpty(serilogAiConnectionString))
{
    serilogConfig = serilogConfig.WriteTo.ApplicationInsights(
        serilogAiConnectionString,
        TelemetryConverter.Traces);
}

Log.Logger = serilogConfig.CreateLogger();

builder.Host.UseSerilog();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add MudBlazor services
builder.Services.AddMudServices();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

var authBuilder = builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    });

authBuilder.AddIdentityCookies();
authBuilder.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
{
    var jwtKey = builder.Configuration["Jwt:Key"];
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "EastSeat.Agenti",
        ValidAudience = builder.Configuration["Jwt:Audience"] ?? "EastSeat.Agenti.Android",
        IssuerSigningKey = string.IsNullOrWhiteSpace(jwtKey)
            ? null
            : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// Configure PostgreSQL
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

// Factory for services that need their own DbContext (avoids concurrency during Blazor prerendering)
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString), ServiceLifetime.Scoped);

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// Add Application Insights telemetry
var aiConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
if (!string.IsNullOrEmpty(aiConnectionString))
{
    builder.Services.AddApplicationInsightsTelemetry(options =>
    {
        options.ConnectionString = aiConnectionString;
        options.EnableAdaptiveSampling = builder.Configuration.GetValue<bool>("ApplicationInsights:EnableAdaptiveSampling", false);
        options.EnableDependencyTrackingTelemetryModule = true;
        options.EnableRequestTrackingTelemetryModule = true;
        options.EnableEventCounterCollectionModule = true;
    });

    // Configure sampling percentage for production
    if (builder.Environment.IsProduction())
    {
        var samplingPercentage = builder.Configuration.GetValue<double>("ApplicationInsights:SamplingPercentage", 100);
        builder.Services.Configure<TelemetryConfiguration>(config =>
        {
            config.DefaultTelemetrySink.TelemetryProcessorChainBuilder.UseAdaptiveSampling(
                maxTelemetryItemsPerSecond: 5,
                excludedTypes: "Exception");
        });
    }
}

builder.Services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

// Add claims transformation for BranchId
builder.Services.AddScoped<IClaimsTransformation, BranchIdClaimsTransformer>();

// Add application services
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<ICashCountService, CashCountService>();
builder.Services.AddScoped<ICashSessionService, CashSessionService>();
builder.Services.AddScoped<IAgentService, AgentService>();
builder.Services.AddScoped<IWalletTypeService, WalletTypeService>();
builder.Services.AddScoped<IVaultService, VaultService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ILoginTelemetryService, LoginTelemetryService>();
builder.Services.AddScoped<ISetupService, SetupService>();
builder.Services.AddScoped<IThemeService, ThemeService>();
builder.Services.AddScoped<INotificationService, NotificationService>();

// Add vault background service
builder.Services.AddHostedService<VaultExpirationService>();
// Add user audit cleanup background service
builder.Services.AddHostedService<UserAuditCleanupService>();

// Add authorization policies
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("VaultView", policy =>
        policy.RequireRole(UserRole.Admin.ToString(), UserRole.Supervisor.ToString())
              .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme, JwtBearerDefaults.AuthenticationScheme))
    .AddPolicy("VaultAccess", policy =>
        policy.RequireRole(UserRole.Admin.ToString(), UserRole.Supervisor.ToString())
              .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme, JwtBearerDefaults.AuthenticationScheme))
    .AddPolicy("VaultAdjust", policy =>
        policy.RequireRole(UserRole.Admin.ToString(), UserRole.Supervisor.ToString())
              .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme, JwtBearerDefaults.AuthenticationScheme))
    .AddPolicy("VaultApprove", policy =>
        policy.RequireRole(UserRole.Admin.ToString())
              .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme, JwtBearerDefaults.AuthenticationScheme))
    // Admin-only user management access
    .AddPolicy("UserManagement", policy =>
        policy.RequireRole(UserRole.Admin.ToString())
              .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme, JwtBearerDefaults.AuthenticationScheme))
    // Cash count approval access (Admin or Supervisor)
    .AddPolicy("CashCountApprove", policy =>
        policy.RequireRole(UserRole.Admin.ToString(), UserRole.Supervisor.ToString())
              .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme, JwtBearerDefaults.AuthenticationScheme));

// Add REST API support
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Agenti API",
        Version = "v1",
        Description = "REST API for the Agenti banking ERP system, supporting the Android mobile application."
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT bearer token."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            []
        }
    });
});

var app = builder.Build();

// Validate JWT key is configured (required for the Android API)
var jwtKey = app.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey))
{
    var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
    startupLogger.LogWarning(
        "JWT key is not configured. The REST API (/api/*) will not function. " +
        "Set the 'Jwt__Key' environment variable or configure it in appsettings.");
}

// Apply pending migrations on startup (for production deployments)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        logger.LogInformation("Applying database migrations...");
        await db.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error applying database migrations.");
        throw;
    }
}

// Initialize setup check at startup
using (var scope = app.Services.CreateScope())
{
    var setupService = scope.ServiceProvider.GetRequiredService<ISetupService>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        var isSetupComplete = await setupService.IsSetupCompleteAsync();

        if (!isSetupComplete)
        {
            // Only clean up if this is truly a fresh database (no data at all).
            // Check that no users exist to confirm it's not a transient failure.
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var hasAnyUsers = await db.Users.AnyAsync();

            if (hasAnyUsers)
            {
                logger.LogWarning(
                    "Setup appears incomplete but users exist in the database. " +
                    "Skipping cleanup to protect existing data. " +
                    "Manually verify setup status if needed.");
            }
            else
            {
                logger.LogInformation("Setup is required on a fresh database. Cleaning up for fresh start...");
                await setupService.CleanupDatabaseAsync();
                logger.LogInformation("Database cleanup completed. Setup flow will be triggered.");
            }
        }
        else
        {
            logger.LogInformation("Setup is already complete.");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error during startup setup check. Continuing without cleanup to protect data.");
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Agenti API v1");
        options.RoutePrefix = "api/docs";
    });
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

// Map REST API endpoints (for Android app)
var apiGroup = app.MapGroup("/api");

apiGroup.MapGroup("/auth")
    .WithTags("Authentication")
    .MapAuthApi();

apiGroup.MapGroup("/dashboard")
    .WithTags("Dashboard")
    .MapDashboardApi();

apiGroup.MapGroup("/agents")
    .WithTags("Agents")
    .MapAgentsApi();

apiGroup.MapGroup("/cash-counts")
    .WithTags("Cash Counts")
    .MapCashCountsApi();

apiGroup.MapGroup("/cash-sessions")
    .WithTags("Cash Sessions")
    .MapCashSessionsApi();

apiGroup.MapGroup("/vault")
    .WithTags("Vault")
    .MapVaultApi();

apiGroup.MapGroup("/wallet-types")
    .WithTags("Wallet Types")
    .MapWalletTypesApi();

// Redirect to setup page if setup is incomplete
// Use a separate scope to avoid DbContext concurrency with Blazor components
var setupCompleteFlag = false;
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;
    var isSetupPage = path.StartsWith("/setup-prerequisites", StringComparison.OrdinalIgnoreCase);
    var isStaticAsset = path.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase) ||
                       path.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase) ||
                       path.StartsWith("/_content", StringComparison.OrdinalIgnoreCase) ||
                       path.StartsWith("/css", StringComparison.OrdinalIgnoreCase) ||
                       path.StartsWith("/js", StringComparison.OrdinalIgnoreCase) ||
                       path.StartsWith("/lib", StringComparison.OrdinalIgnoreCase) ||
                       path.StartsWith("/favicon", StringComparison.OrdinalIgnoreCase);
    var isApiPath = path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);

    if (!setupCompleteFlag)
    {
        using var scope = context.RequestServices.CreateScope();
        var setupService = scope.ServiceProvider.GetRequiredService<ISetupService>();
        setupCompleteFlag = await setupService.IsSetupCompleteAsync();
    }

    if (!setupCompleteFlag && !isSetupPage && !isStaticAsset && !isApiPath)
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("Setup incomplete. Redirecting to setup page from {Path}", path);
        context.Response.Redirect("/setup-prerequisites");
        return;
    }

    if (setupCompleteFlag && isSetupPage)
    {
        context.Response.Redirect("/");
        return;
    }

    await next();
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

try
{
    Log.Information("Starting Agenti application");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

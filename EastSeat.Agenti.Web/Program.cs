using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using EastSeat.Agenti.Web.Components;
using EastSeat.Agenti.Web.Components.Account;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Dashboard;
using EastSeat.Agenti.Web.Features.CashCounts;
using EastSeat.Agenti.Web.Features.CashSessions;
using EastSeat.Agenti.Web.Features.Agents;
using EastSeat.Agenti.Web.Features.WalletTypes;
using EastSeat.Agenti.Web.Features.Vaults;
using EastSeat.Agenti.Web.Features.Users;
using EastSeat.Agenti.Web.Features.Setup;
using EastSeat.Agenti.Shared.Domain.Enums;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add MudBlazor services
builder.Services.AddMudServices();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// Configure PostgreSQL
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

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
builder.Services.AddScoped<ISetupService, SetupService>();

// Add vault background service
builder.Services.AddHostedService<VaultExpirationService>();
// Add user audit cleanup background service
builder.Services.AddHostedService<UserAuditCleanupService>();

// Add authorization policies
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("VaultView", policy => policy.RequireRole(UserRole.Admin.ToString(), UserRole.Supervisor.ToString()))
    .AddPolicy("VaultAccess", policy => policy.RequireRole(UserRole.Admin.ToString(), UserRole.Supervisor.ToString()))
    .AddPolicy("VaultAdjust", policy => policy.RequireRole(UserRole.Admin.ToString(), UserRole.Supervisor.ToString()))
    .AddPolicy("VaultApprove", policy => policy.RequireRole(UserRole.Admin.ToString()))
    // Admin-only user management access
    .AddPolicy("UserManagement", policy => policy.RequireRole(UserRole.Admin.ToString()));

var app = builder.Build();

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
            logger.LogInformation("Setup is required. Cleaning up database for fresh start...");
            await setupService.CleanupDatabaseAsync();
            logger.LogInformation("Database cleanup completed. Setup flow will be triggered.");
        }
        else
        {
            logger.LogInformation("Setup is already complete.");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error during startup setup check.");
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

// Redirect to setup page if setup is incomplete
app.Use(async (context, next) =>
{
    var setupService = context.RequestServices.GetRequiredService<ISetupService>();
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();

    var path = context.Request.Path.Value ?? string.Empty;
    var isSetupPage = path.StartsWith("/setup-prerequisites", StringComparison.OrdinalIgnoreCase);
    var isStaticAsset = path.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase) ||
                       path.StartsWith("/_content", StringComparison.OrdinalIgnoreCase) ||
                       path.StartsWith("/css", StringComparison.OrdinalIgnoreCase) ||
                       path.StartsWith("/js", StringComparison.OrdinalIgnoreCase) ||
                       path.StartsWith("/lib", StringComparison.OrdinalIgnoreCase) ||
                       path.StartsWith("/favicon", StringComparison.OrdinalIgnoreCase);

    var setupComplete = await setupService.IsSetupCompleteAsync();

    if (!setupComplete && !isSetupPage && !isStaticAsset)
    {
        logger.LogInformation("Setup incomplete. Redirecting to setup page from {Path}", path);
        context.Response.Redirect("/setup-prerequisites");
        return;
    }

    if (setupComplete && isSetupPage)
    {
        logger.LogInformation("Setup already complete. Redirecting to home from setup page.");
        context.Response.Redirect("/");
        return;
    }

    await next();
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

app.Run();

using System.Reflection;
using EastSeat.Agenti.Android.Converters;
using EastSeat.Agenti.Android.Pages;
using EastSeat.Agenti.Android.Services;
using EastSeat.Agenti.Android.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EastSeat.Agenti.Android;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // ── Configuration ────────────────────────────────────────────────────
        // Load appsettings.json (production) then overlay with
        // appsettings.Development.json in DEBUG builds.
        var assembly = Assembly.GetExecutingAssembly();
        using var appSettingsStream = assembly.GetManifestResourceStream("appsettings.json");
        if (appSettingsStream is not null)
        {
            builder.Configuration.AddJsonStream(appSettingsStream);
        }

#if DEBUG
        using var devSettingsStream = assembly.GetManifestResourceStream("appsettings.Development.json");
        if (devSettingsStream is not null)
        {
            builder.Configuration.AddJsonStream(devSettingsStream);
        }
#endif

        // ── HTTP client ──────────────────────────────────────────────────────
        var baseUrl = builder.Configuration.GetValue<string>("ApiSettings:BaseUrl")
            ?? "https://agenti.azurewebsites.net/";

        builder.Services.AddHttpClient<IApiService, ApiService>(client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .ConfigurePrimaryHttpMessageHandler(() =>
        {
            var handler = new HttpClientHandler();
#if DEBUG
            // Accept the ASP.NET Core dev certificate on the Android emulator.
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
#endif
            return handler;
        });

        // ── Services ─────────────────────────────────────────────────────────
        builder.Services.AddSingleton<IAuthService, AuthService>();

        // ── ViewModels ───────────────────────────────────────────────────────
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddTransient<AgentsViewModel>();
        builder.Services.AddTransient<CashCountViewModel>();
        builder.Services.AddTransient<CashSessionsViewModel>();
        builder.Services.AddTransient<VaultViewModel>();

        // ── Pages ────────────────────────────────────────────────────────────
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<AgentsPage>();
        builder.Services.AddTransient<CashCountPage>();
        builder.Services.AddTransient<CashSessionsPage>();
        builder.Services.AddTransient<ProfilePage>();

        // ── Shell ────────────────────────────────────────────────────────────
        builder.Services.AddSingleton<AppShell>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}

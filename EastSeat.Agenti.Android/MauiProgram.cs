using EastSeat.Agenti.Android.Converters;
using EastSeat.Agenti.Android.Pages;
using EastSeat.Agenti.Android.Services;
using EastSeat.Agenti.Android.ViewModels;
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

        // ── HTTP client ──────────────────────────────────────────────────────
        // Base address is read from appsettings/environment.
        // For production, configure this to your server URL.
        // For local development, use ngrok or your machine's local IP.
        builder.Services.AddHttpClient<IApiService, ApiService>(client =>
        {
            var baseUrl = "https://your-agenti-server.example.com/";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
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

        // ── Shell ────────────────────────────────────────────────────────────
        builder.Services.AddSingleton<AppShell>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}

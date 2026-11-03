using EastSeat.Agenti.Android.Pages;
using EastSeat.Agenti.Android.Services;
using Microsoft.Extensions.Logging;

namespace EastSeat.Agenti.Android;

public partial class App : Application
{
    private readonly IAuthService _authService;
    private readonly ILogger<App> _logger;

    public App(IAuthService authService, ILogger<App> logger)
    {
        InitializeComponent();
        _authService = authService;
        _logger = logger;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new ContentPage
        {
            Content = new ActivityIndicator { IsRunning = true, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
        });
        window.Created += async (_, _) => await RestoreAsync(window);
        return window;
    }

    private async Task RestoreAsync(Window window)
    {
        try
        {
            var restored = await _authService.TryRestoreSessionAsync();
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                window.Page = restored
                    ? (Page)IPlatformApplication.Current!.Services.GetRequiredService<AppShell>()
                    : IPlatformApplication.Current!.Services.GetRequiredService<LoginPage>();
            });
        }
        catch (Exception exception)
        {
            _logger.LogError("Application sign-in restoration failed with {ExceptionType}.", exception.GetType().Name);
            var retry = new Button { Text = "Retry sign-in initialization" };
            retry.Clicked += async (_, _) => { retry.IsEnabled = false; await RestoreAsync(window); };
            window.Page = new ContentPage
            {
                Content = new VerticalStackLayout
                {
                    Padding = 24, Spacing = 16,
                    Children = { new Label { Text = "Secure sign-in could not be initialized. Retry before using banking." }, retry }
                }
            };
        }
    }
}

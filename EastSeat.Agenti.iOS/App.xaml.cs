using EastSeat.Agenti.iOS.Pages;
using EastSeat.Agenti.iOS.Services;

namespace EastSeat.Agenti.iOS;

public partial class App : Application
{
    private readonly IAuthService _authService;

    public App(IAuthService authService)
    {
        InitializeComponent();
        _authService = authService;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Attempt to restore a previous session on app start
        var startPage = Task.Run(async () =>
        {
            var restored = await _authService.TryRestoreSessionAsync();
            return restored
                ? (Page)IPlatformApplication.Current!.Services.GetRequiredService<AppShell>()
                : (Page)IPlatformApplication.Current!.Services.GetRequiredService<LoginPage>();
        }).GetAwaiter().GetResult();

        return new Window(startPage);
    }
}

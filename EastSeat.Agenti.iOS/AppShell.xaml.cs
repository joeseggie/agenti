using EastSeat.Agenti.iOS.Services;
using System.Windows.Input;

namespace EastSeat.Agenti.iOS;

public partial class AppShell : Shell
{
    private readonly IAuthService _authService;

    public ICommand LogoutCommand { get; }

    public AppShell(IAuthService authService)
    {
        InitializeComponent();
        _authService = authService;
        LogoutCommand = new Command(async () => await PerformLogoutAsync());
        BindingContext = this;
    }

    private async Task PerformLogoutAsync()
    {
        await _authService.LogoutAsync();
        Application.Current!.MainPage = IPlatformApplication.Current!.Services.GetRequiredService<Pages.LoginPage>();
    }
}

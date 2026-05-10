using EastSeat.Agenti.iOS.Services;

namespace EastSeat.Agenti.iOS.Pages;

public partial class ProfilePage : ContentPage
{
    private readonly IAuthService _authService;

    public ProfilePage(IAuthService authService)
    {
        InitializeComponent();
        _authService = authService;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        var user = _authService.CurrentUser;
        if (user != null)
        {
            NameLabel.Text = user.FullName;
            EmailLabel.Text = user.Email;
        }
    }
}

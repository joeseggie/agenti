using EastSeat.Agenti.Android.ViewModels;

namespace EastSeat.Agenti.Android.Pages;

public partial class LoginPage : ContentPage
{
    private readonly LoginViewModel _viewModel;

    public LoginPage(LoginViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        _viewModel.Email = EmailEntry.Text ?? string.Empty;
        _viewModel.Password = PasswordEntry.Text ?? string.Empty;

        SetLoading(true);

        var success = await _viewModel.LoginAsync();

        SetLoading(false);

        if (success)
        {
            // Navigate to the main shell (dashboard)
            Application.Current!.Windows[0].Page =
                IPlatformApplication.Current!.Services.GetRequiredService<AppShell>();
        }
        else
        {
            ErrorLabel.Text = _viewModel.ErrorMessage;
            ErrorLabel.IsVisible = true;
        }
    }

    private void SetLoading(bool loading)
    {
        LoginButton.IsEnabled = !loading;
        LoadingIndicator.IsRunning = loading;
        LoadingIndicator.IsVisible = loading;
        ErrorLabel.IsVisible = false;
    }
}

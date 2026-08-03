using EastSeat.Agenti.iOS.Services;

namespace EastSeat.Agenti.iOS.ViewModels;

/// <summary>
/// ViewModel for the login page.
/// </summary>
public class LoginViewModel : BaseViewModel
{
    private readonly IAuthService _authService;
    private string _email = string.Empty;
    private string _password = string.Empty;

    public string Email
    {
        get => _email;
        set => SetProperty(ref _email, value);
    }

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    public LoginViewModel(IAuthService authService)
    {
        _authService = authService;
    }

    public async Task<bool> LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Please enter your email and password.";
            return false;
        }

        ErrorMessage = string.Empty;
        IsBusy = true;

        try
        {
            var success = await _authService.LoginAsync(Email, Password);
            if (!success)
                ErrorMessage = "Invalid email or password. Please try again.";
            return success;
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "Cannot connect to the server. Please check your internet connection.";
            return false;
        }
        catch (Exception)
        {
            ErrorMessage = "An unexpected error occurred. Please try again.";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

using EastSeat.Agenti.iOS.Models;
using EastSeat.Agenti.iOS.Services;

namespace EastSeat.Agenti.iOS.ViewModels;

/// <summary>
/// ViewModel for the agent dashboard.
/// </summary>
public class DashboardViewModel : BaseViewModel
{
    private readonly IApiService _apiService;
    private readonly IAuthService _authService;
    private Models.DashboardViewModel? _dashboard;

    public Models.DashboardViewModel? Dashboard
    {
        get => _dashboard;
        set => SetProperty(ref _dashboard, value);
    }

    public string WelcomeMessage =>
        _authService.CurrentUser is not null
            ? $"Welcome, {_authService.CurrentUser.FullName}"
            : "Welcome";

    public string UserRole => _authService.CurrentUser?.Role ?? string.Empty;

    public DashboardViewModel(IApiService apiService, IAuthService authService)
    {
        _apiService = apiService;
        _authService = authService;
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            var result = await _apiService.GetDashboardAsync();
            if (result?.Success == true)
                Dashboard = result.Data;
            else
                ErrorMessage = result?.Error ?? "Failed to load dashboard.";
        }
        catch (Exception)
        {
            ErrorMessage = "Unable to load dashboard. Check your connection.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

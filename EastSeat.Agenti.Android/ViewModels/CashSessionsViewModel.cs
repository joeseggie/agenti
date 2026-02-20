using EastSeat.Agenti.Android.Models;
using EastSeat.Agenti.Android.Services;

namespace EastSeat.Agenti.Android.ViewModels;

/// <summary>
/// ViewModel for the cash sessions list page.
/// </summary>
public class CashSessionsViewModel : BaseViewModel
{
    private readonly IApiService _apiService;
    private List<CashSessionListItem> _sessions = [];

    public List<CashSessionListItem> Sessions
    {
        get => _sessions;
        set => SetProperty(ref _sessions, value);
    }

    public CashSessionsViewModel(IApiService apiService)
    {
        _apiService = apiService;
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            var result = await _apiService.GetCashSessionsAsync();
            if (result?.Success == true)
                Sessions = result.Data ?? [];
            else
                ErrorMessage = result?.Error ?? "Failed to load sessions.";
        }
        catch (Exception)
        {
            ErrorMessage = "Unable to load sessions. Check your connection.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

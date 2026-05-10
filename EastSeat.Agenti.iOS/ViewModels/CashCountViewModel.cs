using EastSeat.Agenti.iOS.Models;
using EastSeat.Agenti.iOS.Services;

namespace EastSeat.Agenti.iOS.ViewModels;

/// <summary>
/// ViewModel for cash count (open/close session) page.
/// </summary>
public class CashCountViewModel : BaseViewModel
{
    private readonly IApiService _apiService;
    private CurrentSessionInfo? _currentSession;
    private CashCountForm? _form;
    private bool _isOpening;

    public CurrentSessionInfo? CurrentSession
    {
        get => _currentSession;
        set => SetProperty(ref _currentSession, value);
    }

    public CashCountForm? Form
    {
        get => _form;
        set => SetProperty(ref _form, value);
    }

    public bool IsOpening
    {
        get => _isOpening;
        set => SetProperty(ref _isOpening, value);
    }

    public string CountTitle => IsOpening ? "Opening Count" : "Closing Count";

    public CashCountViewModel(IApiService apiService)
    {
        _apiService = apiService;
    }

    public async Task LoadCurrentSessionAsync()
    {
        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            var result = await _apiService.GetCurrentSessionAsync();
            if (result?.Success == true)
                CurrentSession = result.Data;
            else
                ErrorMessage = result?.Error ?? "Failed to load session.";
        }
        catch (Exception)
        {
            ErrorMessage = "Unable to load session. Check your connection.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task InitializeFormAsync(bool isOpening)
    {
        IsOpening = isOpening;
        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            var result = await _apiService.InitializeCashCountAsync(isOpening);
            if (result?.Success == true)
                Form = result.Data;
            else
                ErrorMessage = result?.Error ?? "Failed to initialize form.";
        }
        catch (Exception)
        {
            ErrorMessage = "Unable to load form. Check your connection.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> SaveAndSubmitAsync()
    {
        if (Form is null) return false;

        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            var saveResult = await _apiService.SaveCashCountAsync(Form);
            if (saveResult?.Success != true)
            {
                ErrorMessage = saveResult?.Error ?? "Failed to save cash count.";
                return false;
            }

            var cashCountId = saveResult.Data?.CashCountId;
            if (cashCountId is null)
            {
                ErrorMessage = "Invalid response from server.";
                return false;
            }

            var submitResult = await _apiService.SubmitCashCountAsync(cashCountId.Value);
            if (submitResult?.Success != true)
            {
                ErrorMessage = submitResult?.Error ?? "Failed to submit cash count.";
                return false;
            }

            return true;
        }
        catch (Exception)
        {
            ErrorMessage = "Unable to save. Check your connection.";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

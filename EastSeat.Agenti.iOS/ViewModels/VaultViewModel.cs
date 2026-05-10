using EastSeat.Agenti.iOS.Models;
using EastSeat.Agenti.iOS.Services;

namespace EastSeat.Agenti.iOS.ViewModels;

/// <summary>
/// ViewModel for the vault management page.
/// </summary>
public class VaultViewModel : BaseViewModel
{
    private readonly IApiService _apiService;
    private VaultInfo? _vault;
    private List<VaultTransactionItem> _transactions = [];

    public VaultInfo? Vault
    {
        get => _vault;
        set => SetProperty(ref _vault, value);
    }

    public List<VaultTransactionItem> Transactions
    {
        get => _transactions;
        set => SetProperty(ref _transactions, value);
    }

    public VaultViewModel(IApiService apiService)
    {
        _apiService = apiService;
    }

    public async Task LoadAsync(long branchId)
    {
        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            var vaultResult = await _apiService.GetVaultAsync(branchId);
            if (vaultResult?.Success == true)
                Vault = vaultResult.Data;
            else
            {
                ErrorMessage = vaultResult?.Error ?? "Failed to load vault.";
                return;
            }

            var txResult = await _apiService.GetVaultTransactionsAsync(branchId);
            if (txResult?.Success == true)
                Transactions = txResult.Data ?? [];
        }
        catch (Exception)
        {
            ErrorMessage = "Unable to load vault. Check your connection.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

namespace EastSeat.Agenti.Web.Features.WalletTypes;

/// <summary>
/// Service interface for wallet type management operations.
/// </summary>
public interface IWalletTypeService
{
    /// <summary>
    /// Gets all wallet types with wallet counts.
    /// </summary>
    Task<List<WalletTypeListItemDto>> GetWalletTypesAsync();

    /// <summary>
    /// Gets a wallet type by ID.
    /// </summary>
    Task<WalletTypeListItemDto?> GetWalletTypeAsync(long id);

    /// <summary>
    /// Creates a new wallet type.
    /// </summary>
    Task<WalletTypeSaveResult> CreateAsync(WalletTypeFormModel model);

    /// <summary>
    /// Updates an existing wallet type.
    /// </summary>
    Task<WalletTypeSaveResult> UpdateAsync(WalletTypeFormModel model);

    /// <summary>
    /// Toggles wallet type active status.
    /// </summary>
    Task<WalletTypeSaveResult> ToggleStatusAsync(long id);

    /// <summary>
    /// Deletes a wallet type (only if no wallets exist with this type).
    /// </summary>
    Task<WalletTypeSaveResult> DeleteAsync(long id);
}

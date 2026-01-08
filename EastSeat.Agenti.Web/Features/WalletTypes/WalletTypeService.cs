using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.WalletTypes;

/// <summary>
/// Service implementation for wallet type management operations.
/// </summary>
public class WalletTypeService(ApplicationDbContext dbContext) : IWalletTypeService
{
    /// <inheritdoc />
    public async Task<List<WalletTypeListItemDto>> GetWalletTypesAsync()
    {
        return await dbContext.WalletTypes
            .OrderBy(wt => wt.Name)
            .Select(wt => new WalletTypeListItemDto
            {
                Id = wt.Id,
                Name = wt.Name,
                Description = wt.Description,
                Type = wt.Type,
                IsSystem = wt.IsSystem,
                IsActive = wt.IsActive,
                SupportsDenominations = wt.SupportsDenominations,
                WalletCount = wt.Wallets.Count,
                CreatedAt = wt.CreatedAt
            })
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<WalletTypeListItemDto?> GetWalletTypeAsync(long id)
    {
        return await dbContext.WalletTypes
            .Where(wt => wt.Id == id)
            .Select(wt => new WalletTypeListItemDto
            {
                Id = wt.Id,
                Name = wt.Name,
                Description = wt.Description,
                Type = wt.Type,
                IsSystem = wt.IsSystem,
                IsActive = wt.IsActive,
                SupportsDenominations = wt.SupportsDenominations,
                WalletCount = wt.Wallets.Count,
                CreatedAt = wt.CreatedAt
            })
            .FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task<WalletTypeSaveResult> CreateAsync(WalletTypeFormModel model)
    {
        // Check for duplicate name
        var duplicateExists = await dbContext.WalletTypes
            .AnyAsync(wt => wt.Name == model.Name);
        if (duplicateExists)
        {
            return WalletTypeSaveResult.Error($"Wallet type '{model.Name}' already exists.");
        }

        var walletType = new WalletType
        {
            Name = model.Name,
            Description = model.Description,
            Type = model.Type,
            IsSystem = false, // User-created types are not system types
            IsActive = model.IsActive,
            SupportsDenominations = model.SupportsDenominations,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.WalletTypes.Add(walletType);
        await dbContext.SaveChangesAsync();

        return WalletTypeSaveResult.Ok(walletType.Id);
    }

    /// <inheritdoc />
    public async Task<WalletTypeSaveResult> UpdateAsync(WalletTypeFormModel model)
    {
        if (!model.Id.HasValue)
        {
            return WalletTypeSaveResult.Error("Wallet type ID is required for update.");
        }

        var walletType = await dbContext.WalletTypes.FindAsync(model.Id.Value);
        if (walletType == null)
        {
            return WalletTypeSaveResult.Error("Wallet type not found.");
        }

        // Check for duplicate name (excluding current type)
        var duplicateExists = await dbContext.WalletTypes
            .AnyAsync(wt => wt.Name == model.Name && wt.Id != model.Id);
        if (duplicateExists)
        {
            return WalletTypeSaveResult.Error($"Wallet type '{model.Name}' already exists.");
        }

        walletType.Name = model.Name;
        walletType.Description = model.Description;
        walletType.Type = model.Type;
        walletType.SupportsDenominations = model.SupportsDenominations;
        walletType.IsActive = model.IsActive;
        walletType.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync();

        return WalletTypeSaveResult.Ok(walletType.Id);
    }

    /// <inheritdoc />
    public async Task<WalletTypeSaveResult> ToggleStatusAsync(long id)
    {
        var walletType = await dbContext.WalletTypes.FindAsync(id);
        if (walletType == null)
        {
            return WalletTypeSaveResult.Error("Wallet type not found.");
        }

        walletType.IsActive = !walletType.IsActive;
        walletType.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync();

        return WalletTypeSaveResult.Ok(walletType.Id);
    }

    /// <inheritdoc />
    public async Task<WalletTypeSaveResult> DeleteAsync(long id)
    {
        var walletType = await dbContext.WalletTypes
            .Include(wt => wt.Wallets)
            .FirstOrDefaultAsync(wt => wt.Id == id);

        if (walletType == null)
        {
            return WalletTypeSaveResult.Error("Wallet type not found.");
        }

        if (walletType.IsSystem)
        {
            return WalletTypeSaveResult.Error("System wallet types cannot be deleted.");
        }

        if (walletType.Wallets.Count > 0)
        {
            return WalletTypeSaveResult.Error("Cannot delete wallet type with existing wallets. Deactivate it instead.");
        }

        dbContext.WalletTypes.Remove(walletType);
        await dbContext.SaveChangesAsync();

        return WalletTypeSaveResult.Ok(id);
    }
}

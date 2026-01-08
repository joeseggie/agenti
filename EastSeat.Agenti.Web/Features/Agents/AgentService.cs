using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.Agents;

/// <summary>
/// Service implementation for agent management operations.
/// </summary>
public class AgentService(ApplicationDbContext dbContext) : IAgentService
{
    /// <inheritdoc />
    public async Task<List<AgentListItemDto>> GetAgentsAsync()
    {
        return await dbContext.Agents
            .Include(a => a.User)
            .OrderBy(a => a.Code)
            .Select(a => new AgentListItemDto
            {
                Id = a.Id,
                Code = a.Code,
                FullName = a.User != null ? (a.User.FirstName + " " + a.User.LastName).Trim() : "Unknown",
                Email = a.User != null ? a.User.Email : null,
                PhoneNumber = a.User != null ? a.User.PhoneNumber : null,
                IsActive = a.IsActive,
                WalletCount = a.Wallets.Count(w => w.IsActive),
                TotalBalance = a.Wallets.Where(w => w.IsActive).Sum(w => w.Balance)
            })
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<AgentDetailDto?> GetAgentAsync(long agentId)
    {
        var agent = await dbContext.Agents
            .Include(a => a.User)
            .Include(a => a.Wallets)
                .ThenInclude(w => w.WalletType)
            .Where(a => a.Id == agentId)
            .FirstOrDefaultAsync();

        if (agent == null) return null;

        return new AgentDetailDto
        {
            Id = agent.Id,
            Code = agent.Code,
            FirstName = agent.User?.FirstName ?? string.Empty,
            LastName = agent.User?.LastName ?? string.Empty,
            Email = agent.User?.Email,
            PhoneNumber = agent.User?.PhoneNumber,
            BranchId = agent.BranchId,
            IsActive = agent.IsActive,
            CreatedAt = agent.CreatedAt,
            Wallets = agent.Wallets
                .OrderBy(w => w.WalletType?.Name)
                .ThenBy(w => w.Name)
                .Select(w => new AgentWalletDto
                {
                    Id = w.Id,
                    Name = w.Name,
                    WalletTypeName = w.WalletType?.Name ?? "Unknown",
                    WalletType = w.WalletType?.Type ?? Shared.Domain.Enums.WalletTypeEnum.Custom,
                    Currency = w.Currency,
                    Balance = w.Balance,
                    IsActive = w.IsActive,
                    SupportsDenominations = w.WalletType?.SupportsDenominations ?? false,
                    CreatedAt = w.CreatedAt
                })
                .ToList()
        };
    }

    /// <inheritdoc />
    public async Task<List<AvailableUserDto>> GetAvailableUsersAsync()
    {
        return await dbContext.Users
            .Where(u => u.AgentId == null && u.IsActive)
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
            .Select(u => new AvailableUserDto
            {
                Id = u.Id,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Email = u.Email ?? string.Empty,
                PhoneNumber = u.PhoneNumber
            })
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<SaveResult> CreateAgentAsync(AgentFormModel model)
    {
        // Validate UserId is provided for new agents
        if (string.IsNullOrEmpty(model.UserId))
        {
            return SaveResult.Error("Please select a user to create an agent.");
        }

        // Check that the user exists and doesn't already have an agent
        var user = await dbContext.Users.FindAsync(model.UserId);
        if (user == null)
        {
            return SaveResult.Error("Selected user not found.");
        }

        if (user.AgentId != null)
        {
            return SaveResult.Error("Selected user is already linked to an agent.");
        }

        // Generate unique 4-letter code from user's name
        var code = await GenerateUniqueAgentCodeAsync(user.FirstName, user.LastName);

        // Use a transaction to ensure both Agent and User are updated together
        using var transaction = await dbContext.Database.BeginTransactionAsync();

        try
        {
            // Step 1: Create the Agent with UserId (profile info comes from User)
            var agent = new Agent
            {
                UserId = model.UserId,
                Code = code,
                BranchId = model.BranchId,
                IsActive = model.IsActive,
                CreatedAt = DateTimeOffset.UtcNow
            };

            dbContext.Agents.Add(agent);
            await dbContext.SaveChangesAsync();

            // Step 2: Update the ApplicationUser with the new AgentId
            user.AgentId = agent.Id;
            user.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync();

            await transaction.CommitAsync();

            return SaveResult.Ok(agent.Id);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return SaveResult.Error($"Failed to create agent: {ex.Message}");
        }
    }

    /// <summary>
    /// Generates a unique 4-letter uppercase code from the user's first and last name.
    /// Format: First 2 letters of FirstName + First 2 letters of LastName (e.g., "JODO" for John Doe)
    /// If the code already exists, appends a number (e.g., "JOD1", "JOD2", etc.)
    /// </summary>
    private async Task<string> GenerateUniqueAgentCodeAsync(string firstName, string lastName)
    {
        // Clean and normalize names (remove non-letters, uppercase)
        var cleanFirst = new string(firstName.Where(char.IsLetter).ToArray()).ToUpperInvariant();
        var cleanLast = new string(lastName.Where(char.IsLetter).ToArray()).ToUpperInvariant();

        // Get first 2 letters from each name (pad with 'X' if too short)
        var firstPart = cleanFirst.Length >= 2 ? cleanFirst[..2] : cleanFirst.PadRight(2, 'X');
        var lastPart = cleanLast.Length >= 2 ? cleanLast[..2] : cleanLast.PadRight(2, 'X');

        var baseCode = firstPart + lastPart;

        // Check if base code is available
        if (!await dbContext.Agents.AnyAsync(a => a.Code == baseCode))
        {
            return baseCode;
        }

        // If base code exists, try with numeric suffix
        // Get all existing codes that start with the first 3 letters
        var prefix = baseCode[..3];
        var existingCodes = await dbContext.Agents
            .Where(a => a.Code.StartsWith(prefix))
            .Select(a => a.Code)
            .ToListAsync();

        // Find the next available number suffix (1-9, then A-Z)
        for (int i = 1; i <= 9; i++)
        {
            var candidateCode = prefix + i.ToString();
            if (!existingCodes.Contains(candidateCode))
            {
                return candidateCode;
            }
        }

        // If 1-9 are taken, use A-Z
        for (char c = 'A'; c <= 'Z'; c++)
        {
            var candidateCode = prefix + c;
            if (!existingCodes.Contains(candidateCode))
            {
                return candidateCode;
            }
        }

        // Last resort: use timestamp-based unique suffix
        return prefix + DateTime.UtcNow.Ticks.ToString()[^1];
    }

    /// <inheritdoc />
    public async Task<SaveResult> UpdateAgentAsync(AgentFormModel model)
    {
        if (!model.Id.HasValue)
        {
            return SaveResult.Error("Agent ID is required for update.");
        }

        var agent = await dbContext.Agents.FindAsync(model.Id.Value);
        if (agent == null)
        {
            return SaveResult.Error("Agent not found.");
        }

        // Check for duplicate code (excluding current agent)
        var duplicateExists = await dbContext.Agents
            .AnyAsync(a => a.Code == model.Code && a.Id != model.Id);
        if (duplicateExists)
        {
            return SaveResult.Error($"Agent code '{model.Code}' already exists.");
        }

        agent.Code = model.Code;
        agent.BranchId = model.BranchId;
        agent.IsActive = model.IsActive;
        agent.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync();

        return SaveResult.Ok(agent.Id);
    }

    /// <inheritdoc />
    public async Task<SaveResult> ToggleAgentStatusAsync(long agentId)
    {
        var agent = await dbContext.Agents.FindAsync(agentId);
        if (agent == null)
        {
            return SaveResult.Error("Agent not found.");
        }

        agent.IsActive = !agent.IsActive;
        agent.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync();

        return SaveResult.Ok(agent.Id);
    }

    /// <inheritdoc />
    public async Task<List<WalletTypeDto>> GetWalletTypesAsync()
    {
        return await dbContext.WalletTypes
            .Where(wt => wt.IsActive)
            .OrderBy(wt => wt.Name)
            .Select(wt => new WalletTypeDto
            {
                Id = wt.Id,
                Name = wt.Name,
                Description = wt.Description,
                Type = wt.Type,
                SupportsDenominations = wt.SupportsDenominations
            })
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<SaveResult> AddWalletAsync(WalletFormModel model)
    {
        var agent = await dbContext.Agents.FindAsync(model.AgentId);
        if (agent == null)
        {
            return SaveResult.Error("Agent not found.");
        }

        var walletType = await dbContext.WalletTypes.FindAsync(model.WalletTypeId);
        if (walletType == null)
        {
            return SaveResult.Error("Wallet type not found.");
        }

        var wallet = new Wallet
        {
            AgentId = model.AgentId,
            WalletTypeId = model.WalletTypeId,
            Name = model.Name,
            Currency = model.Currency,
            Balance = model.InitialBalance,
            IsActive = model.IsActive,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.Wallets.Add(wallet);
        await dbContext.SaveChangesAsync();

        return SaveResult.Ok(wallet.Id);
    }

    /// <inheritdoc />
    public async Task<SaveResult> UpdateWalletAsync(WalletFormModel model)
    {
        if (!model.Id.HasValue)
        {
            return SaveResult.Error("Wallet ID is required for update.");
        }

        var wallet = await dbContext.Wallets.FindAsync(model.Id.Value);
        if (wallet == null)
        {
            return SaveResult.Error("Wallet not found.");
        }

        wallet.Name = model.Name;
        wallet.WalletTypeId = model.WalletTypeId;
        wallet.Currency = model.Currency;
        wallet.IsActive = model.IsActive;
        wallet.UpdatedAt = DateTimeOffset.UtcNow;

        // Note: Balance is not updated here - it should only change via transactions/counts

        await dbContext.SaveChangesAsync();

        return SaveResult.Ok(wallet.Id);
    }

    /// <inheritdoc />
    public async Task<SaveResult> ToggleWalletStatusAsync(long walletId)
    {
        var wallet = await dbContext.Wallets.FindAsync(walletId);
        if (wallet == null)
        {
            return SaveResult.Error("Wallet not found.");
        }

        wallet.IsActive = !wallet.IsActive;
        wallet.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync();

        return SaveResult.Ok(wallet.Id);
    }

    /// <inheritdoc />
    public async Task<SaveResult> DeleteWalletAsync(long walletId)
    {
        var wallet = await dbContext.Wallets
            .Include(w => w.TransactionsFrom)
            .Include(w => w.TransactionsTo)
            .FirstOrDefaultAsync(w => w.Id == walletId);

        if (wallet == null)
        {
            return SaveResult.Error("Wallet not found.");
        }

        if (wallet.Balance != 0)
        {
            return SaveResult.Error("Cannot delete wallet with non-zero balance.");
        }

        if (wallet.TransactionsFrom.Any() || wallet.TransactionsTo.Any())
        {
            return SaveResult.Error("Cannot delete wallet with transaction history. Consider deactivating instead.");
        }

        // Check for cash count details
        var hasCountDetails = await dbContext.CashCountDetails.AnyAsync(d => d.WalletId == walletId);
        if (hasCountDetails)
        {
            return SaveResult.Error("Cannot delete wallet with cash count history. Consider deactivating instead.");
        }

        dbContext.Wallets.Remove(wallet);
        await dbContext.SaveChangesAsync();

        return SaveResult.Ok(walletId);
    }
}

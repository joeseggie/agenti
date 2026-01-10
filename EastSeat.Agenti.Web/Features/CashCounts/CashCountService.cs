using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.CashCounts;

/// <summary>
/// Service implementation for cash count operations.
/// </summary>
public class CashCountService(ApplicationDbContext dbContext) : ICashCountService
{
    /// <inheritdoc />
    public async Task<CurrentSessionDto> GetCurrentSessionAsync(string userId)
    {
        // Get current user's agent ID
        var agentId = await GetAgentIdForUserAsync(userId);
        if (agentId == null)
        {
            return new CurrentSessionDto
            {
                StatusText = "User not configured as an agent",
                StatusColor = "error",
                CanPerformOpeningCount = false,
                CanPerformClosingCount = false
            };
        }

        // Find the agent's current open session (only one allowed at a time)
        var openSession = await dbContext.CashSessions
            .Include(s => s.CashCounts)
            .Where(s => s.AgentId == agentId && s.Status == CashSessionStatus.Open)
            .FirstOrDefaultAsync();

        if (openSession == null)
        {
            // No open session - agent can start a new one with an opening count
            return new CurrentSessionDto
            {
                StatusText = "No open session",
                StatusColor = "info",
                CanPerformOpeningCount = true,
                CanPerformClosingCount = false
            };
        }

        var hasOpeningCount = openSession.CashCounts.Any(c => c.IsOpening && c.SubmittedAt.HasValue);
        var hasClosingCount = openSession.CashCounts.Any(c => !c.IsOpening && c.SubmittedAt.HasValue);

        return new CurrentSessionDto
        {
            SessionId = openSession.Id,
            SessionDate = openSession.SessionDate,
            StatusText = "Session Open",
            StatusColor = "success",
            HasOpeningCount = hasOpeningCount,
            HasClosingCount = hasClosingCount,
            // Cannot perform opening count if session is already open (it already has one)
            CanPerformOpeningCount = false,
            // Can perform closing count only if opening count is submitted
            CanPerformClosingCount = hasOpeningCount && !hasClosingCount
        };
    }

    /// <inheritdoc />
    public async Task<CashCountFormModel> InitializeCashCountFormAsync(string userId, bool isOpening)
    {
        var agentId = await GetAgentIdForUserAsync(userId);
        if (agentId == null)
        {
            return new CashCountFormModel { IsOpening = isOpening };
        }

        // Get wallets assigned to this agent
        var wallets = await dbContext.Wallets
            .Include(w => w.WalletType)
            .Where(w => w.AgentId == agentId && w.IsActive)
            .OrderBy(w => w.WalletType!.Name)
            .ThenBy(w => w.Name)
            .ToListAsync();

        var walletEntries = wallets.Select(w => new WalletCountEntryDto
        {
            WalletId = w.Id,
            WalletName = w.Name,
            WalletTypeName = w.WalletType?.Name ?? "Unknown",
            SupportsDenominations = w.WalletType?.SupportsDenominations ?? false,
            ExpectedBalance = w.Balance,
            CountedAmount = 0,
            Denominations = w.WalletType?.SupportsDenominations == true
                ? DenominationBreakdown.Empty
                : null
        }).ToList();

        return new CashCountFormModel
        {
            IsOpening = isOpening,
            WalletEntries = walletEntries
        };
    }

    /// <inheritdoc />
    public async Task<CashCountSaveResult> SaveCashCountAsync(string userId, CashCountFormModel form)
    {
        var agentId = await GetAgentIdForUserAsync(userId);
        if (agentId == null)
        {
            return CashCountSaveResult.Error("User is not configured as an agent.");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Find the agent's current open session
        var session = await dbContext.CashSessions
            .Include(s => s.CashCounts)
            .Where(s => s.AgentId == agentId && s.Status == CashSessionStatus.Open)
            .FirstOrDefaultAsync();

        if (form.IsOpening)
        {
            if (session != null)
            {
                return CashCountSaveResult.Error("An open session already exists. Please close it before starting a new one.");
            }

            // Create new session for opening count
            session = new CashSession
            {
                AgentId = agentId.Value,
                SessionDate = today,
                Status = CashSessionStatus.Open,
                OpenedAt = DateTimeOffset.UtcNow
            };
            dbContext.CashSessions.Add(session);
            await dbContext.SaveChangesAsync();
        }
        else
        {
            // Closing count requires an open session
            if (session == null)
            {
                return CashCountSaveResult.Error("No open session found. Please perform an opening count first.");
            }
        }

        // Check if count already exists
        var existingCount = session.CashCounts.FirstOrDefault(c => c.IsOpening == form.IsOpening);
        if (existingCount != null && existingCount.SubmittedAt.HasValue)
        {
            return CashCountSaveResult.Error($"{(form.IsOpening ? "Opening" : "Closing")} count has already been submitted.");
        }

        // Create or update count
        var cashCount = existingCount ?? new CashCount
        {
            CashSessionId = session.Id,
            IsOpening = form.IsOpening,
            CreatedAt = DateTimeOffset.UtcNow
        };

        cashCount.TotalAmount = form.TotalAmount;

        if (existingCount == null)
        {
            dbContext.CashCounts.Add(cashCount);
            await dbContext.SaveChangesAsync(); // Get the ID
        }

        // Remove existing details and add new ones
        if (existingCount != null)
        {
            var existingDetails = await dbContext.CashCountDetails
                .Where(d => d.CashCountId == cashCount.Id)
                .ToListAsync();
            dbContext.CashCountDetails.RemoveRange(existingDetails);
        }

        // Add new details
        foreach (var entry in form.WalletEntries)
        {
            var detail = new CashCountDetail
            {
                CashCountId = cashCount.Id,
                WalletId = entry.WalletId,
                Amount = entry.CountedAmount,
                Denominations = entry.Denominations?.ToJson()
            };
            dbContext.CashCountDetails.Add(detail);
        }

        await dbContext.SaveChangesAsync();

        return CashCountSaveResult.Ok(cashCount.Id, session.Id);
    }

    /// <inheritdoc />
    public async Task<CashCountSaveResult> SubmitCashCountAsync(string userId, long cashCountId)
    {
        var agentId = await GetAgentIdForUserAsync(userId);
        if (agentId == null)
        {
            return CashCountSaveResult.Error("User is not configured as an agent.");
        }

        var cashCount = await dbContext.CashCounts
            .Include(c => c.CashSession)
            .Include(c => c.Details)
                .ThenInclude(d => d.Wallet)
            .Where(c => c.Id == cashCountId && c.CashSession!.AgentId == agentId)
            .FirstOrDefaultAsync();

        if (cashCount == null)
        {
            return CashCountSaveResult.Error("Cash count not found.");
        }

        if (cashCount.SubmittedAt.HasValue)
        {
            return CashCountSaveResult.Error("Cash count has already been submitted.");
        }

        cashCount.SubmittedAt = DateTimeOffset.UtcNow;

        // For opening count, update wallet balances and auto-approve
        if (cashCount.IsOpening)
        {
            foreach (var detail in cashCount.Details)
            {
                if (detail.Wallet != null)
                {
                    detail.Wallet.Balance = detail.Amount;
                    detail.Wallet.UpdatedAt = DateTimeOffset.UtcNow;
                }
            }
            cashCount.ApprovedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            // Closing count: close the session
            cashCount.CashSession!.Status = CashSessionStatus.Closed;
            cashCount.CashSession.ClosedAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync();

        return CashCountSaveResult.Ok(cashCount.Id, cashCount.CashSessionId);
    }

    /// <inheritdoc />
    public async Task<CashCountFormModel?> GetCashCountFormAsync(string userId, long cashCountId)
    {
        var agentId = await GetAgentIdForUserAsync(userId);
        if (agentId == null)
        {
            return null;
        }

        var cashCount = await dbContext.CashCounts
            .Include(c => c.CashSession)
            .Include(c => c.Details)
                .ThenInclude(d => d.Wallet)
                    .ThenInclude(w => w!.WalletType)
            .Where(c => c.Id == cashCountId && c.CashSession!.AgentId == agentId)
            .FirstOrDefaultAsync();

        if (cashCount == null)
        {
            return null;
        }

        var walletEntries = cashCount.Details.Select(d => new WalletCountEntryDto
        {
            WalletId = d.WalletId,
            WalletName = d.Wallet?.Name ?? "Unknown",
            WalletTypeName = d.Wallet?.WalletType?.Name ?? "Unknown",
            SupportsDenominations = d.Wallet?.WalletType?.SupportsDenominations ?? false,
            ExpectedBalance = d.Wallet?.Balance ?? 0,
            CountedAmount = d.Amount,
            Denominations = DenominationBreakdown.FromJson(d.Denominations)
        }).ToList();

        return new CashCountFormModel
        {
            CashCountId = cashCount.Id,
            CashSessionId = cashCount.CashSessionId,
            IsOpening = cashCount.IsOpening,
            WalletEntries = walletEntries
        };
    }

    /// <summary>
    /// Gets the agent ID for a user from the ApplicationUser.AgentId field.
    /// </summary>
    private async Task<long?> GetAgentIdForUserAsync(string userId)
    {
        var user = await dbContext.Users.FindAsync(userId);
        if (user == null) return null;

        // Return the AgentId from the ApplicationUser
        return user.AgentId;
    }
}

using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Web.Data;
using Microsoft.ApplicationInsights;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.PendingTransactions;

/// <summary>
/// Service for recording and tracking pending (failed) transactions.
/// </summary>
public class PendingTransactionService(
    ApplicationDbContext dbContext,
    TelemetryClient? telemetryClient = null) : IPendingTransactionService
{
    /// <inheritdoc />
    public async Task<PendingTransactionSaveResult> RecordPendingTransactionAsync(
        string userId, PendingTransactionFormModel form)
    {
        var agent = await GetAgentForUserAsync(userId);
        if (agent == null)
        {
            return PendingTransactionSaveResult.Error("User is not configured as an agent.");
        }

        if (!agent.BranchId.HasValue)
        {
            return PendingTransactionSaveResult.Error("Agent is not assigned to a branch.");
        }

        var branchId = agent.BranchId.Value;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var session = await dbContext.CashSessions
            .Where(s => s.BranchId == branchId && s.SessionDate == today && s.Status == CashSessionStatus.Open)
            .FirstOrDefaultAsync();

        if (session == null)
        {
            return PendingTransactionSaveResult.Error(
                "No active cash session found. A session must be open to record a pending transaction.");
        }

        if (form.Amount <= 0)
        {
            return PendingTransactionSaveResult.Error("Amount must be greater than zero.");
        }

        var wallet = await dbContext.Wallets
            .Include(w => w.WalletType)
            .FirstOrDefaultAsync(w => w.Id == form.WalletId && w.AgentId == agent.Id && w.IsActive);

        if (wallet == null)
        {
            return PendingTransactionSaveResult.Error("Wallet not found or does not belong to this agent.");
        }

        if (string.IsNullOrWhiteSpace(form.Notes) || form.Notes.Trim().Length < 10)
        {
            return PendingTransactionSaveResult.Error("Notes are required (minimum 10 characters).");
        }

        var pendingTransaction = new PendingTransaction
        {
            CashSessionId = session.Id,
            AgentId = agent.Id,
            WalletId = form.WalletId,
            Type = form.Type,
            Status = PendingTransactionStatus.Open,
            Amount = form.Amount,
            Currency = wallet.Currency,
            CustomerAccountNumber = form.CustomerAccountNumber?.Trim(),
            Notes = form.Notes.Trim(),
            RecordedByUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.PendingTransactions.Add(pendingTransaction);
        await dbContext.SaveChangesAsync();

        telemetryClient?.TrackEvent("pending_transaction_recorded", new Dictionary<string, string>
        {
            { "AgentId", agent.Id.ToString() },
            { "SessionId", session.Id.ToString() },
            { "WalletId", form.WalletId.ToString() },
            { "Type", form.Type.ToString() },
            { "Amount", form.Amount.ToString("F2") }
        });

        return PendingTransactionSaveResult.Ok(pendingTransaction.Id);
    }

    /// <inheritdoc />
    public async Task<PendingTransactionSaveResult> UpdatePendingTransactionAsync(
        string userId, long pendingTransactionId, PendingTransactionUpdateModel update)
    {
        var pendingTransaction = await dbContext.PendingTransactions
            .Include(t => t.Agent)
            .FirstOrDefaultAsync(t => t.Id == pendingTransactionId);

        if (pendingTransaction == null)
        {
            return PendingTransactionSaveResult.Error("Pending transaction not found.");
        }

        // Authorization: only the recording agent or an Admin/Supervisor of the same branch can update
        var caller = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (caller == null)
        {
            return PendingTransactionSaveResult.Error("Caller not found.");
        }

        bool isAdminOrSupervisor = caller.Role == UserRole.Admin || caller.Role == UserRole.Supervisor;
        bool isRecordingAgent = pendingTransaction.RecordedByUserId == userId;

        if (!isAdminOrSupervisor && !isRecordingAgent)
        {
            return PendingTransactionSaveResult.Error("You are not authorized to update this pending transaction.");
        }

        if (pendingTransaction.Status == PendingTransactionStatus.Resolved ||
            pendingTransaction.Status == PendingTransactionStatus.Cancelled)
        {
            return PendingTransactionSaveResult.Error("Cannot update a resolved or cancelled pending transaction.");
        }

        // Validate status transition
        if (update.NewStatus.HasValue)
        {
            var validTransition = (pendingTransaction.Status, update.NewStatus.Value) switch
            {
                (PendingTransactionStatus.Open, PendingTransactionStatus.ReportedToBank) => true,
                (PendingTransactionStatus.Open, PendingTransactionStatus.Resolved) => true,
                (PendingTransactionStatus.Open, PendingTransactionStatus.Cancelled) => true,
                (PendingTransactionStatus.ReportedToBank, PendingTransactionStatus.Resolved) => true,
                (PendingTransactionStatus.ReportedToBank, PendingTransactionStatus.Cancelled) => true,
                _ => false
            };

            if (!validTransition)
            {
                return PendingTransactionSaveResult.Error(
                    $"Cannot transition from {pendingTransaction.Status} to {update.NewStatus.Value}.");
            }

            if (update.NewStatus.Value == PendingTransactionStatus.ReportedToBank &&
                string.IsNullOrWhiteSpace(update.TicketNumber))
            {
                return PendingTransactionSaveResult.Error(
                    "A ticket number is required when marking a pending transaction as reported to the bank.");
            }

            if (update.NewStatus.Value is PendingTransactionStatus.Resolved or PendingTransactionStatus.Cancelled &&
                (string.IsNullOrWhiteSpace(update.ResolutionNotes) || update.ResolutionNotes.Trim().Length < 10))
            {
                return PendingTransactionSaveResult.Error(
                    "Resolution notes are required (minimum 10 characters) when resolving or cancelling.");
            }

            pendingTransaction.Status = update.NewStatus.Value;

            if (update.NewStatus.Value is PendingTransactionStatus.Resolved or PendingTransactionStatus.Cancelled)
            {
                pendingTransaction.ResolvedAt = DateTimeOffset.UtcNow;
            }
        }

        if (!string.IsNullOrWhiteSpace(update.TicketNumber))
        {
            pendingTransaction.TicketNumber = update.TicketNumber.Trim();
        }

        if (!string.IsNullOrWhiteSpace(update.ReceiptPhotoPath))
        {
            pendingTransaction.ReceiptPhotoPath = update.ReceiptPhotoPath.Trim();
        }

        if (!string.IsNullOrWhiteSpace(update.Notes))
        {
            if (update.Notes.Trim().Length < 10)
            {
                return PendingTransactionSaveResult.Error("Notes must be at least 10 characters.");
            }
            pendingTransaction.Notes = update.Notes.Trim();
        }

        if (!string.IsNullOrWhiteSpace(update.ResolutionNotes))
        {
            pendingTransaction.ResolutionNotes = update.ResolutionNotes.Trim();
        }

        pendingTransaction.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync();

        telemetryClient?.TrackEvent("pending_transaction_updated", new Dictionary<string, string>
        {
            { "PendingTransactionId", pendingTransactionId.ToString() },
            { "UserId", userId },
            { "NewStatus", update.NewStatus?.ToString() ?? "unchanged" }
        });

        return PendingTransactionSaveResult.Ok(pendingTransactionId);
    }

    /// <inheritdoc />
    public async Task<List<PendingTransactionDto>> GetPendingTransactionsForAgentAsync(string userId)
    {
        var agent = await GetAgentForUserAsync(userId);
        if (agent == null || !agent.BranchId.HasValue)
        {
            return [];
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var session = await dbContext.CashSessions
            .Where(s => s.BranchId == agent.BranchId.Value && s.SessionDate == today && s.Status == CashSessionStatus.Open)
            .FirstOrDefaultAsync();

        if (session == null)
        {
            return [];
        }

        return await dbContext.PendingTransactions
            .Include(t => t.Wallet)
                .ThenInclude(w => w!.WalletType)
            .Include(t => t.Agent)
                .ThenInclude(a => a!.User)
            .Where(t => t.CashSessionId == session.Id && t.AgentId == agent.Id)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => MapToDto(t))
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<List<PendingTransactionDto>> GetOpenPendingTransactionsForBranchAsync(string userId, long branchId)
    {
        var caller = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (caller == null || (caller.Role != UserRole.Admin && caller.Role != UserRole.Supervisor))
        {
            return [];
        }

        return await dbContext.PendingTransactions
            .Include(t => t.Wallet)
                .ThenInclude(w => w!.WalletType)
            .Include(t => t.Agent)
                .ThenInclude(a => a!.User)
            .Include(t => t.CashSession)
            .Where(t => t.CashSession!.BranchId == branchId &&
                        (t.Status == PendingTransactionStatus.Open ||
                         t.Status == PendingTransactionStatus.ReportedToBank))
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => MapToDto(t))
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<List<PendingTransactionDto>> GetAllPendingTransactionsForBranchAsync(
        string userId, long branchId, long? agentId = null)
    {
        var caller = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (caller == null || (caller.Role != UserRole.Admin && caller.Role != UserRole.Supervisor))
        {
            return [];
        }

        var query = dbContext.PendingTransactions
            .Include(t => t.Wallet)
                .ThenInclude(w => w!.WalletType)
            .Include(t => t.Agent)
                .ThenInclude(a => a!.User)
            .Include(t => t.CashSession)
            .Where(t => t.CashSession!.BranchId == branchId);

        if (agentId.HasValue)
        {
            query = query.Where(t => t.AgentId == agentId.Value);
        }

        return await query
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => MapToDto(t))
            .ToListAsync();
    }

    private async Task<Agent?> GetAgentForUserAsync(string userId)
    {
        return await dbContext.Agents
            .Include(a => a.User)
            .FirstOrDefaultAsync(a => a.UserId == userId);
    }

    /// <inheritdoc />
    public async Task<List<PendingTransactionWalletDto>> GetAgentWalletsForUserAsync(string userId)
    {
        var agent = await GetAgentForUserAsync(userId);
        if (agent == null)
        {
            return [];
        }

        return await dbContext.Wallets
            .Include(w => w.WalletType)
            .Where(w => w.AgentId == agent.Id && w.IsActive)
            .OrderBy(w => w.Name)
            .Select(w => new PendingTransactionWalletDto
            {
                WalletId = w.Id,
                Name = w.Name,
                WalletTypeName = w.WalletType != null ? w.WalletType.Name : "Unknown"
            })
            .ToListAsync();
    }

    private static PendingTransactionDto MapToDto(PendingTransaction t) => new()
    {
        Id = t.Id,
        WalletId = t.WalletId,
        WalletName = t.Wallet != null ? t.Wallet.Name : "Unknown",
        WalletTypeName = t.Wallet != null && t.Wallet.WalletType != null ? t.Wallet.WalletType.Name : "Unknown",
        AgentName = t.Agent != null && t.Agent.User != null
            ? $"{t.Agent.User.FirstName} {t.Agent.User.LastName}".Trim()
            : "Unknown",
        AgentCode = t.Agent != null ? t.Agent.Code : "N/A",
        Type = t.Type,
        Status = t.Status,
        Amount = t.Amount,
        Currency = t.Currency,
        CustomerAccountNumber = t.CustomerAccountNumber,
        TicketNumber = t.TicketNumber,
        ReceiptPhotoPath = t.ReceiptPhotoPath,
        Notes = t.Notes,
        ResolutionNotes = t.ResolutionNotes,
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt,
        ResolvedAt = t.ResolvedAt
    };
}

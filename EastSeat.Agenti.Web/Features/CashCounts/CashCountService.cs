using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Notifications;
using EastSeat.Agenti.Web.Features.Vaults;
using Microsoft.ApplicationInsights;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.CashCounts;

/// <summary>
/// Service implementation for cash count operations with approval workflow.
/// Implements all 25 rules from issue #43.
/// </summary>
public class CashCountService(
    ApplicationDbContext dbContext,
    IVaultService vaultService,
    INotificationService notificationService,
    TelemetryClient? telemetryClient = null) : ICashCountService
{
    /// <inheritdoc />
    public async Task<CurrentSessionDto> GetCurrentSessionAsync(string userId)
    {
        var agent = await GetAgentForUserAsync(userId);
        if (agent == null)
        {
            return new CurrentSessionDto
            {
                StatusText = "User not configured as an agent",
                StatusColor = "error",
                CanPerformOpeningCount = false,
                CanPerformClosingCount = false
            };
        }

        if (!agent.BranchId.HasValue)
        {
            return new CurrentSessionDto
            {
                StatusText = "Agent not assigned to a branch",
                StatusColor = "error",
                CanPerformOpeningCount = false,
                CanPerformClosingCount = false
            };
        }

        var branchId = agent.BranchId.Value;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Rule 14: Check if agent has any cash count in PendingApproval status
        var hasPendingApproval = await dbContext.CashCounts
            .AnyAsync(c => c.AgentId == agent.Id && c.Status == CashCountStatus.PendingApproval);

        if (hasPendingApproval)
        {
            return new CurrentSessionDto
            {
                StatusText = "Cash count pending approval",
                StatusColor = "warning",
                HasPendingApproval = true,
                CanPerformOpeningCount = false,
                CanPerformClosingCount = false,
                BlockReason = "You have a cash count pending admin approval. Please wait for it to be processed."
            };
        }

        // Rule 4: Check for unclosed sessions from previous days
        var previousUnclosedSession = await dbContext.CashSessions
            .Where(s => s.BranchId == branchId &&
                        s.SessionDate < today &&
                        s.Status != CashSessionStatus.Closed)
            .OrderByDescending(s => s.SessionDate)
            .FirstOrDefaultAsync();

        if (previousUnclosedSession != null)
        {
            return new CurrentSessionDto
            {
                SessionId = previousUnclosedSession.Id,
                SessionDate = previousUnclosedSession.SessionDate,
                StatusText = "Previous session not closed",
                StatusColor = "error",
                CanPerformOpeningCount = false,
                CanPerformClosingCount = false,
                BlockReason = $"The cash session from {previousUnclosedSession.SessionDate:MMM dd, yyyy} has not been closed. Please contact an admin."
            };
        }

        // Find today's branch session
        var todaySession = await dbContext.CashSessions
            .Include(s => s.CashCounts.Where(c => c.AgentId == agent.Id))
            .Where(s => s.BranchId == branchId && s.SessionDate == today)
            .FirstOrDefaultAsync();

        if (todaySession == null)
        {
            return new CurrentSessionDto
            {
                StatusText = "No open session",
                StatusColor = "info",
                CanPerformOpeningCount = true,
                CanPerformClosingCount = false
            };
        }

        // Rule 7: If session is closed, can't do anything more today
        if (todaySession.Status == CashSessionStatus.Closed)
        {
            return new CurrentSessionDto
            {
                SessionId = todaySession.Id,
                SessionDate = todaySession.SessionDate,
                StatusText = "Session closed",
                StatusColor = "default",
                CanPerformOpeningCount = false,
                CanPerformClosingCount = false
            };
        }

        var agentOpeningCount = todaySession.CashCounts
            .FirstOrDefault(c => c.IsOpening);
        var agentClosingCount = todaySession.CashCounts
            .FirstOrDefault(c => !c.IsOpening);

        var hasApprovedOpening = agentOpeningCount?.Status == CashCountStatus.Approved;
        var hasSubmittedOrApprovedClosing = agentClosingCount != null &&
            (agentClosingCount.Status == CashCountStatus.Approved ||
             agentClosingCount.Status == CashCountStatus.PendingApproval);

        var canOpen = agentOpeningCount == null ||
                      agentOpeningCount.Status == CashCountStatus.Rejected;
        var canClose = hasApprovedOpening && !hasSubmittedOrApprovedClosing;

        return new CurrentSessionDto
        {
            SessionId = todaySession.Id,
            SessionDate = todaySession.SessionDate,
            StatusText = "Session Open",
            StatusColor = "success",
            HasOpeningCount = agentOpeningCount != null && agentOpeningCount.Status != CashCountStatus.Rejected,
            HasClosingCount = agentClosingCount != null && agentClosingCount.Status != CashCountStatus.Rejected,
            OpeningCountStatus = agentOpeningCount?.Status,
            ClosingCountStatus = agentClosingCount?.Status,
            CanPerformOpeningCount = canOpen,
            CanPerformClosingCount = canClose
        };
    }

    /// <inheritdoc />
    public async Task<CashCountFormModel> InitializeCashCountFormAsync(string userId, bool isOpening)
    {
        var agent = await GetAgentForUserAsync(userId);
        if (agent == null)
        {
            return new CashCountFormModel { IsOpening = isOpening };
        }

        var wallets = await dbContext.Wallets
            .Include(w => w.WalletType)
            .Where(w => w.AgentId == agent.Id && w.IsActive)
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
            CountDate = DateOnly.FromDateTime(DateTime.UtcNow),
            WalletEntries = walletEntries
        };
    }

    /// <inheritdoc />
    public async Task<CashCountSaveResult> SaveCashCountAsync(string userId, CashCountFormModel form)
    {
        var agent = await GetAgentForUserAsync(userId);
        if (agent == null)
        {
            return CashCountSaveResult.Error("User is not configured as an agent.");
        }

        if (!agent.BranchId.HasValue)
        {
            return CashCountSaveResult.Error("Agent is not assigned to a branch. Please contact your administrator.");
        }

        var agentId = agent.Id;
        var branchId = agent.BranchId.Value;
        var countDate = form.CountDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Rule 15: No future dates
        if (countDate > today)
        {
            return CashCountSaveResult.Error("Cash count date cannot be in the future.");
        }

        // Rule 14: Check if agent has any pending approval counts
        var hasPendingApproval = await dbContext.CashCounts
            .AnyAsync(c => c.AgentId == agentId && c.Status == CashCountStatus.PendingApproval);
        if (hasPendingApproval)
        {
            return CashCountSaveResult.Error("You have a cash count pending approval. Please wait for it to be processed before starting a new one.");
        }

        // Rule 4, 7: Check for unclosed sessions from previous days
        var previousUnclosedSession = await dbContext.CashSessions
            .Where(s => s.BranchId == branchId &&
                        s.SessionDate < countDate &&
                        s.Status != CashSessionStatus.Closed)
            .AnyAsync();

        if (previousUnclosedSession && form.IsOpening)
        {
            await notificationService.NotifyBranchAdminsAsync(
                branchId,
                "Session Blocked",
                $"An agent is unable to start an opening cash count because a previous cash session has not been closed.",
                NotificationType.SessionBlocked,
                "/cashsessions");

            return CashCountSaveResult.Error("A previous cash session has not been closed. Admins have been notified.");
        }

        // Find or create the branch-level session for this date
        // Review comment: handle race condition with retry on unique constraint violation
        CashSession? session;
        try
        {
            session = await dbContext.CashSessions
                .Include(s => s.CashCounts.Where(c => c.AgentId == agentId))
                .Where(s => s.BranchId == branchId && s.SessionDate == countDate)
                .FirstOrDefaultAsync();
        }
        catch
        {
            session = null;
        }

        if (form.IsOpening)
        {
            // Rule 7: If session exists and is closed, can't open a new count
            if (session?.Status == CashSessionStatus.Closed)
            {
                return CashCountSaveResult.Error("The cash session for this date is already closed.");
            }

            var existingOpening = session?.CashCounts
                .FirstOrDefault(c => c.IsOpening && c.Status != CashCountStatus.Rejected);
            if (existingOpening != null)
            {
                return CashCountSaveResult.Error("An opening count already exists for this session. Close the existing count first.");
            }

            // Rules 1-3: Create session if none exists
            if (session == null)
            {
                session = new CashSession
                {
                    BranchId = branchId,
                    SessionDate = countDate,
                    Status = CashSessionStatus.Open,
                    OpenedAt = DateTimeOffset.UtcNow
                };
                dbContext.CashSessions.Add(session);
                try
                {
                    await dbContext.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    // Race condition: another agent created the session concurrently
                    dbContext.Entry(session).State = EntityState.Detached;
                    session = await dbContext.CashSessions
                        .Include(s => s.CashCounts.Where(c => c.AgentId == agentId))
                        .Where(s => s.BranchId == branchId && s.SessionDate == countDate)
                        .FirstOrDefaultAsync();

                    if (session == null)
                    {
                        return CashCountSaveResult.Error("Failed to create or find session. Please try again.");
                    }
                }
            }
        }
        else
        {
            // Closing count requires an open session
            if (session == null)
            {
                return CashCountSaveResult.Error("No session found for this date. Please perform an opening count first.");
            }

            // Review comment: closing path must also check session is not closed
            if (session.Status == CashSessionStatus.Closed)
            {
                return CashCountSaveResult.Error("The cash session for this date is already closed.");
            }

            var approvedOpening = session.CashCounts
                .FirstOrDefault(c => c.IsOpening && c.Status == CashCountStatus.Approved);
            if (approvedOpening == null)
            {
                return CashCountSaveResult.Error("Opening count must be approved before submitting a closing count.");
            }

            var existingClosing = session.CashCounts
                .FirstOrDefault(c => !c.IsOpening && c.Status != CashCountStatus.Rejected);
            if (existingClosing != null && existingClosing.Status != CashCountStatus.Draft)
            {
                return CashCountSaveResult.Error("A closing count has already been submitted for this session.");
            }
        }

        var cashCount = session.CashCounts
            .FirstOrDefault(c => c.IsOpening == form.IsOpening &&
                                 (c.Status == CashCountStatus.Draft || c.Status == CashCountStatus.Rejected));

        if (cashCount == null)
        {
            cashCount = new CashCount
            {
                CashSessionId = session.Id,
                AgentId = agentId,
                IsOpening = form.IsOpening,
                Status = CashCountStatus.Draft,
                CountDate = countDate,
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.CashCounts.Add(cashCount);
            await dbContext.SaveChangesAsync();
        }
        else if (cashCount.Status == CashCountStatus.Rejected)
        {
            cashCount.Status = CashCountStatus.Draft;
            cashCount.RejectedAt = null;
            cashCount.RejectedByUserId = null;
            cashCount.RejectionReason = null;
        }

        cashCount.TotalAmount = form.TotalAmount;
        cashCount.CountDate = countDate;
        cashCount.Explanation = form.Explanation;

        var existingDetails = await dbContext.CashCountDetails
            .Where(d => d.CashCountId == cashCount.Id)
            .ToListAsync();
        dbContext.CashCountDetails.RemoveRange(existingDetails);

        foreach (var entry in form.WalletEntries)
        {
            dbContext.CashCountDetails.Add(new CashCountDetail
            {
                CashCountId = cashCount.Id,
                WalletId = entry.WalletId,
                Amount = entry.CountedAmount,
                Denominations = entry.Denominations?.ToJson()
            });
        }

        await dbContext.SaveChangesAsync();

        return CashCountSaveResult.Ok(cashCount.Id, session.Id);
    }

    /// <inheritdoc />
    public async Task<CashCountSaveResult> SubmitCashCountAsync(string userId, long cashCountId)
    {
        var agent = await GetAgentForUserAsync(userId);
        if (agent == null)
        {
            return CashCountSaveResult.Error("User is not configured as an agent.");
        }

        var cashCount = await dbContext.CashCounts
            .Include(c => c.CashSession)
            .Include(c => c.Details)
                .ThenInclude(d => d.Wallet)
            .Where(c => c.Id == cashCountId && c.AgentId == agent.Id)
            .FirstOrDefaultAsync();

        if (cashCount == null)
        {
            return CashCountSaveResult.Error("Cash count not found.");
        }

        if (cashCount.Status != CashCountStatus.Draft)
        {
            return CashCountSaveResult.Error("Cash count has already been submitted.");
        }

        // Review comment: reject submission on a closed session
        if (cashCount.CashSession!.Status == CashSessionStatus.Closed)
        {
            return CashCountSaveResult.Error("Cannot submit a cash count for a closed session.");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var branchId = cashCount.CashSession!.BranchId;

        cashCount.SubmittedAt = DateTimeOffset.UtcNow;
        cashCount.Status = CashCountStatus.PendingApproval;

        // Rule 12: Auto-approve matching closing counts for today only
        if (!cashCount.IsOpening && cashCount.CountDate == today)
        {
            var openingCount = await dbContext.CashCounts
                .Where(c => c.CashSessionId == cashCount.CashSessionId &&
                            c.AgentId == agent.Id &&
                            c.IsOpening &&
                            c.Status == CashCountStatus.Approved)
                .FirstOrDefaultAsync();

            if (openingCount != null && cashCount.TotalAmount == openingCount.TotalAmount)
            {
                return await ExecuteClosingCountApproval(cashCount, branchId, userId, isAutoApproval: true);
            }
        }

        // Rule 10, 16: If closing count has discrepancy, require explanation
        if (!cashCount.IsOpening)
        {
            var openingCount = await dbContext.CashCounts
                .Where(c => c.CashSessionId == cashCount.CashSessionId &&
                            c.AgentId == agent.Id &&
                            c.IsOpening &&
                            c.Status == CashCountStatus.Approved)
                .FirstOrDefaultAsync();

            if (openingCount != null && cashCount.TotalAmount != openingCount.TotalAmount)
            {
                if (string.IsNullOrWhiteSpace(cashCount.Explanation))
                {
                    cashCount.Status = CashCountStatus.Draft;
                    cashCount.SubmittedAt = null;
                    await dbContext.SaveChangesAsync();
                    return CashCountSaveResult.Error("Closing count differs from opening count. Please provide an explanation for the discrepancy.");
                }

                var discrepancy = new Discrepancy
                {
                    CashSessionId = cashCount.CashSessionId,
                    CashCountId = cashCount.Id,
                    Status = DiscrepancyStatus.PendingReview,
                    ExpectedAmount = openingCount.TotalAmount,
                    ActualAmount = cashCount.TotalAmount,
                    Variance = cashCount.TotalAmount - openingCount.TotalAmount,
                    Explanation = cashCount.Explanation,
                    ExplainedByUserId = agent.Id,
                    ExplainedAt = DateTimeOffset.UtcNow,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                dbContext.Discrepancies.Add(discrepancy);
            }
        }

        await dbContext.SaveChangesAsync();

        var agentName = agent.User != null ? $"{agent.User.FirstName} {agent.User.LastName}".Trim() : agent.Code;
        var countType = cashCount.IsOpening ? "opening" : "closing";
        await notificationService.NotifyBranchAdminsAsync(
            branchId,
            "Cash Count Pending Approval",
            $"{agentName}'s {countType} cash count of UGX {cashCount.TotalAmount:N0} is awaiting your approval.",
            NotificationType.CountPendingApproval,
            "/cashcount-approvals");

        telemetryClient?.TrackEvent("cash_count_submitted", new Dictionary<string, string>
        {
            { "Type", cashCount.IsOpening ? "Opening" : "Closing" },
            { "AgentId", agent.Id.ToString() },
            { "SessionId", cashCount.CashSessionId.ToString() },
            { "TotalAmount", cashCount.TotalAmount.ToString("F2") },
            { "CountDate", cashCount.CountDate.ToString("yyyy-MM-dd") }
        });

        return CashCountSaveResult.Ok(cashCount.Id, cashCount.CashSessionId);
    }

    /// <inheritdoc />
    public async Task<CashCountSaveResult> ApproveCashCountAsync(string adminUserId, long cashCountId)
    {
        var admin = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == adminUserId);
        if (admin == null || (admin.Role != UserRole.Admin && admin.Role != UserRole.Supervisor))
        {
            return CashCountSaveResult.Error("Only administrators or supervisors can approve cash counts.");
        }

        var cashCount = await dbContext.CashCounts
            .Include(c => c.CashSession)
            .Include(c => c.Agent)
                .ThenInclude(a => a!.User)
            .Include(c => c.Details)
                .ThenInclude(d => d.Wallet)
            .FirstOrDefaultAsync(c => c.Id == cashCountId);

        if (cashCount == null)
        {
            return CashCountSaveResult.Error("Cash count not found.");
        }

        if (cashCount.Status != CashCountStatus.PendingApproval)
        {
            return CashCountSaveResult.Error("Cash count is not pending approval.");
        }

        // Review comment: reject approval on a closed session
        if (cashCount.CashSession!.Status == CashSessionStatus.Closed)
        {
            return CashCountSaveResult.Error("Cannot approve a cash count for a closed session.");
        }

        var branchId = cashCount.CashSession!.BranchId;

        if (cashCount.IsOpening)
        {
            return await ExecuteOpeningCountApproval(cashCount, branchId, adminUserId);
        }
        else
        {
            return await ExecuteClosingCountApproval(cashCount, branchId, adminUserId, isAutoApproval: false);
        }
    }

    /// <inheritdoc />
    public async Task<CashCountSaveResult> RejectCashCountAsync(string adminUserId, long cashCountId, string reason)
    {
        var admin = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == adminUserId);
        if (admin == null || (admin.Role != UserRole.Admin && admin.Role != UserRole.Supervisor))
        {
            return CashCountSaveResult.Error("Only administrators or supervisors can reject cash counts.");
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 10)
        {
            return CashCountSaveResult.Error("Rejection reason must be at least 10 characters.");
        }

        var cashCount = await dbContext.CashCounts
            .Include(c => c.Agent)
                .ThenInclude(a => a!.User)
            .FirstOrDefaultAsync(c => c.Id == cashCountId);

        if (cashCount == null)
        {
            return CashCountSaveResult.Error("Cash count not found.");
        }

        if (cashCount.Status != CashCountStatus.PendingApproval)
        {
            return CashCountSaveResult.Error("Cash count is not pending approval.");
        }

        cashCount.Status = CashCountStatus.Rejected;
        cashCount.RejectedAt = DateTimeOffset.UtcNow;
        cashCount.RejectedByUserId = adminUserId;
        cashCount.RejectionReason = reason.Trim();

        // Reject associated discrepancy if any
        var discrepancy = await dbContext.Discrepancies
            .FirstOrDefaultAsync(d => d.CashCountId == cashCountId && d.Status == DiscrepancyStatus.PendingReview);
        if (discrepancy != null)
        {
            discrepancy.Status = DiscrepancyStatus.Rejected;
            discrepancy.ApprovedByUserId = adminUserId;
            discrepancy.ApprovedAt = DateTimeOffset.UtcNow;
            discrepancy.ApprovalNotes = $"Rejected by admin. Reason: {reason.Trim()}";
        }

        await dbContext.SaveChangesAsync();

        if (cashCount.Agent?.UserId != null)
        {
            var countType = cashCount.IsOpening ? "opening" : "closing";
            await notificationService.CreateSystemNotificationAsync(
                cashCount.Agent.UserId,
                "Cash Count Rejected",
                $"Your {countType} cash count has been rejected. Reason: {reason.Trim()}",
                NotificationType.CountRejected,
                "/cashcount");
        }

        telemetryClient?.TrackEvent("cash_count_rejected", new Dictionary<string, string>
        {
            { "CashCountId", cashCountId.ToString() },
            { "RejectedBy", adminUserId },
            { "Reason", reason.Trim() }
        });

        return CashCountSaveResult.Ok(cashCount.Id, cashCount.CashSessionId);
    }

    /// <inheritdoc />
    public async Task<List<PendingApprovalDto>> GetPendingApprovalsAsync(long branchId)
    {
        // Review comment: fix N+1 by batch-loading opening totals
        var pendingCounts = await dbContext.CashCounts
            .Include(c => c.CashSession)
            .Include(c => c.Agent)
                .ThenInclude(a => a!.User)
            .Where(c => c.CashSession!.BranchId == branchId &&
                        c.Status == CashCountStatus.PendingApproval)
            .OrderBy(c => c.SubmittedAt)
            .ToListAsync();

        if (pendingCounts.Count == 0)
        {
            return [];
        }

        // Batch load all opening totals for closing counts in one query
        var closingSessionAgentPairs = pendingCounts
            .Where(c => !c.IsOpening)
            .Select(c => new { c.CashSessionId, c.AgentId })
            .Distinct()
            .ToList();

        var openingTotalsLookup = new Dictionary<(long SessionId, long AgentId), decimal>();
        if (closingSessionAgentPairs.Count > 0)
        {
            var sessionIds = closingSessionAgentPairs.Select(p => p.CashSessionId).Distinct().ToList();
            var agentIds = closingSessionAgentPairs.Select(p => p.AgentId).Distinct().ToList();

            var openingCounts = await dbContext.CashCounts
                .Where(c => sessionIds.Contains(c.CashSessionId) &&
                            agentIds.Contains(c.AgentId) &&
                            c.IsOpening &&
                            c.Status == CashCountStatus.Approved)
                .Select(c => new { c.CashSessionId, c.AgentId, c.TotalAmount })
                .ToListAsync();

            foreach (var oc in openingCounts)
            {
                openingTotalsLookup[(oc.CashSessionId, oc.AgentId)] = oc.TotalAmount;
            }
        }

        return pendingCounts.Select(count =>
        {
            decimal? openingTotal = null;
            if (!count.IsOpening)
            {
                openingTotalsLookup.TryGetValue((count.CashSessionId, count.AgentId), out var ot);
                openingTotal = ot > 0 ? ot : null;
            }

            return new PendingApprovalDto
            {
                CashCountId = count.Id,
                CashSessionId = count.CashSessionId,
                SessionDate = count.CashSession!.SessionDate,
                CountDate = count.CountDate,
                AgentName = count.Agent?.User != null
                    ? $"{count.Agent.User.FirstName} {count.Agent.User.LastName}".Trim()
                    : "Unknown",
                AgentCode = count.Agent?.Code ?? "N/A",
                IsOpening = count.IsOpening,
                Status = count.Status,
                TotalAmount = count.TotalAmount,
                OpeningTotal = openingTotal,
                Variance = openingTotal.HasValue ? count.TotalAmount - openingTotal.Value : null,
                Explanation = count.Explanation,
                HasDiscrepancy = openingTotal.HasValue && count.TotalAmount != openingTotal.Value,
                SubmittedAt = count.SubmittedAt ?? count.CreatedAt
            };
        }).ToList();
    }

    /// <inheritdoc />
    public async Task<CashCountSaveResult> AdminCloseAgentSessionAsync(string adminUserId, long cashSessionId, long agentId)
    {
        var admin = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == adminUserId);
        if (admin == null || admin.Role != UserRole.Admin)
        {
            return CashCountSaveResult.Error("Only administrators can close a session for an agent.");
        }

        var session = await dbContext.CashSessions
            .Include(s => s.CashCounts.Where(c => c.AgentId == agentId))
            .FirstOrDefaultAsync(s => s.Id == cashSessionId);

        if (session == null)
        {
            return CashCountSaveResult.Error("Cash session not found.");
        }

        var openingCount = session.CashCounts
            .FirstOrDefault(c => c.IsOpening && c.Status == CashCountStatus.Approved);

        if (openingCount == null)
        {
            return CashCountSaveResult.Error("Agent has no approved opening count in this session.");
        }

        var existingClosing = session.CashCounts
            .FirstOrDefault(c => !c.IsOpening && c.Status != CashCountStatus.Rejected);

        if (existingClosing?.Status == CashCountStatus.Approved)
        {
            return CashCountSaveResult.Error("Agent's closing count is already approved.");
        }

        // Create or update closing count matching opening (admin-forced)
        var closingCount = existingClosing ?? new CashCount
        {
            CashSessionId = session.Id,
            AgentId = agentId,
            IsOpening = false,
            CountDate = session.SessionDate,
            CreatedAt = DateTimeOffset.UtcNow
        };

        closingCount.TotalAmount = openingCount.TotalAmount;
        closingCount.Status = CashCountStatus.PendingApproval;
        closingCount.SubmittedAt = DateTimeOffset.UtcNow;
        closingCount.Explanation = "Session closed by admin.";

        if (existingClosing == null)
        {
            dbContext.CashCounts.Add(closingCount);
            await dbContext.SaveChangesAsync();

            var openingDetails = await dbContext.CashCountDetails
                .Where(d => d.CashCountId == openingCount.Id)
                .ToListAsync();

            foreach (var detail in openingDetails)
            {
                dbContext.CashCountDetails.Add(new CashCountDetail
                {
                    CashCountId = closingCount.Id,
                    WalletId = detail.WalletId,
                    Amount = detail.Amount,
                    Denominations = detail.Denominations
                });
            }

            await dbContext.SaveChangesAsync();
        }

        // Review comment: reload closing count with Details + Wallet includes before approval
        var reloadedClosingCount = await dbContext.CashCounts
            .Include(c => c.CashSession)
            .Include(c => c.Agent)
                .ThenInclude(a => a!.User)
            .Include(c => c.Details)
                .ThenInclude(d => d.Wallet)
            .FirstAsync(c => c.Id == closingCount.Id);

        return await ExecuteClosingCountApproval(reloadedClosingCount, session.BranchId, adminUserId, isAutoApproval: false);
    }

    /// <inheritdoc />
    public async Task<CashCountFormModel?> GetCashCountFormAsync(string userId, long cashCountId)
    {
        var agent = await GetAgentForUserAsync(userId);
        if (agent == null)
        {
            return null;
        }

        var cashCount = await dbContext.CashCounts
            .Include(c => c.CashSession)
            .Include(c => c.Details)
                .ThenInclude(d => d.Wallet)
                    .ThenInclude(w => w!.WalletType)
            .Where(c => c.Id == cashCountId && c.AgentId == agent.Id)
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
            CountDate = cashCount.CountDate,
            Explanation = cashCount.Explanation,
            WalletEntries = walletEntries
        };
    }

    // ---- Private helpers ----

    /// <summary>
    /// Review comment: wraps vault withdrawal + wallet updates + status change in a single transaction.
    /// </summary>
    private async Task<CashCountSaveResult> ExecuteOpeningCountApproval(
        CashCount cashCount, long branchId, string adminUserId)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        try
        {
            // Rule 6: Check vault balance
            var withdrawResult = await vaultService.WithdrawForSessionAsync(
                cashCount.CashSessionId,
                branchId,
                cashCount.TotalAmount,
                adminUserId,
                ensureTransaction: false
            );

            if (!withdrawResult.Success)
            {
                await transaction.RollbackAsync();
                return CashCountSaveResult.Error($"Vault withdrawal failed: {withdrawResult.ErrorMessage}");
            }

            foreach (var detail in cashCount.Details)
            {
                if (detail.Wallet != null)
                {
                    detail.Wallet.Balance = detail.Amount;
                    detail.Wallet.UpdatedAt = DateTimeOffset.UtcNow;
                }
            }

            cashCount.Status = CashCountStatus.Approved;
            cashCount.ApprovedAt = DateTimeOffset.UtcNow;
            cashCount.ApprovedByUserId = adminUserId;

            await dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        if (cashCount.Agent?.UserId != null)
        {
            await notificationService.CreateSystemNotificationAsync(
                cashCount.Agent.UserId,
                "Opening Count Approved",
                $"Your opening cash count of UGX {cashCount.TotalAmount:N0} has been approved.",
                NotificationType.CountApproved,
                "/cashcount");
        }

        telemetryClient?.TrackEvent("cash_count_approved", new Dictionary<string, string>
        {
            { "Type", "Opening" },
            { "CashCountId", cashCount.Id.ToString() },
            { "ApprovedBy", adminUserId },
            { "TotalAmount", cashCount.TotalAmount.ToString("F2") }
        });

        return CashCountSaveResult.Ok(cashCount.Id, cashCount.CashSessionId);
    }

    /// <summary>
    /// Review comment: wraps vault deposit + wallet updates + status change in a single transaction.
    /// </summary>
    private async Task<CashCountSaveResult> ExecuteClosingCountApproval(
        CashCount cashCount, long branchId, string userId, bool isAutoApproval)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        try
        {
            var depositResult = await vaultService.DepositForSessionAsync(
                cashCount.CashSessionId,
                branchId,
                cashCount.TotalAmount,
                userId,
                ensureTransaction: false
            );

            if (!depositResult.Success)
            {
                await transaction.RollbackAsync();
                return CashCountSaveResult.Error($"Vault deposit failed: {depositResult.ErrorMessage}");
            }

            foreach (var detail in cashCount.Details)
            {
                if (detail.Wallet != null)
                {
                    detail.Wallet.Balance = 0;
                    detail.Wallet.UpdatedAt = DateTimeOffset.UtcNow;
                }
            }

            cashCount.Status = CashCountStatus.Approved;
            cashCount.ApprovedAt = DateTimeOffset.UtcNow;
            cashCount.ApprovedByUserId = isAutoApproval ? null : userId;

            // Approve associated discrepancy if any
            var discrepancy = await dbContext.Discrepancies
                .FirstOrDefaultAsync(d => d.CashCountId == cashCount.Id && d.Status == DiscrepancyStatus.PendingReview);
            if (discrepancy != null)
            {
                discrepancy.Status = DiscrepancyStatus.Approved;
                discrepancy.ApprovedByUserId = isAutoApproval ? null : userId;
                discrepancy.ApprovedAt = DateTimeOffset.UtcNow;
                discrepancy.ApprovalNotes = isAutoApproval ? "Auto-approved (closing matches opening)" : null;
            }

            await dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        // Check if all agents' closing counts are now approved -> close session
        await TryCloseSessionAsync(cashCount.CashSessionId);

        if (cashCount.Agent?.UserId != null)
        {
            var notificationType = isAutoApproval ? NotificationType.CountAutoApproved : NotificationType.CountApproved;
            await notificationService.CreateSystemNotificationAsync(
                cashCount.Agent.UserId,
                "Closing Count Approved",
                $"Your closing cash count of UGX {cashCount.TotalAmount:N0} has been {(isAutoApproval ? "automatically " : "")}approved.",
                notificationType,
                "/cashcount");
        }

        if (isAutoApproval)
        {
            await notificationService.NotifyBranchAdminsAsync(
                branchId,
                "Closing Count Auto-Approved",
                $"A closing cash count matching the opening count was automatically approved for {cashCount.Agent?.User?.FullName ?? "an agent"}.",
                NotificationType.CountAutoApproved,
                $"/cashsessions/{cashCount.CashSessionId}");
        }

        telemetryClient?.TrackEvent("cash_count_approved", new Dictionary<string, string>
        {
            { "Type", "Closing" },
            { "CashCountId", cashCount.Id.ToString() },
            { "ApprovedBy", isAutoApproval ? "System" : userId },
            { "TotalAmount", cashCount.TotalAmount.ToString("F2") },
            { "AutoApproved", isAutoApproval.ToString() }
        });

        return CashCountSaveResult.Ok(cashCount.Id, cashCount.CashSessionId);
    }

    /// <summary>
    /// Rules 5, 8, 9: Check if all agents' closing counts in a session are approved.
    /// Also ensures no pending approvals remain before auto-closing.
    /// </summary>
    private async Task TryCloseSessionAsync(long sessionId)
    {
        var session = await dbContext.CashSessions
            .Include(s => s.CashCounts)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null || session.Status == CashSessionStatus.Closed)
        {
            return;
        }

        // Review comment: don't auto-close if any counts are still pending approval
        var hasPendingApprovals = session.CashCounts
            .Any(c => c.Status == CashCountStatus.PendingApproval);
        if (hasPendingApprovals)
        {
            return;
        }

        var agentsWithOpeningCounts = session.CashCounts
            .Where(c => c.IsOpening && c.Status == CashCountStatus.Approved)
            .Select(c => c.AgentId)
            .Distinct()
            .ToList();

        if (agentsWithOpeningCounts.Count == 0)
        {
            return;
        }

        var agentsWithApprovedClosing = session.CashCounts
            .Where(c => !c.IsOpening && c.Status == CashCountStatus.Approved)
            .Select(c => c.AgentId)
            .Distinct()
            .ToList();

        var allClosed = agentsWithOpeningCounts.All(agentId => agentsWithApprovedClosing.Contains(agentId));

        if (allClosed)
        {
            session.Status = CashSessionStatus.Closed;
            session.ClosedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync();

            telemetryClient?.TrackEvent("session_auto_closed", new Dictionary<string, string>
            {
                { "SessionId", session.Id.ToString() },
                { "SessionDate", session.SessionDate.ToString("yyyy-MM-dd") },
                { "AgentCount", agentsWithOpeningCounts.Count.ToString() }
            });
        }
    }

    private async Task<Agent?> GetAgentForUserAsync(string userId)
    {
        return await dbContext.Agents
            .Include(a => a.User)
            .FirstOrDefaultAsync(a => a.UserId == userId);
    }
}

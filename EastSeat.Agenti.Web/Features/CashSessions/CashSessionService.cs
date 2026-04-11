using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Web.Data;
using Microsoft.ApplicationInsights;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.CashSessions;

/// <summary>
/// Service implementation for cash session operations (branch-level sessions).
/// </summary>
public class CashSessionService(
    ApplicationDbContext dbContext,
    TelemetryClient? telemetryClient = null) : ICashSessionService
{
    /// <inheritdoc />
    public async Task<List<CashSessionListItemDto>> GetCashSessionsAsync(long? branchId = null)
    {
        var query = dbContext.CashSessions
            .Include(cs => cs.CashCounts)
            .AsQueryable();

        if (branchId.HasValue)
        {
            query = query.Where(cs => cs.BranchId == branchId.Value);
        }

        var sessions = await query
            .OrderByDescending(cs => cs.SessionDate)
            .ThenByDescending(cs => cs.OpenedAt)
            .ToListAsync();

        return sessions.Select(s =>
        {
            var agentIds = s.CashCounts
                .Where(c => c.IsOpening && c.Status != CashCountStatus.Rejected)
                .Select(c => c.AgentId)
                .Distinct()
                .ToList();

            var approvedClosingAgents = s.CashCounts
                .Where(c => !c.IsOpening && c.Status == CashCountStatus.Approved)
                .Select(c => c.AgentId)
                .Distinct()
                .ToList();

            var pendingCount = s.CashCounts
                .Count(c => c.Status == CashCountStatus.PendingApproval);

            var totalOpening = s.CashCounts
                .Where(c => c.IsOpening && c.Status == CashCountStatus.Approved)
                .Sum(c => c.TotalAmount);

            var closingCounts = s.CashCounts
                .Where(c => !c.IsOpening && c.Status == CashCountStatus.Approved)
                .ToList();

            return new CashSessionListItemDto
            {
                Id = s.Id,
                SessionDate = s.SessionDate,
                Status = s.Status,
                OpenedAt = s.OpenedAt,
                ClosedAt = s.ClosedAt,
                AgentCount = agentIds.Count,
                ApprovedClosingCount = approvedClosingAgents.Count,
                TotalOpeningAmount = totalOpening,
                TotalClosingAmount = closingCounts.Count > 0 ? closingCounts.Sum(c => c.TotalAmount) : null,
                AllClosingCountsApproved = agentIds.Count > 0 && agentIds.All(id => approvedClosingAgents.Contains(id)),
                PendingApprovalCount = pendingCount
            };
        }).ToList();
    }

    /// <inheritdoc />
    public async Task<CashSessionDetailDto?> GetCashSessionDetailAsync(long sessionId)
    {
        var session = await dbContext.CashSessions
            .Include(cs => cs.CashCounts)
                .ThenInclude(c => c.Agent)
                    .ThenInclude(a => a!.User)
            .Include(cs => cs.CashCounts)
                .ThenInclude(c => c.Details)
                    .ThenInclude(d => d.Wallet)
                        .ThenInclude(w => w!.WalletType)
            .FirstOrDefaultAsync(cs => cs.Id == sessionId);

        if (session == null)
        {
            return null;
        }

        // Group counts by agent
        var agentGroups = session.CashCounts
            .GroupBy(c => c.AgentId)
            .ToList();

        // Load wallet adjustment totals for all session agents in a single query
        var agentIds = agentGroups.Select(g => g.Key).ToList();
        var rawAdjustments = agentIds.Count == 0
            ? []
            : await dbContext.WalletAdjustments
                .Where(wa => wa.CashSessionId == sessionId &&
                             agentIds.Contains(wa.AgentId) &&
                             wa.Status == WalletAdjustmentStatus.Approved)
                .Select(wa => new { wa.AgentId, wa.Amount })
                .ToListAsync();

        var adjustmentsByAgent = rawAdjustments
            .GroupBy(a => a.AgentId)
            .Where(g => g.Sum(a => a.Amount) > 0)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Amount));

        var agentSummaries = agentGroups.Select(g =>
        {
            var agent = g.First().Agent;
            var openingCount = g.FirstOrDefault(c => c.IsOpening && c.Status != CashCountStatus.Rejected);
            var closingCount = g.FirstOrDefault(c => !c.IsOpening && c.Status != CashCountStatus.Rejected);

            return new AgentSessionSummaryDto
            {
                AgentId = g.Key,
                AgentName = agent?.User != null
                    ? $"{agent.User.FirstName} {agent.User.LastName}".Trim()
                    : "Unknown",
                AgentCode = agent?.Code ?? "N/A",
                OpeningCount = openingCount != null ? MapToCountSummary(openingCount) : null,
                ClosingCount = closingCount != null ? MapToCountSummary(closingCount) : null,
                TotalAdjustments = adjustmentsByAgent.GetValueOrDefault(g.Key, 0)
            };
        }).OrderBy(a => a.AgentName).ToList();

        var totalOpening = agentSummaries
            .Where(a => a.OpeningCount?.Status == CashCountStatus.Approved)
            .Sum(a => a.OpeningCount!.TotalAmount);

        var approvedClosings = agentSummaries
            .Where(a => a.ClosingCount?.Status == CashCountStatus.Approved)
            .ToList();

        return new CashSessionDetailDto
        {
            Id = session.Id,
            SessionDate = session.SessionDate,
            Status = session.Status,
            OpenedAt = session.OpenedAt,
            ClosedAt = session.ClosedAt,
            TotalOpeningAmount = totalOpening,
            TotalClosingAmount = approvedClosings.Count > 0
                ? approvedClosings.Sum(a => a.ClosingCount!.TotalAmount)
                : null,
            AgentSummaries = agentSummaries
        };
    }

    /// <inheritdoc />
    public async Task<(bool Success, string? ErrorMessage)> CloseSessionAsync(long sessionId)
    {
        var session = await dbContext.CashSessions
            .Include(s => s.CashCounts)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null)
        {
            return (false, "Cash session not found.");
        }

        if (session.Status == CashSessionStatus.Closed)
        {
            return (false, "Session is already closed.");
        }

        // Rules 5, 8, 9: All agents with opening counts must have approved closing counts
        var agentsWithOpenings = session.CashCounts
            .Where(c => c.IsOpening && c.Status == CashCountStatus.Approved)
            .Select(c => c.AgentId)
            .Distinct()
            .ToList();

        var agentsWithApprovedClosings = session.CashCounts
            .Where(c => !c.IsOpening && c.Status == CashCountStatus.Approved)
            .Select(c => c.AgentId)
            .Distinct()
            .ToList();

        var agentsMissingClosing = agentsWithOpenings
            .Where(id => !agentsWithApprovedClosings.Contains(id))
            .ToList();

        if (agentsMissingClosing.Count > 0)
        {
            var missingCount = agentsMissingClosing.Count;
            return (false, $"Cannot close session: {missingCount} agent(s) still need approved closing counts.");
        }

        // Check for pending approvals
        var pendingCount = session.CashCounts.Count(c => c.Status == CashCountStatus.PendingApproval);
        if (pendingCount > 0)
        {
            return (false, $"Cannot close session: {pendingCount} cash count(s) are still pending approval.");
        }

        session.Status = CashSessionStatus.Closed;
        session.ClosedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync();

        telemetryClient?.TrackEvent("session_closed", new Dictionary<string, string>
        {
            { "SessionId", session.Id.ToString() },
            { "SessionDate", session.SessionDate.ToString("yyyy-MM-dd") },
            { "AgentCount", agentsWithOpenings.Count.ToString() },
            { "Duration", session.ClosedAt.HasValue ? (session.ClosedAt.Value - session.OpenedAt).ToString() : "N/A" }
        });

        return (true, null);
    }

    /// <inheritdoc />
    public async Task<List<CashSessionListItemDto>> GetOpenSessionsAsync(long branchId)
    {
        var sessions = await dbContext.CashSessions
            .Include(cs => cs.CashCounts)
            .Where(cs => cs.BranchId == branchId && cs.Status != CashSessionStatus.Closed)
            .OrderByDescending(cs => cs.SessionDate)
            .ToListAsync();

        return sessions.Select(s =>
        {
            var agentIds = s.CashCounts
                .Where(c => c.IsOpening && c.Status != CashCountStatus.Rejected)
                .Select(c => c.AgentId)
                .Distinct()
                .ToList();

            return new CashSessionListItemDto
            {
                Id = s.Id,
                SessionDate = s.SessionDate,
                Status = s.Status,
                OpenedAt = s.OpenedAt,
                AgentCount = agentIds.Count,
                PendingApprovalCount = s.CashCounts.Count(c => c.Status == CashCountStatus.PendingApproval)
            };
        }).ToList();
    }

    private static CashCountSummaryDto MapToCountSummary(EastSeat.Agenti.Shared.Domain.Entities.CashCount cashCount)
    {
        return new CashCountSummaryDto
        {
            Id = cashCount.Id,
            Status = cashCount.Status,
            TotalAmount = cashCount.TotalAmount,
            CreatedAt = cashCount.CreatedAt,
            SubmittedAt = cashCount.SubmittedAt,
            ApprovedAt = cashCount.ApprovedAt,
            Explanation = cashCount.Explanation,
            RejectionReason = cashCount.RejectionReason,
            WalletEntries = cashCount.Details.Select(d => new WalletCountSummaryDto
            {
                WalletId = d.WalletId,
                WalletName = d.Wallet?.Name ?? "Unknown",
                WalletTypeName = d.Wallet?.WalletType?.Name ?? "Unknown",
                Amount = d.Amount
            }).OrderBy(w => w.WalletTypeName).ThenBy(w => w.WalletName).ToList()
        };
    }
}

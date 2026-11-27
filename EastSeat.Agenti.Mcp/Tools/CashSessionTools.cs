using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using EastSeat.Agenti.Mcp.Configuration;
using EastSeat.Agenti.Mcp.Data;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Mcp.Authentication;

namespace EastSeat.Agenti.Mcp.Tools;

[McpServerToolType]
public class CashSessionTools
{
    [McpServerTool(Name = "query_cash_sessions"),
     Description("Query cash session history with date range, agent, status, and branch filters. " +
                 "Returns session date, participating agents, status, opening/closing totals, and discrepancy flag.")]
    public static async Task<string> QueryCashSessions(
        ReadOnlyDbContext db,
        McpServerConfig config,
        McpCaller caller,
        [Description("Start date (yyyy-MM-dd format, required)")] string dateFrom,
        [Description("End date (yyyy-MM-dd format, defaults to today)")] string? dateTo = null,
        [Description("Filter by agent code (e.g., 'JODO')")] string? agentCode = null,
        [Description("Filter by session status: Open, Closed, Pending, DiscrepancyUnderReview, Completed, Blocked")]
        string? status = null,
        [Description("Filter by branch ID (supervisors are restricted to their assigned branch)")]
        long? branchId = null,
        CancellationToken cancellationToken = default)
    {
        if (!DateOnly.TryParse(dateFrom, out var from))
            return "Error: Invalid dateFrom format. Use yyyy-MM-dd.";

        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        if (dateTo != null && !DateOnly.TryParse(dateTo, out to))
            return "Error: Invalid dateTo format. Use yyyy-MM-dd.";

        if (from > to)
            return "Error: dateFrom cannot be after dateTo.";

        var query = db.CashSessions.AsQueryable();

        // Date filter
        query = query.Where(s => s.SessionDate >= from && s.SessionDate <= to);

        // Branch isolation
        var effectiveBranchId = caller.ResolveBranch(branchId);
        if (effectiveBranchId.HasValue)
            query = query.Where(s => s.BranchId == effectiveBranchId.Value);

        // Agent filter (via CashCounts)
        if (!string.IsNullOrWhiteSpace(agentCode))
            query = query.Where(s => s.CashCounts.Any(c => c.Agent != null && c.Agent.Code == agentCode.ToUpper()));

        // Status filter
        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<CashSessionStatus>(status, ignoreCase: true, out var parsedStatus))
            query = query.Where(s => s.Status == parsedStatus);

        var sessions = await query
            .OrderByDescending(s => s.SessionDate)
            .Take(config.MaxRows)
            .Select(s => new
            {
                s.Id, s.SessionDate, s.Status, s.BranchId, s.WorkflowVersion, s.OpenedAt, s.ClosedAt,
                Agents = s.CashCounts.Where(c => c.IsOpening && c.Agent != null)
                    .Select(c => c.Agent!.Code + " (" + (c.Agent.User == null ? "N/A" : (c.Agent.User.FirstName + " " + c.Agent.User.LastName).Trim()) + ")")
                    .OrderBy(name => name).Take(config.MaxRows).ToList(),
                OpeningTotal = s.CashCounts.Where(c => c.IsOpening && c.Status == CashCountStatus.Approved).Sum(c => c.TotalAmount),
                ClosingTotal = s.CashCounts.Where(c => !c.IsOpening && c.Status == CashCountStatus.Approved).Sum(c => c.TotalAmount),
                Reported = s.CashCounts.Where(c => !c.IsOpening && c.SubmittedAt.HasValue).Sum(c => c.TotalAmount),
                Pending = s.CashCounts.Count(c => !c.IsOpening && c.Status == CashCountStatus.PendingApproval),
                DiscrepancyCount = s.Discrepancies.Count
            }).ToListAsync(cancellationToken);

        if (sessions.Count == 0)
            return "No cash sessions found matching the specified filters.";

        var lines = new List<string>
        {
            $"Found {sessions.Count} cash session(s) from {from:yyyy-MM-dd} to {to:yyyy-MM-dd}:",
            ""
        };

        foreach (var s in sessions)
        {
            lines.Add($"• Session #{s.Id} | {s.SessionDate:yyyy-MM-dd} | Agents: {string.Join(", ", s.Agents.Distinct())}");
            lines.Add($"  Status: {s.Status} | Branch: {s.BranchId}");
            lines.Add($"  Workflow: {s.WorkflowVersion}; closed status describes operations, not bank reconciliation.");
            if (s.WorkflowVersion == SessionWorkflowVersion.BankReconciliation)
            {
                lines.Add($"  Reported closing: {s.Reported:N2} | Awaiting financial approval: {s.Pending}");
            }
            lines.Add($"  Approved opening: {s.OpeningTotal:N2} | Approved closing: {s.ClosingTotal:N2}");
            lines.Add($"  Opened: {s.OpenedAt:yyyy-MM-dd HH:mm} | Closed: {(s.ClosedAt.HasValue ? s.ClosedAt.Value.ToString("yyyy-MM-dd HH:mm") : "Still open")}");
            if (s.DiscrepancyCount > 0)
                lines.Add($"  ⚠️ {s.DiscrepancyCount} discrepancy(ies) flagged");
            if (s.Agents.Count == config.MaxRows)
                lines.Add($"  Participating agents limited to {config.MaxRows}.");
            lines.Add("");
        }

        if (sessions.Count == config.MaxRows)
            lines.Add($"(Results limited to {config.MaxRows} rows. Narrow your date range for more specific results.)");

        return string.Join("\n", lines);
    }
}

using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using EastSeat.Agenti.Mcp.Configuration;
using EastSeat.Agenti.Mcp.Data;
using EastSeat.Agenti.Shared.Domain.Enums;

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
        [Description("Start date (yyyy-MM-dd format, required)")] string dateFrom,
        [Description("End date (yyyy-MM-dd format, defaults to today)")] string? dateTo = null,
        [Description("Filter by agent code (e.g., 'JODO')")] string? agentCode = null,
        [Description("Filter by session status: Open, Closed, Pending, DiscrepancyUnderReview, Completed, Blocked")]
        string? status = null,
        [Description("Filter by branch ID (admin/supervisor only, ignored for agents)")]
        long? branchId = null)
    {
        if (!DateOnly.TryParse(dateFrom, out var from))
            return "Error: Invalid dateFrom format. Use yyyy-MM-dd.";

        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        if (dateTo != null && !DateOnly.TryParse(dateTo, out to))
            return "Error: Invalid dateTo format. Use yyyy-MM-dd.";

        if (from > to)
            return "Error: dateFrom cannot be after dateTo.";

        var query = db.CashSessions
            .Include(s => s.CashCounts)
                .ThenInclude(c => c.Agent)
                    .ThenInclude(a => a!.User)
            .Include(s => s.Discrepancies)
            .AsQueryable();

        // Date filter
        query = query.Where(s => s.SessionDate >= from && s.SessionDate <= to);

        // Branch isolation
        var effectiveBranchId = config.CanQueryAllBranches ? branchId : config.BranchId;
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
            .ToListAsync();

        if (sessions.Count == 0)
            return "No cash sessions found matching the specified filters.";

        var lines = new List<string>
        {
            $"Found {sessions.Count} cash session(s) from {from:yyyy-MM-dd} to {to:yyyy-MM-dd}:",
            ""
        };

        foreach (var s in sessions)
        {
            var agents = s.CashCounts
                .Where(c => c.IsOpening && c.Agent != null)
                .Select(c => $"{c.Agent!.Code} ({c.Agent.User?.FullName ?? "N/A"})")
                .Distinct()
                .ToList();

            var openingTotal = s.CashCounts
                .Where(c => c.IsOpening && c.Status == CashCountStatus.Approved)
                .Sum(c => c.TotalAmount);
            var closingTotal = s.CashCounts
                .Where(c => !c.IsOpening && c.Status == CashCountStatus.Approved)
                .Sum(c => c.TotalAmount);

            lines.Add($"• Session #{s.Id} | {s.SessionDate:yyyy-MM-dd} | Agents: {string.Join(", ", agents)}");
            lines.Add($"  Status: {s.Status} | Branch: {s.BranchId}");
            lines.Add($"  Opening: {openingTotal:N2} | Closing: {closingTotal:N2}");
            lines.Add($"  Opened: {s.OpenedAt:yyyy-MM-dd HH:mm} | Closed: {(s.ClosedAt.HasValue ? s.ClosedAt.Value.ToString("yyyy-MM-dd HH:mm") : "Still open")}");
            if (s.Discrepancies.Any())
                lines.Add($"  ⚠️ {s.Discrepancies.Count} discrepancy(ies) flagged");
            lines.Add("");
        }

        if (sessions.Count == config.MaxRows)
            lines.Add($"(Results limited to {config.MaxRows} rows. Narrow your date range for more specific results.)");

        return string.Join("\n", lines);
    }
}

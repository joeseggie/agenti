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
                 "Returns session date, agent name, status, opening/closing totals, and discrepancy flag.")]
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
            .Include(s => s.Agent)
                .ThenInclude(a => a!.User)
            .Include(s => s.CashCounts)
            .Include(s => s.Discrepancies)
            .AsQueryable();

        // Date filter
        query = query.Where(s => s.SessionDate >= from && s.SessionDate <= to);

        // Branch isolation
        var effectiveBranchId = config.CanQueryAllBranches ? branchId : config.BranchId;
        if (effectiveBranchId.HasValue)
            query = query.Where(s => s.BranchId == effectiveBranchId.Value);

        // Agent filter
        if (!string.IsNullOrWhiteSpace(agentCode))
            query = query.Where(s => s.Agent != null && s.Agent.Code == agentCode.ToUpper());

        // Status filter
        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<CashSessionStatus>(status, ignoreCase: true, out var parsedStatus))
            query = query.Where(s => s.Status == parsedStatus);

        var sessions = await query
            .OrderByDescending(s => s.SessionDate)
            .ThenBy(s => s.Agent != null ? s.Agent.Code : "")
            .Take(config.MaxRows)
            .Select(s => new
            {
                s.Id,
                s.SessionDate,
                AgentCode = s.Agent != null ? s.Agent.Code : "N/A",
                AgentName = s.Agent != null && s.Agent.User != null
                    ? s.Agent.User.FirstName + " " + s.Agent.User.LastName
                    : "N/A",
                Status = s.Status.ToString(),
                s.BranchId,
                OpeningTotal = s.CashCounts
                    .Where(c => c.IsOpening)
                    .Sum(c => c.TotalAmount),
                ClosingTotal = s.CashCounts
                    .Where(c => !c.IsOpening)
                    .Sum(c => c.TotalAmount),
                HasDiscrepancy = s.Discrepancies.Any(),
                DiscrepancyCount = s.Discrepancies.Count,
                OpenedAt = s.OpenedAt.ToString("yyyy-MM-dd HH:mm"),
                ClosedAt = s.ClosedAt.HasValue
                    ? s.ClosedAt.Value.ToString("yyyy-MM-dd HH:mm")
                    : "Still open"
            })
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
            lines.Add($"• Session #{s.Id} | {s.SessionDate:yyyy-MM-dd} | Agent: {s.AgentCode} ({s.AgentName})");
            lines.Add($"  Status: {s.Status} | Branch: {s.BranchId}");
            lines.Add($"  Opening: {s.OpeningTotal:N2} | Closing: {s.ClosingTotal:N2}");
            lines.Add($"  Opened: {s.OpenedAt} | Closed: {s.ClosedAt}");
            if (s.HasDiscrepancy)
                lines.Add($"  ⚠️ {s.DiscrepancyCount} discrepancy(ies) flagged");
            lines.Add("");
        }

        if (sessions.Count == config.MaxRows)
            lines.Add($"(Results limited to {config.MaxRows} rows. Narrow your date range for more specific results.)");

        return string.Join("\n", lines);
    }
}

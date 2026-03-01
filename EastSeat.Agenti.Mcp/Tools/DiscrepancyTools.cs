using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using EastSeat.Agenti.Mcp.Configuration;
using EastSeat.Agenti.Mcp.Data;
using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Mcp.Tools;

[McpServerToolType]
public class DiscrepancyTools
{
    [McpServerTool(Name = "query_discrepancies"),
     Description("Query cash count discrepancies with date range, status, agent, and variance filters. " +
                 "Returns session info, expected/actual amounts, variance, status, and explanations.")]
    public static async Task<string> QueryDiscrepancies(
        ReadOnlyDbContext db,
        McpServerConfig config,
        [Description("Start date (yyyy-MM-dd format, required)")] string dateFrom,
        [Description("End date (yyyy-MM-dd format, defaults to today)")] string? dateTo = null,
        [Description("Filter by status: PendingReview, Approved, Rejected")] string? status = null,
        [Description("Filter by agent code (e.g., 'JODO')")] string? agentCode = null,
        [Description("Minimum absolute variance amount")] decimal? minVariance = null,
        [Description("Filter by branch ID (admin/supervisor only)")] long? branchId = null)
    {
        if (!DateOnly.TryParse(dateFrom, out var from))
            return "Error: Invalid dateFrom format. Use yyyy-MM-dd.";

        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        if (dateTo != null && !DateOnly.TryParse(dateTo, out to))
            return "Error: Invalid dateTo format. Use yyyy-MM-dd.";

        var query = db.Discrepancies
            .Include(d => d.CashSession)
                .ThenInclude(s => s!.Agent)
                    .ThenInclude(a => a!.User)
            .Include(d => d.CashCount)
            .AsQueryable();

        // Date filter via cash session date
        query = query.Where(d => d.CashSession != null &&
                                  d.CashSession.SessionDate >= from &&
                                  d.CashSession.SessionDate <= to);

        // Branch isolation
        var effectiveBranchId = config.CanQueryAllBranches ? branchId : config.BranchId;
        if (effectiveBranchId.HasValue)
            query = query.Where(d => d.CashSession != null && d.CashSession.BranchId == effectiveBranchId.Value);

        // Status filter
        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<DiscrepancyStatus>(status, ignoreCase: true, out var parsedStatus))
            query = query.Where(d => d.Status == parsedStatus);

        // Agent filter
        if (!string.IsNullOrWhiteSpace(agentCode))
            query = query.Where(d =>
                d.CashSession != null &&
                d.CashSession.Agent != null &&
                d.CashSession.Agent.Code == agentCode.ToUpper());

        // Variance filter
        if (minVariance.HasValue)
            query = query.Where(d => Math.Abs(d.Variance) >= minVariance.Value);

        var discrepancies = await query
            .OrderByDescending(d => d.CreatedAt)
            .Take(config.MaxRows)
            .Select(d => new
            {
                d.Id,
                SessionDate = d.CashSession != null ? d.CashSession.SessionDate.ToString("yyyy-MM-dd") : "N/A",
                AgentCode = d.CashSession != null && d.CashSession.Agent != null
                    ? d.CashSession.Agent.Code : "N/A",
                AgentName = d.CashSession != null && d.CashSession.Agent != null && d.CashSession.Agent.User != null
                    ? d.CashSession.Agent.User.FirstName + " " + d.CashSession.Agent.User.LastName : "N/A",
                Status = d.Status.ToString(),
                d.ExpectedAmount,
                d.ActualAmount,
                d.Variance,
                d.Reason,
                d.Explanation,
                d.ApprovalNotes,
                CreatedAt = d.CreatedAt.ToString("yyyy-MM-dd HH:mm")
            })
            .ToListAsync();

        if (discrepancies.Count == 0)
            return "No discrepancies found matching the specified filters.";

        var totalVariance = discrepancies.Sum(d => d.Variance);
        var lines = new List<string>
        {
            $"Found {discrepancies.Count} discrepancy(ies) from {from:yyyy-MM-dd} to {to:yyyy-MM-dd}:",
            $"Total net variance: {totalVariance:N2}",
            ""
        };

        foreach (var d in discrepancies)
        {
            var varianceSign = d.Variance >= 0 ? "+" : "";
            lines.Add($"⚠️ Discrepancy #{d.Id} | {d.SessionDate} | Agent: {d.AgentCode} ({d.AgentName})");
            lines.Add($"   Status: {d.Status}");
            lines.Add($"   Expected: {d.ExpectedAmount:N2} | Actual: {d.ActualAmount:N2} | Variance: {varianceSign}{d.Variance:N2}");
            if (!string.IsNullOrWhiteSpace(d.Reason))
                lines.Add($"   Reason: {d.Reason}");
            if (!string.IsNullOrWhiteSpace(d.Explanation))
                lines.Add($"   Explanation: {d.Explanation}");
            if (!string.IsNullOrWhiteSpace(d.ApprovalNotes))
                lines.Add($"   Approval Notes: {d.ApprovalNotes}");
            lines.Add($"   Created: {d.CreatedAt}");
            lines.Add("");
        }

        if (discrepancies.Count == config.MaxRows)
            lines.Add($"(Results limited to {config.MaxRows} rows.)");

        return string.Join("\n", lines);
    }
}

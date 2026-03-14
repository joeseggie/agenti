using System.ComponentModel;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using EastSeat.Agenti.Mcp.Configuration;
using EastSeat.Agenti.Mcp.Data;
using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Mcp.Tools;

[McpServerToolType]
public class TrendTools
{
    /// <summary>
    /// Hard ceiling for trend aggregation queries regardless of operator configuration.
    /// </summary>
    private const int MaxTrendRows = 10000;

    [McpServerTool(Name = "query_trends"),
     Description("Aggregate analytics across the Agenti system. " +
                 "Supports metrics: daily_totals, agent_performance, wallet_type_volume, discrepancy_rate, vault_flow. " +
                 "Returns time-series or summary data grouped by day, week, or month.")]
    public static async Task<string> QueryTrends(
        ReadOnlyDbContext db,
        McpServerConfig config,
        [Description("Metric to analyze: daily_totals, agent_performance, wallet_type_volume, discrepancy_rate, vault_flow")]
        string metric,
        [Description("Start date (yyyy-MM-dd format, required)")] string dateFrom,
        [Description("End date (yyyy-MM-dd format, defaults to today)")] string? dateTo = null,
        [Description("Group results by: day, week, month (default: day)")] string groupBy = "day",
        [Description("Filter by agent code (e.g., 'JODO')")] string? agentCode = null,
        [Description("Filter by branch ID (admin/supervisor only)")] long? branchId = null)
    {
        if (!DateOnly.TryParse(dateFrom, out var from))
            return "Error: Invalid dateFrom format. Use yyyy-MM-dd.";

        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        if (dateTo != null && !DateOnly.TryParse(dateTo, out to))
            return "Error: Invalid dateTo format. Use yyyy-MM-dd.";

        if (from > to)
            return "Error: dateFrom cannot be after dateTo.";

        // Limit date range to 1 year for performance
        if ((to.ToDateTime(TimeOnly.MinValue) - from.ToDateTime(TimeOnly.MinValue)).TotalDays > 366)
            return "Error: Date range cannot exceed 1 year. Please narrow your query.";

        var effectiveBranchId = config.CanQueryAllBranches ? branchId : config.BranchId;
        var limit = Math.Min(config.MaxRows, MaxTrendRows);

        return metric.ToLowerInvariant() switch
        {
            "daily_totals" => await DailyTotals(db, from, to, groupBy, agentCode, effectiveBranchId, limit),
            "agent_performance" => await AgentPerformance(db, from, to, agentCode, effectiveBranchId, limit),
            "wallet_type_volume" => await WalletTypeVolume(db, from, to, agentCode, effectiveBranchId, limit),
            "discrepancy_rate" => await DiscrepancyRate(db, from, to, groupBy, agentCode, effectiveBranchId, limit),
            "vault_flow" => await VaultFlow(db, from, to, groupBy, effectiveBranchId, limit),
            _ => "Error: Invalid metric. Choose from: daily_totals, agent_performance, wallet_type_volume, discrepancy_rate, vault_flow"
        };
    }

    private static async Task<string> DailyTotals(
        ReadOnlyDbContext db, DateOnly from, DateOnly to, string groupBy,
        string? agentCode, long? branchId, int limit)
    {
        var fromOffset = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toOffset = new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        var query = db.Transactions
            .Where(t => t.CreatedAt >= fromOffset && t.CreatedAt <= toOffset);

        if (branchId.HasValue)
            query = query.Where(t => t.CashSession != null && t.CashSession.BranchId == branchId.Value);

        if (!string.IsNullOrWhiteSpace(agentCode))
            query = query.Where(t =>
                t.CashSession != null && t.CashSession.Agent != null &&
                t.CashSession.Agent.Code == agentCode.ToUpper());

        // DB-level aggregation by date to avoid loading raw rows into memory
        var dailyAggregates = await query
            .GroupBy(t => t.CreatedAt.Date)
            .Select(g => new
            {
                Date = g.Key,
                Deposits = g.Where(t => t.Type == TransactionType.Deposit).Sum(t => t.Amount),
                Withdrawals = g.Where(t => t.Type == TransactionType.Withdrawal).Sum(t => t.Amount),
                Transfers = g.Where(t => t.Type == TransactionType.Transfer).Sum(t => t.Amount),
                Total = g.Sum(t => t.Amount),
                Count = g.Count()
            })
            .OrderBy(g => g.Date)
            .Take(limit)
            .ToListAsync();

        // Re-group by week/month if needed (in-memory, on already-aggregated data)
        var grouped = dailyAggregates
            .GroupBy(d => GroupDate(d.Date, groupBy))
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                Period = g.Key,
                Deposits = g.Sum(d => d.Deposits),
                Withdrawals = g.Sum(d => d.Withdrawals),
                Transfers = g.Sum(d => d.Transfers),
                Total = g.Sum(d => d.Total),
                Count = g.Sum(d => d.Count)
            })
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"Daily Totals ({from:yyyy-MM-dd} to {to:yyyy-MM-dd}, grouped by {groupBy}):");
        sb.AppendLine($"Total transactions: {grouped.Sum(g => g.Count)}");
        sb.AppendLine();
        sb.AppendLine("Period | Deposits | Withdrawals | Transfers | Total | Count");
        sb.AppendLine("-------|----------|-------------|-----------|-------|------");

        foreach (var g in grouped)
        {
            sb.AppendLine($"{g.Period} | {g.Deposits:N2} | {g.Withdrawals:N2} | {g.Transfers:N2} | {g.Total:N2} | {g.Count}");
        }

        if (dailyAggregates.Count == limit)
            sb.AppendLine($"(Results limited to {limit} rows. Some periods may be incomplete.)");

        return sb.ToString();
    }

    private static async Task<string> AgentPerformance(
        ReadOnlyDbContext db, DateOnly from, DateOnly to,
        string? agentCode, long? branchId, int limit)
    {
        var sessionQuery = db.CashSessions
            .Include(s => s.Agent).ThenInclude(a => a!.User)
            .Include(s => s.Discrepancies)
            .Where(s => s.SessionDate >= from && s.SessionDate <= to);

        if (branchId.HasValue)
            sessionQuery = sessionQuery.Where(s => s.BranchId == branchId.Value);

        if (!string.IsNullOrWhiteSpace(agentCode))
            sessionQuery = sessionQuery.Where(s => s.Agent != null && s.Agent.Code == agentCode.ToUpper());

        var sessions = await sessionQuery.Take(limit).ToListAsync();

        var agentStats = sessions
            .Where(s => s.Agent != null)
            .GroupBy(s => new { s.Agent!.Code, Name = s.Agent.User?.FullName ?? "N/A" })
            .Select(g => new
            {
                g.Key.Code,
                g.Key.Name,
                SessionCount = g.Count(),
                ClosedSessions = g.Count(s => s.Status == CashSessionStatus.Closed || s.Status == CashSessionStatus.Completed),
                OpenSessions = g.Count(s => s.Status == CashSessionStatus.Open),
                DiscrepancyCount = g.Sum(s => s.Discrepancies.Count),
                SessionsWithDiscrepancy = g.Count(s => s.Discrepancies.Any())
            })
            .OrderBy(a => a.Code)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"Agent Performance ({from:yyyy-MM-dd} to {to:yyyy-MM-dd}):");
        sb.AppendLine();

        foreach (var a in agentStats)
        {
            var discrepancyRate = a.SessionCount > 0
                ? (double)a.SessionsWithDiscrepancy / a.SessionCount * 100
                : 0;

            sb.AppendLine($"👤 {a.Code} ({a.Name})");
            sb.AppendLine($"   Sessions: {a.SessionCount} total | {a.ClosedSessions} closed | {a.OpenSessions} open");
            sb.AppendLine($"   Discrepancies: {a.DiscrepancyCount} total ({a.SessionsWithDiscrepancy} sessions, {discrepancyRate:F1}% rate)");
            sb.AppendLine();
        }

        if (agentStats.Count == 0)
            return "No agent performance data found for the specified period.";

        if (sessions.Count == limit)
            sb.AppendLine($"(Results limited to {limit} rows. Some periods may be incomplete.)");

        return sb.ToString();
    }

    private static async Task<string> WalletTypeVolume(
        ReadOnlyDbContext db, DateOnly from, DateOnly to,
        string? agentCode, long? branchId, int limit)
    {
        var fromOffset = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toOffset = new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        var query = db.Transactions
            .Include(t => t.FromWallet).ThenInclude(w => w!.WalletType)
            .Include(t => t.ToWallet).ThenInclude(w => w!.WalletType)
            .Where(t => t.CreatedAt >= fromOffset && t.CreatedAt <= toOffset);

        if (branchId.HasValue)
            query = query.Where(t => t.CashSession != null && t.CashSession.BranchId == branchId.Value);

        if (!string.IsNullOrWhiteSpace(agentCode))
            query = query.Where(t =>
                t.CashSession != null && t.CashSession.Agent != null &&
                t.CashSession.Agent.Code == agentCode.ToUpper());

        var transactions = await query.Take(limit).ToListAsync();

        // Aggregate by wallet type (from both sides of transactions)
        var walletVolumes = new Dictionary<string, (decimal Volume, int Count)>();

        foreach (var t in transactions)
        {
            var fromType = t.FromWallet?.WalletType?.Name ?? "Unknown";
            var toType = t.ToWallet?.WalletType?.Name ?? "Unknown";

            if (!walletVolumes.ContainsKey(fromType))
                walletVolumes[fromType] = (0, 0);
            walletVolumes[fromType] = (walletVolumes[fromType].Volume + t.Amount, walletVolumes[fromType].Count + 1);

            if (fromType != toType)
            {
                if (!walletVolumes.ContainsKey(toType))
                    walletVolumes[toType] = (0, 0);
                walletVolumes[toType] = (walletVolumes[toType].Volume + t.Amount, walletVolumes[toType].Count + 1);
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Wallet Type Volume ({from:yyyy-MM-dd} to {to:yyyy-MM-dd}):");
        sb.AppendLine();
        sb.AppendLine("Wallet Type | Total Volume | Transaction Count");
        sb.AppendLine("------------|-------------|------------------");

        foreach (var kv in walletVolumes.OrderByDescending(kv => kv.Value.Volume))
        {
            sb.AppendLine($"{kv.Key} | {kv.Value.Volume:N2} | {kv.Value.Count}");
        }

        if (transactions.Count == limit)
            sb.AppendLine($"(Results limited to {limit} rows. Some periods may be incomplete.)");

        return sb.ToString();
    }

    private static async Task<string> DiscrepancyRate(
        ReadOnlyDbContext db, DateOnly from, DateOnly to, string groupBy,
        string? agentCode, long? branchId, int limit)
    {
        var sessionQuery = db.CashSessions
            .Include(s => s.Discrepancies)
            .Where(s => s.SessionDate >= from && s.SessionDate <= to);

        if (branchId.HasValue)
            sessionQuery = sessionQuery.Where(s => s.BranchId == branchId.Value);

        if (!string.IsNullOrWhiteSpace(agentCode))
            sessionQuery = sessionQuery.Where(s => s.Agent != null && s.Agent.Code == agentCode.ToUpper());

        var sessions = await sessionQuery.Take(limit).ToListAsync();

        var grouped = sessions
            .GroupBy(s => GroupDate(s.OpenedAt.DateTime, groupBy))
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                Period = g.Key,
                TotalSessions = g.Count(),
                SessionsWithDiscrepancy = g.Count(s => s.Discrepancies.Any()),
                TotalDiscrepancies = g.Sum(s => s.Discrepancies.Count),
                TotalVariance = g.Sum(s => s.Discrepancies.Sum(d => d.Variance))
            })
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"Discrepancy Rate ({from:yyyy-MM-dd} to {to:yyyy-MM-dd}, grouped by {groupBy}):");
        sb.AppendLine();
        sb.AppendLine("Period | Sessions | With Discrepancy | Rate | Total Variance");
        sb.AppendLine("-------|----------|-----------------|------|---------------");

        foreach (var g in grouped)
        {
            var rate = g.TotalSessions > 0 ? (double)g.SessionsWithDiscrepancy / g.TotalSessions * 100 : 0;
            sb.AppendLine($"{g.Period} | {g.TotalSessions} | {g.SessionsWithDiscrepancy} | {rate:F1}% | {g.TotalVariance:N2}");
        }

        if (sessions.Count == limit)
            sb.AppendLine($"(Results limited to {limit} rows. Some periods may be incomplete.)");

        return sb.ToString();
    }

    private static async Task<string> VaultFlow(
        ReadOnlyDbContext db, DateOnly from, DateOnly to, string groupBy, long? branchId, int limit)
    {
        var fromOffset = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toOffset = new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        var query = db.VaultTransactions
            .Include(vt => vt.Vault)
            .Where(vt => vt.CreatedAt >= fromOffset && vt.CreatedAt <= toOffset &&
                         vt.Status == VaultTransactionStatus.Completed);

        if (branchId.HasValue)
            query = query.Where(vt => vt.Vault != null && vt.Vault.BranchId == branchId.Value);

        var transactions = await query
            .Select(vt => new { vt.Type, vt.Amount, vt.CreatedAt })
            .Take(limit)
            .ToListAsync();

        var grouped = transactions
            .GroupBy(vt => GroupDate(vt.CreatedAt.DateTime, groupBy))
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                Period = g.Key,
                Inflow = g.Where(t =>
                    t.Type == VaultTransactionType.Closing ||
                    t.Type == VaultTransactionType.ManualDeposit)
                    .Sum(t => t.Amount),
                Outflow = g.Where(t =>
                    t.Type == VaultTransactionType.Opening ||
                    t.Type == VaultTransactionType.ManualWithdrawal)
                    .Sum(t => t.Amount),
                TransactionCount = g.Count()
            })
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"Vault Flow ({from:yyyy-MM-dd} to {to:yyyy-MM-dd}, grouped by {groupBy}):");
        sb.AppendLine();
        sb.AppendLine("Period | Inflow | Outflow | Net Flow | Txn Count");
        sb.AppendLine("-------|--------|---------|----------|----------");

        foreach (var g in grouped)
        {
            var netFlow = g.Inflow - g.Outflow;
            var sign = netFlow >= 0 ? "+" : "";
            sb.AppendLine($"{g.Period} | {g.Inflow:N2} | {g.Outflow:N2} | {sign}{netFlow:N2} | {g.TransactionCount}");
        }

        if (transactions.Count == limit)
            sb.AppendLine($"(Results limited to {limit} rows. Some periods may be incomplete.)");

        return sb.ToString();
    }

    private static string GroupDate(DateTime date, string groupBy)
    {
        return groupBy.ToLowerInvariant() switch
        {
            "week" => $"{date:yyyy}-W{System.Globalization.ISOWeek.GetWeekOfYear(date):D2}",
            "month" => $"{date:yyyy-MM}",
            _ => $"{date:yyyy-MM-dd}"
        };
    }
}

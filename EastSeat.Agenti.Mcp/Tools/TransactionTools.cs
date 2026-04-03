using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using EastSeat.Agenti.Mcp.Configuration;
using EastSeat.Agenti.Mcp.Data;
using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Mcp.Tools;

[McpServerToolType]
public class TransactionTools
{
    [McpServerTool(Name = "query_transactions"),
     Description("Query transaction history with date range, type, agent, and amount filters. " +
                 "Returns transaction type, amount, wallets involved, reference, and timestamp.")]
    public static async Task<string> QueryTransactions(
        ReadOnlyDbContext db,
        McpServerConfig config,
        [Description("Start date (yyyy-MM-dd format, required)")] string dateFrom,
        [Description("End date (yyyy-MM-dd format, defaults to today)")] string? dateTo = null,
        [Description("Filter by transaction type: Deposit, Withdrawal, Transfer, Adjustment, Reversal")]
        string? type = null,
        [Description("Filter by agent code (e.g., 'JODO')")] string? agentCode = null,
        [Description("Minimum transaction amount")] decimal? minAmount = null,
        [Description("Maximum transaction amount")] decimal? maxAmount = null,
        [Description("Filter by branch ID (admin/supervisor only)")] long? branchId = null)
    {
        if (!DateOnly.TryParse(dateFrom, out var from))
            return "Error: Invalid dateFrom format. Use yyyy-MM-dd.";

        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        if (dateTo != null && !DateOnly.TryParse(dateTo, out to))
            return "Error: Invalid dateTo format. Use yyyy-MM-dd.";

        var fromOffset = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toOffset = new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        var query = db.Transactions
            .Include(t => t.CashSession)
                .ThenInclude(s => s!.CashCounts)
                    .ThenInclude(c => c.Agent)
            .Include(t => t.FromWallet)
                .ThenInclude(w => w!.WalletType)
            .Include(t => t.ToWallet)
                .ThenInclude(w => w!.WalletType)
            .Where(t => t.CreatedAt >= fromOffset && t.CreatedAt <= toOffset);

        // Branch isolation
        var effectiveBranchId = config.CanQueryAllBranches ? branchId : config.BranchId;
        if (effectiveBranchId.HasValue)
            query = query.Where(t => t.CashSession != null && t.CashSession.BranchId == effectiveBranchId.Value);

        // Type filter
        if (!string.IsNullOrWhiteSpace(type) &&
            Enum.TryParse<TransactionType>(type, ignoreCase: true, out var parsedType))
            query = query.Where(t => t.Type == parsedType);

        // Agent filter
        if (!string.IsNullOrWhiteSpace(agentCode))
            query = query.Where(t =>
                t.CashSession != null &&
                t.CashSession.CashCounts.Any(c => c.Agent != null && c.Agent.Code == agentCode.ToUpper()));

        // Amount filters
        if (minAmount.HasValue)
            query = query.Where(t => t.Amount >= minAmount.Value);
        if (maxAmount.HasValue)
            query = query.Where(t => t.Amount <= maxAmount.Value);

        var transactions = await query
            .OrderByDescending(t => t.CreatedAt)
            .Take(config.MaxRows)
            .Select(t => new
            {
                t.Id,
                Type = t.Type.ToString(),
                t.Amount,
                t.Currency,
                t.Reference,
                t.Notes,
                FromWallet = t.FromWallet != null ? t.FromWallet.Name : "N/A",
                FromWalletType = t.FromWallet != null && t.FromWallet.WalletType != null
                    ? t.FromWallet.WalletType.Name : "N/A",
                ToWallet = t.ToWallet != null ? t.ToWallet.Name : "N/A",
                ToWalletType = t.ToWallet != null && t.ToWallet.WalletType != null
                    ? t.ToWallet.WalletType.Name : "N/A",
                AgentCode = t.CashSession != null
                    ? t.CashSession.CashCounts
                        .Where(c => c.IsOpening && c.Agent != null)
                        .Select(c => c.Agent!.Code)
                        .FirstOrDefault() ?? "N/A"
                    : "N/A",
                CreatedAt = t.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                IsReversed = t.ReversedAt.HasValue
            })
            .ToListAsync();

        if (transactions.Count == 0)
            return "No transactions found matching the specified filters.";

        var lines = new List<string>
        {
            $"Found {transactions.Count} transaction(s) from {from:yyyy-MM-dd} to {to:yyyy-MM-dd}:",
            ""
        };

        foreach (var t in transactions)
        {
            lines.Add($"• Txn #{t.Id} | {t.Type} | {t.Amount:N2} {t.Currency} | Agent: {t.AgentCode}");
            lines.Add($"  From: {t.FromWallet} ({t.FromWalletType}) → To: {t.ToWallet} ({t.ToWalletType})");
            if (!string.IsNullOrWhiteSpace(t.Reference))
                lines.Add($"  Ref: {t.Reference}");
            if (t.IsReversed)
                lines.Add("  ↩️ REVERSED");
            lines.Add($"  Created: {t.CreatedAt}");
            lines.Add("");
        }

        if (transactions.Count == config.MaxRows)
            lines.Add($"(Results limited to {config.MaxRows} rows.)");

        return string.Join("\n", lines);
    }
}

using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using EastSeat.Agenti.Mcp.Configuration;
using EastSeat.Agenti.Mcp.Data;

namespace EastSeat.Agenti.Mcp.Tools;

[McpServerToolType]
public class VaultTools
{
    [McpServerTool(Name = "query_vault_balance"),
     Description("Get current vault balance and recent transaction summary for a branch. " +
                 "Returns current balance, today's deposits/withdrawals, and pending adjustments.")]
    public static async Task<string> QueryVaultBalance(
        ReadOnlyDbContext db,
        McpServerConfig config,
        [Description("Branch ID (admin/supervisor only, agents use their configured branch)")]
        long? branchId = null,
        [Description("Include last N vault transactions (default: 10, max: 50)")]
        int? includeHistory = null)
    {
        var effectiveBranchId = config.CanQueryAllBranches ? branchId : config.BranchId;

        var vaultQuery = db.Vaults
            .Include(v => v.Branch)
            .AsQueryable();

        if (effectiveBranchId.HasValue)
            vaultQuery = vaultQuery.Where(v => v.BranchId == effectiveBranchId.Value);

        var vaults = await vaultQuery.ToListAsync();

        if (vaults.Count == 0)
            return effectiveBranchId.HasValue
                ? $"No vault found for branch {effectiveBranchId.Value}."
                : "No vaults found.";

        var lines = new List<string>();

        // Precompute today's date range once
        var today = DateTimeOffset.UtcNow.Date;
        var todayStart = new DateTimeOffset(today, TimeSpan.Zero);
        var todayEnd = todayStart.AddDays(1);

        // Collect all vault IDs to batch queries
        var vaultIds = vaults.Select(v => v.Id).ToList();

        // Batch query: today's transactions for all relevant vaults
        var allTodayTransactions = await db.VaultTransactions
            .Where(vt => vaultIds.Contains(vt.VaultId) &&
                         vt.CreatedAt >= todayStart &&
                         vt.CreatedAt < todayEnd)
            .ToListAsync();

        var todayByVault = allTodayTransactions
            .GroupBy(vt => vt.VaultId)
            .ToDictionary(
                g => g.Key,
                g =>
                (
                    Deposits: g.Where(vt => vt.Type == Shared.Domain.Enums.VaultTransactionType.Closing ||
                                            vt.Type == Shared.Domain.Enums.VaultTransactionType.ManualDeposit)
                                .Sum(vt => vt.Amount),
                    Withdrawals: g.Where(vt => vt.Type == Shared.Domain.Enums.VaultTransactionType.Opening ||
                                               vt.Type == Shared.Domain.Enums.VaultTransactionType.ManualWithdrawal)
                                  .Sum(vt => vt.Amount)
                ));

        // Batch query: pending transaction counts for all relevant vaults
        var pendingCounts = await db.VaultTransactions
            .Where(vt => vaultIds.Contains(vt.VaultId) &&
                         vt.Status == Shared.Domain.Enums.VaultTransactionStatus.Pending)
            .GroupBy(vt => vt.VaultId)
            .Select(g => new { VaultId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.VaultId, x => x.Count);

        foreach (var vault in vaults)
        {
            // Lookup today's summary for this vault
            if (!todayByVault.TryGetValue(vault.Id, out var todaySummary))
            {
                todaySummary = (Deposits: 0m, Withdrawals: 0m);
            }

            var todayDeposits = todaySummary.Deposits;
            var todayWithdrawals = todaySummary.Withdrawals;

            // Lookup pending count for this vault
            if (!pendingCounts.TryGetValue(vault.Id, out var pendingCount))
            {
                pendingCount = 0;
            }
            lines.Add($"🏦 Vault for Branch: {vault.Branch?.Name ?? vault.BranchId.ToString()} (ID: {vault.BranchId})");
            lines.Add($"  Current Balance: {vault.CurrentBalance:N2}");
            lines.Add($"  Today's Deposits: {todayDeposits:N2}");
            lines.Add($"  Today's Withdrawals: {todayWithdrawals:N2}");
            lines.Add($"  Pending Adjustments: {pendingCount}");
            lines.Add($"  Last Updated: {vault.UpdatedAt?.ToString("yyyy-MM-dd HH:mm") ?? "N/A"}");

            // Include history if requested
            var historyCount = Math.Clamp(includeHistory ?? 0, 0, 50);
            if (historyCount > 0)
            {
                var history = await db.VaultTransactions
                    .Where(vt => vt.VaultId == vault.Id)
                    .OrderByDescending(vt => vt.CreatedAt)
                    .Take(historyCount)
                    .Select(vt => new
                    {
                        vt.Id,
                        Type = vt.Type.ToString(),
                        Status = vt.Status.ToString(),
                        vt.Amount,
                        vt.BalanceAfter,
                        vt.Notes,
                        CreatedAt = vt.CreatedAt.ToString("yyyy-MM-dd HH:mm")
                    })
                    .ToListAsync();

                lines.Add("");
                lines.Add($"  Recent Transactions ({history.Count}):");
                foreach (var h in history)
                {
                    lines.Add($"    • #{h.Id} | {h.Type} | {h.Status} | {h.Amount:N2} | Balance After: {h.BalanceAfter?.ToString("N2") ?? "N/A"} | {h.CreatedAt}");
                    if (!string.IsNullOrWhiteSpace(h.Notes))
                        lines.Add($"      Notes: {h.Notes}");
                }
            }

            lines.Add("");
        }

        return string.Join("\n", lines);
    }
}

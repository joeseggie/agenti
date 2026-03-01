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

        foreach (var vault in vaults)
        {
            var today = DateTimeOffset.UtcNow.Date;
            var todayStart = new DateTimeOffset(today, TimeSpan.Zero);
            var todayEnd = todayStart.AddDays(1);

            var todayTransactions = await db.VaultTransactions
                .Where(vt => vt.VaultId == vault.Id &&
                             vt.CreatedAt >= todayStart &&
                             vt.CreatedAt < todayEnd)
                .ToListAsync();

            var todayDeposits = todayTransactions
                .Where(vt => vt.Type == Shared.Domain.Enums.VaultTransactionType.Closing ||
                             vt.Type == Shared.Domain.Enums.VaultTransactionType.ManualDeposit)
                .Sum(vt => vt.Amount);

            var todayWithdrawals = todayTransactions
                .Where(vt => vt.Type == Shared.Domain.Enums.VaultTransactionType.Opening ||
                             vt.Type == Shared.Domain.Enums.VaultTransactionType.ManualWithdrawal)
                .Sum(vt => vt.Amount);

            var pendingCount = await db.VaultTransactions
                .Where(vt => vt.VaultId == vault.Id &&
                             vt.Status == Shared.Domain.Enums.VaultTransactionStatus.Pending)
                .CountAsync();

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

using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using EastSeat.Agenti.Mcp.Configuration;
using EastSeat.Agenti.Mcp.Data;
using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Mcp.Tools;

[McpServerToolType]
public class AgentTools
{
    [McpServerTool(Name = "query_agent_wallets"),
     Description("Query agent wallet balances and status. " +
                 "Returns agent details with wallet breakdowns including balance per wallet type.")]
    public static async Task<string> QueryAgentWallets(
        ReadOnlyDbContext db,
        McpServerConfig config,
        [Description("Filter by agent code (e.g., 'JODO')")] string? agentCode = null,
        [Description("Filter by wallet type: Cash, MobileMoney, Bank, Custom")] string? walletType = null,
        [Description("Include inactive agents/wallets (default: false)")] bool includeInactive = false,
        [Description("Filter by branch ID (admin/supervisor only)")] long? branchId = null)
    {
        var query = db.Agents
            .Include(a => a.User)
            .Include(a => a.Wallets)
                .ThenInclude(w => w.WalletType)
            .AsQueryable();

        // Branch isolation
        var effectiveBranchId = config.CanQueryAllBranches ? branchId : config.BranchId;
        if (effectiveBranchId.HasValue)
            query = query.Where(a => a.BranchId == effectiveBranchId.Value);

        if (!includeInactive)
            query = query.Where(a => a.IsActive);

        if (!string.IsNullOrWhiteSpace(agentCode))
            query = query.Where(a => a.Code == agentCode.ToUpper());

        var agents = await query
            .OrderBy(a => a.Code)
            .Take(config.MaxRows)
            .ToListAsync();

        if (agents.Count == 0)
            return "No agents found matching the specified filters.";

        var lines = new List<string>
        {
            $"Found {agents.Count} agent(s):",
            ""
        };

        foreach (var agent in agents)
        {
            var wallets = agent.Wallets
                .Where(w => includeInactive || w.IsActive)
                .ToList();

            if (!string.IsNullOrWhiteSpace(walletType))
            {
                if (Enum.TryParse<WalletTypeEnum>(walletType, ignoreCase: true, out var parsedType))
                    wallets = wallets.Where(w => w.WalletType?.Type == parsedType).ToList();
            }

            var totalBalance = wallets.Sum(w => w.Balance);

            lines.Add($"👤 Agent: {agent.Code} | {agent.User?.FullName ?? "N/A"} | Branch: {agent.BranchId}");
            lines.Add($"   Active: {agent.IsActive} | Wallets: {wallets.Count} | Total Balance: {totalBalance:N2}");

            foreach (var wallet in wallets.OrderBy(w => w.WalletType?.Name))
            {
                var status = wallet.IsActive ? "✅" : "❌";
                lines.Add($"   {status} {wallet.Name} ({wallet.WalletType?.Name ?? "Unknown"}) | Balance: {wallet.Balance:N2} {wallet.Currency}");
            }

            lines.Add("");
        }

        return string.Join("\n", lines);
    }
}

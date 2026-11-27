using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using EastSeat.Agenti.Mcp.Configuration;
using EastSeat.Agenti.Mcp.Data;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Mcp.Authentication;

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
        McpCaller caller,
        [Description("Filter by agent code (e.g., 'JODO')")] string? agentCode = null,
        [Description("Filter by wallet type: Cash, MobileMoney, Bank, Custom")] string? walletType = null,
        [Description("Include inactive agents/wallets (default: false)")] bool includeInactive = false,
        [Description("Filter by branch ID (supervisors are restricted to their assigned branch)")] long? branchId = null,
        CancellationToken cancellationToken = default)
    {
        var query = db.Agents.AsQueryable();

        // Branch isolation
        var effectiveBranchId = caller.ResolveBranch(branchId);
        if (effectiveBranchId.HasValue)
            query = query.Where(a => a.BranchId == effectiveBranchId.Value);

        if (!includeInactive)
            query = query.Where(a => a.IsActive);

        if (!string.IsNullOrWhiteSpace(agentCode))
            query = query.Where(a => a.Code == agentCode.ToUpper());

        var agents = await query
            .OrderBy(a => a.Code)
            .Take(config.MaxRows)
            .Select(a => new
            {
                a.Code, a.BranchId, a.IsActive,
                FullName = a.User == null ? "N/A" : (a.User.FirstName + " " + a.User.LastName).Trim(),
                Wallets = a.Wallets.Where(w => includeInactive || w.IsActive)
                    .OrderBy(w => w.Name).Take(config.MaxRows)
                    .Select(w => new
                    {
                        w.Name, w.Balance, w.Currency, w.IsActive,
                        Type = w.WalletType == null ? (WalletTypeEnum?)null : w.WalletType.Type,
                        TypeName = w.WalletType == null ? "Unknown" : w.WalletType.Name
                    }).ToList()
            })
            .ToListAsync(cancellationToken);

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
                    wallets = wallets.Where(w => w.Type == parsedType).ToList();
            }

            var totalBalance = wallets.Sum(w => w.Balance);

            lines.Add($"👤 Agent: {agent.Code} | {agent.FullName} | Branch: {agent.BranchId}");
            lines.Add($"   Active: {agent.IsActive} | Wallets: {wallets.Count} | Total Balance: {totalBalance:N2}");

            foreach (var wallet in wallets.OrderBy(w => w.TypeName))
            {
                var status = wallet.IsActive ? "✅" : "❌";
                lines.Add($"   {status} {wallet.Name} ({wallet.TypeName}) | Balance: {wallet.Balance:N2} {wallet.Currency}");
            }

            if (agent.Wallets.Count == config.MaxRows)
                lines.Add($"   Wallet results limited to {config.MaxRows}; balance totals cover displayed wallets only.");
            lines.Add("");
        }
        if (agents.Count == config.MaxRows)
            lines.Add($"Results limited to {config.MaxRows} agents.");

        return string.Join("\n", lines);
    }
}

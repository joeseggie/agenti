using System.Security.Claims;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Agents;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.Api;

/// <summary>
/// API endpoints for agent management.
/// </summary>
public static class AgentEndpoints
{
    public static RouteGroupBuilder MapAgentsApi(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (ClaimsPrincipal principal, IBankingAccessService access, IAgentService agentService) =>
        {
            var actor = await ApiAccess.ActorAsync(principal, access);
            var agents = await agentService.GetAgentsAsync(actor.BranchId, ApiAccess.AgentFilter(actor));
            return Results.Ok(ApiResponse<List<AgentListItemDto>>.Ok(agents));
        })
        .RequireAuthorization()
        .WithName("GetAgents")
        .WithSummary("Get agents visible to the authenticated user in their branch");

        group.MapGet("/{agentId:long}", async (long agentId, ClaimsPrincipal principal,
            IBankingAccessService access, ApplicationDbContext db, IAgentService agentService) =>
        {
            await ApiAccess.RequireAgentAsync(await ApiAccess.ActorAsync(principal, access), db, agentId);
            var agent = await agentService.GetAgentAsync(agentId);
            return agent is null
                ? Results.NotFound(ApiResponse<AgentDetailDto>.Fail("Agent not found."))
                : Results.Ok(ApiResponse<AgentDetailDto>.Ok(agent));
        })
        .RequireAuthorization()
        .WithName("GetAgent")
        .WithSummary("Get agent details by ID");

        group.MapPost("/", async (
            AgentFormModel model,
            ClaimsPrincipal principal,
            IBankingAccessService access,
            ApplicationDbContext db,
            IAgentService agentService) =>
        {
            var actor = await ApiAccess.ActorAsync(principal, access);
            ApiAccess.RequireBranch(actor, model.BranchId ?? actor.BranchId);
            if (!await db.Users.AnyAsync(u => u.Id == model.UserId && u.BranchId == actor.BranchId))
                throw new UnauthorizedAccessException("The user must belong to your branch.");
            model.BranchId = actor.BranchId;
            var result = await agentService.CreateAgentAsync(model);
            return result.Success
                ? Results.Created($"/api/agents/{result.Id}", ApiResponse<SaveResult>.Ok(result))
                : Results.BadRequest(ApiResponse<SaveResult>.Fail(result.ErrorMessage ?? "Failed to create agent."));
        })
        .RequireAuthorization("UserManagementApi")
        .WithName("CreateAgent")
        .WithSummary("Create a new agent");

        group.MapPut("/{agentId:long}", async (
            long agentId,
            AgentFormModel model,
            ClaimsPrincipal principal,
            IBankingAccessService access,
            ApplicationDbContext db,
            IAgentService agentService) =>
        {
            var actor = await ApiAccess.ActorAsync(principal, access);
            await ApiAccess.RequireAgentAsync(actor, db, agentId);
            ApiAccess.RequireBranch(actor, model.BranchId ?? actor.BranchId);
            model.BranchId = actor.BranchId;
            model.Id = agentId;
            var result = await agentService.UpdateAgentAsync(model);
            return result.Success
                ? Results.Ok(ApiResponse<SaveResult>.Ok(result))
                : Results.BadRequest(ApiResponse<SaveResult>.Fail(result.ErrorMessage ?? "Failed to update agent."));
        })
        .RequireAuthorization("UserManagementApi")
        .WithName("UpdateAgent")
        .WithSummary("Update an existing agent");

        group.MapPost("/{agentId:long}/toggle-status", async (
            long agentId,
            ClaimsPrincipal principal,
            IBankingAccessService access,
            ApplicationDbContext db,
            IAgentService agentService) =>
        {
            await ApiAccess.RequireAgentAsync(await ApiAccess.ActorAsync(principal, access), db, agentId);
            var result = await agentService.ToggleAgentStatusAsync(agentId);
            return result.Success
                ? Results.Ok(ApiResponse<SaveResult>.Ok(result))
                : Results.BadRequest(ApiResponse<SaveResult>.Fail(result.ErrorMessage ?? "Failed to toggle agent status."));
        })
        .RequireAuthorization("UserManagementApi")
        .WithName("ToggleAgentStatus")
        .WithSummary("Toggle agent active/inactive status");

        group.MapGet("/{agentId:long}/wallets", async (long agentId, ClaimsPrincipal principal,
            IBankingAccessService access, ApplicationDbContext db, IAgentService agentService) =>
        {
            await ApiAccess.RequireAgentAsync(await ApiAccess.ActorAsync(principal, access), db, agentId);
            var wallets = await agentService.GetAgentWalletsAsync(agentId);
            return Results.Ok(ApiResponse<List<AgentWalletDto>>.Ok(wallets));
        })
        .RequireAuthorization()
        .WithName("GetAgentWallets")
        .WithSummary("Get wallets for a specific agent");

        group.MapPost("/{agentId:long}/wallets", async (
            long agentId,
            WalletFormModel model,
            ClaimsPrincipal principal,
            IBankingAccessService access,
            ApplicationDbContext db,
            IAgentService agentService) =>
        {
            await ApiAccess.RequireAgentAsync(await ApiAccess.ActorAsync(principal, access), db, agentId);
            model.AgentId = agentId;
            var result = await agentService.AddWalletAsync(model);
            return result.Success
                ? Results.Created($"/api/agents/{agentId}/wallets/{result.Id}", ApiResponse<SaveResult>.Ok(result))
                : Results.BadRequest(ApiResponse<SaveResult>.Fail(result.ErrorMessage ?? "Failed to add wallet."));
        })
        .RequireAuthorization("UserManagementApi")
        .WithName("AddAgentWallet")
        .WithSummary("Add a wallet to an agent");

        group.MapGet("/branches", async (ClaimsPrincipal principal, IBankingAccessService access, IAgentService agentService) =>
        {
            var actor = await ApiAccess.ActorAsync(principal, access);
            var branches = await agentService.GetBranchesAsync();
            return Results.Ok(ApiResponse<List<BranchDto>>.Ok(branches.Where(b => b.Id == actor.BranchId).ToList()));
        })
        .RequireAuthorization()
        .WithName("GetBranches")
        .WithSummary("Get the authenticated user's branch");

        return group;
    }
}

using System.Security.Claims;
using EastSeat.Agenti.Web.Features.Agents;

namespace EastSeat.Agenti.Web.Features.Api;

/// <summary>
/// API endpoints for agent management.
/// </summary>
public static class AgentEndpoints
{
    public static RouteGroupBuilder MapAgentsApi(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (IAgentService agentService) =>
        {
            var agents = await agentService.GetAgentsAsync();
            return Results.Ok(ApiResponse<List<AgentListItemDto>>.Ok(agents));
        })
        .RequireAuthorization()
        .WithName("GetAgents")
        .WithSummary("Get list of all agents");

        group.MapGet("/{agentId:long}", async (long agentId, IAgentService agentService) =>
        {
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
            IAgentService agentService) =>
        {
            var result = await agentService.CreateAgentAsync(model);
            return result.Success
                ? Results.Created($"/api/agents/{result.Id}", ApiResponse<SaveResult>.Ok(result))
                : Results.BadRequest(ApiResponse<SaveResult>.Fail(result.ErrorMessage ?? "Failed to create agent."));
        })
        .RequireAuthorization("UserManagement")
        .WithName("CreateAgent")
        .WithSummary("Create a new agent");

        group.MapPut("/{agentId:long}", async (
            long agentId,
            AgentFormModel model,
            IAgentService agentService) =>
        {
            model.Id = agentId;
            var result = await agentService.UpdateAgentAsync(model);
            return result.Success
                ? Results.Ok(ApiResponse<SaveResult>.Ok(result))
                : Results.BadRequest(ApiResponse<SaveResult>.Fail(result.ErrorMessage ?? "Failed to update agent."));
        })
        .RequireAuthorization("UserManagement")
        .WithName("UpdateAgent")
        .WithSummary("Update an existing agent");

        group.MapPost("/{agentId:long}/toggle-status", async (
            long agentId,
            IAgentService agentService) =>
        {
            var result = await agentService.ToggleAgentStatusAsync(agentId);
            return result.Success
                ? Results.Ok(ApiResponse<SaveResult>.Ok(result))
                : Results.BadRequest(ApiResponse<SaveResult>.Fail(result.ErrorMessage ?? "Failed to toggle agent status."));
        })
        .RequireAuthorization("UserManagement")
        .WithName("ToggleAgentStatus")
        .WithSummary("Toggle agent active/inactive status");

        group.MapGet("/{agentId:long}/wallets", async (long agentId, IAgentService agentService) =>
        {
            var wallets = await agentService.GetAgentWalletsAsync(agentId);
            return Results.Ok(ApiResponse<List<AgentWalletDto>>.Ok(wallets));
        })
        .RequireAuthorization()
        .WithName("GetAgentWallets")
        .WithSummary("Get wallets for a specific agent");

        group.MapPost("/{agentId:long}/wallets", async (
            long agentId,
            WalletFormModel model,
            IAgentService agentService) =>
        {
            model.AgentId = agentId;
            var result = await agentService.AddWalletAsync(model);
            return result.Success
                ? Results.Created($"/api/agents/{agentId}/wallets/{result.Id}", ApiResponse<SaveResult>.Ok(result))
                : Results.BadRequest(ApiResponse<SaveResult>.Fail(result.ErrorMessage ?? "Failed to add wallet."));
        })
        .RequireAuthorization("UserManagement")
        .WithName("AddAgentWallet")
        .WithSummary("Add a wallet to an agent");

        group.MapGet("/branches", async (IAgentService agentService) =>
        {
            var branches = await agentService.GetBranchesAsync();
            return Results.Ok(ApiResponse<List<BranchDto>>.Ok(branches));
        })
        .RequireAuthorization()
        .WithName("GetBranches")
        .WithSummary("Get all branches");

        return group;
    }
}

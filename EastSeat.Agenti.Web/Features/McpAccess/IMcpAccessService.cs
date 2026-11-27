using System.Security.Claims;

namespace EastSeat.Agenti.Web.Features.McpAccess;

public interface IMcpAccessService
{
    Task<CreatedMcpToken> CreateAsync(ClaimsPrincipal actor, CreateMcpTokenRequest request, CancellationToken cancellationToken = default);
    Task<McpTokenPage> ListAsync(ClaimsPrincipal actor, bool allUsers = false, int page = 0, CancellationToken cancellationToken = default);
    Task RevokeAsync(ClaimsPrincipal actor, long tokenId, CancellationToken cancellationToken = default);
}

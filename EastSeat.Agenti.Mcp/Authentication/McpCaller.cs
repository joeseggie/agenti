using EastSeat.Agenti.Shared.Domain.Enums;
using ModelContextProtocol;

namespace EastSeat.Agenti.Mcp.Authentication;

public sealed record McpCaller(string UserId, UserRole Role, long? BranchId, long TokenId)
{
    public long? ResolveBranch(long? requestedBranchId)
    {
        if (requestedBranchId is <= 0)
            throw new McpException("branchId must be a positive integer.");
        if (Role == UserRole.Admin)
            return requestedBranchId;
        if (Role != UserRole.Supervisor || BranchId is null or <= 0 ||
            (requestedBranchId.HasValue && requestedBranchId != BranchId))
            throw new McpException("Access denied: this branch is outside your permitted scope.");
        return BranchId;
    }
}

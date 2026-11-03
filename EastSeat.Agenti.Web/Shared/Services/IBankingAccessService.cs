using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Shared.Services;

public sealed record BankingActor(string UserId, long BranchId, long? AgentId, UserRole Role)
{
    public void RequireAdmin()
    {
        if (Role != UserRole.Admin)
            throw new UnauthorizedAccessException("Only administrators may access bank reconciliation.");
    }
}

public sealed record BankAccountScope(
    long Id, long BranchId, long WalletId, long AgentId, string Currency, string BankName,
    bool IsActive, bool WorkflowEnabled, bool ImportProfileVerified);

public interface IBankingAccessService
{
    Task<BankingActor> GetActorAsync(string userId, CancellationToken cancellationToken = default);
    Task<BankAccountScope> RequireAccountAsync(
        string userId, long accountId, bool adminOnly = false, CancellationToken cancellationToken = default);
    Task RequireTradingSessionAsync(
        BankingActor actor, long sessionId, CancellationToken cancellationToken = default);
}

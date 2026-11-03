namespace EastSeat.Agenti.Shared.Services;

public sealed record SessionSettlementResult(
    long CashCountId, long CashSessionId, long SnapshotId, bool AlreadySettled);

public interface ISessionSettlementService
{
    Task<SessionSettlementResult> ApproveOpeningAsync(
        string userId, long cashCountId, CancellationToken cancellationToken = default);
    Task<SessionSettlementResult> SubmitClosingAsync(
        string userId, long cashCountId, CancellationToken cancellationToken = default);
}

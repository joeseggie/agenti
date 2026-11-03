namespace EastSeat.Agenti.Shared.Services;

public interface ISessionBalanceService
{
    Task<List<SessionWalletPosition>> GetPositionsAsync(
        long sessionId, long agentId, bool includePendingAdjustments = false, CancellationToken cancellationToken = default);

    Task<List<SessionWalletPosition>> GetPositionsAtSnapshotAsync(
        long snapshotId, bool includePendingAdjustments = false, CancellationToken cancellationToken = default);
}

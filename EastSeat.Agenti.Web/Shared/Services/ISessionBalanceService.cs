namespace EastSeat.Agenti.Shared.Services;

public interface ISessionBalanceService
{
    Task<List<SessionWalletPosition>> GetPositionsAsync(
        long sessionId, long agentId, bool includePendingAdjustments = false, CancellationToken cancellationToken = default,
        IReadOnlySet<long>? comparisonWalletIds = null);

    Task<List<SessionWalletPosition>> GetPositionsAtSnapshotAsync(
        long snapshotId, bool includePendingAdjustments = false, CancellationToken cancellationToken = default,
        IReadOnlySet<long>? comparisonWalletIds = null);
}

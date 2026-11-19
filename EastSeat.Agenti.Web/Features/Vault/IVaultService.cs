using EastSeat.Agenti.Web.Features.Vaults;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;

namespace EastSeat.Agenti.Web.Features.Vaults;

/// <summary>
/// Service interface for vault operations.
/// </summary>
public interface IVaultService
{
    Task<VaultDto?> GetVaultAsync(long branchId, CancellationToken cancellationToken = default);
    Task<List<VaultTransactionListItemDto>> GetRecentTransactionsAsync(long branchId, int take = 50, bool includeExpired = false, CancellationToken cancellationToken = default);
    Task<PagedResult<VaultTransactionListItemDto>> GetHistoryPageAsync(long branchId, PageRequest page,
        VaultTransactionStatus? status = null, VaultTransactionType? type = null, CancellationToken cancellationToken = default);
    Task<List<VaultTransactionListItemDto>> GetPendingTransactionsAsync(long branchId, CancellationToken cancellationToken = default);
    Task<VaultOperationResult> WithdrawForSessionAsync(long sessionId, long branchId, decimal amount, string userId, bool ensureTransaction = true, CancellationToken cancellationToken = default);
    Task<VaultOperationResult> DepositForSessionAsync(long sessionId, long branchId, decimal amount, string userId, bool ensureTransaction = true, CancellationToken cancellationToken = default);
    Task<VaultOperationResult> RequestManualAdjustmentAsync(long branchId, decimal amount, bool isDeposit, string notes, string userId, CancellationToken cancellationToken = default);
    Task<VaultOperationResult> ApproveManualAdjustmentAsync(long transactionId, string adminUserId, CancellationToken cancellationToken = default);
    Task<VaultOperationResult> ApproveManualAdjustmentByPublicIdAsync(Guid publicId, string adminUserId, CancellationToken cancellationToken = default);
    Task<VaultOperationResult> RejectManualAdjustmentAsync(long transactionId, string adminUserId, CancellationToken cancellationToken = default);
    Task<VaultOperationResult> RejectManualAdjustmentByPublicIdAsync(Guid publicId, string adminUserId, CancellationToken cancellationToken = default);
    Task<int> ExpirePendingTransactionsAsync(CancellationToken cancellationToken = default);
}

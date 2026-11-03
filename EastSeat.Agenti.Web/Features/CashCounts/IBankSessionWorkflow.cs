namespace EastSeat.Agenti.Web.Features.CashCounts;

public interface IBankSessionWorkflow
{
    Task<bool> IsModernBranchAsync(long branchId, CancellationToken cancellationToken = default);
    Task<bool> HandlesFormAsync(long branchId, CashCountFormModel form, CancellationToken cancellationToken = default);
    Task<CurrentSessionDto> GetCurrentSessionAsync(string userId, CancellationToken cancellationToken = default);
    Task<CashCountFormModel> InitializeFormAsync(string userId, bool isOpening, CancellationToken cancellationToken = default);
    Task<CashCountFormModel?> GetFormAsync(string userId, long cashCountId, CancellationToken cancellationToken = default);
    Task<CashCountSaveResult> SaveAsync(string userId, CashCountFormModel form, CancellationToken cancellationToken = default);
    Task<CashCountSaveResult> SubmitAsync(string userId, long cashCountId, CancellationToken cancellationToken = default);
    Task<CashCountSaveResult> ApproveOpeningAsync(string userId, long cashCountId, CancellationToken cancellationToken = default);
    Task<CashCountSaveResult> RejectOpeningAsync(string userId, long cashCountId, string reason, CancellationToken cancellationToken = default);
}

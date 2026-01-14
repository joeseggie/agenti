using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Web.Features.Vaults;

public class VaultDto
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public decimal CurrentBalance { get; set; }
}

public class VaultTransactionListItemDto
{
    public long Id { get; set; }
    public VaultTransactionType Type { get; set; }
    public VaultTransactionStatus Status { get; set; }
    public decimal Amount { get; set; }
    public decimal? BalanceAfter { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? ApprovedBy { get; set; }
    public string? Notes { get; set; }
    public long? CashSessionId { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

public class ManualAdjustmentFormModel
{
    public decimal Amount { get; set; }
    public bool IsDeposit { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public class VaultOperationResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public long? TransactionId { get; set; }

    public static VaultOperationResult Ok(long? transactionId = null) => new() { Success = true, TransactionId = transactionId };
    public static VaultOperationResult Error(string message) => new() { Success = false, ErrorMessage = message };
}

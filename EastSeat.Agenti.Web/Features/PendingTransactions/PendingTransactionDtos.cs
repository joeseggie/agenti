using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Web.Features.PendingTransactions;

/// <summary>
/// Lightweight wallet DTO for the pending-transaction recording form.
/// </summary>
public class PendingTransactionWalletDto
{
    public long WalletId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string WalletTypeName { get; set; } = string.Empty;
}

/// <summary>
/// Form model for recording a new pending transaction.
/// </summary>
public class PendingTransactionFormModel
{
    public long WalletId { get; set; }
    public PendingTransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public string? CustomerAccountNumber { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Form model for updating a pending transaction (adding ticket number, receipt, or resolving).
/// </summary>
public class PendingTransactionUpdateModel
{
    public string? TicketNumber { get; set; }
    public string? ReceiptPhotoPath { get; set; }
    public string? Notes { get; set; }
    public PendingTransactionStatus? NewStatus { get; set; }
    public string? ResolutionNotes { get; set; }
}

/// <summary>
/// DTO for displaying a pending transaction.
/// </summary>
public class PendingTransactionDto
{
    public long Id { get; set; }
    public long WalletId { get; set; }
    public string WalletName { get; set; } = string.Empty;
    public string WalletTypeName { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public string AgentCode { get; set; } = string.Empty;
    public PendingTransactionType Type { get; set; }
    public PendingTransactionStatus Status { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "UGX";
    public string? CustomerAccountNumber { get; set; }
    public string? TicketNumber { get; set; }
    public string? ReceiptPhotoPath { get; set; }
    public string? Notes { get; set; }
    public string? ResolutionNotes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    public string TypeDisplay => Type switch
    {
        PendingTransactionType.OutboundTransferFailed => "Outbound Transfer Failed",
        PendingTransactionType.SelfLiquidationFailed => "Self-Liquidation Failed",
        _ => Type.ToString()
    };

    public string StatusDisplay => Status switch
    {
        PendingTransactionStatus.Open => "Open",
        PendingTransactionStatus.ReportedToBank => "Reported to Bank",
        PendingTransactionStatus.Resolved => "Resolved",
        PendingTransactionStatus.Cancelled => "Cancelled",
        _ => Status.ToString()
    };

    public bool IsResolved => Status is PendingTransactionStatus.Resolved or PendingTransactionStatus.Cancelled;
}

/// <summary>
/// Result of saving / updating a pending transaction.
/// </summary>
public class PendingTransactionSaveResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public long? PendingTransactionId { get; set; }

    public static PendingTransactionSaveResult Ok(long id) => new() { Success = true, PendingTransactionId = id };

    public static PendingTransactionSaveResult Error(string message) => new() { Success = false, ErrorMessage = message };
}

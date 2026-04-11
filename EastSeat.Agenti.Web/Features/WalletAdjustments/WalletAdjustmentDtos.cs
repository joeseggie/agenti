using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Web.Features.WalletAdjustments;

/// <summary>
/// Form model for recording a wallet adjustment.
/// </summary>
public class WalletAdjustmentFormModel
{
    public long WalletId { get; set; }
    public WalletAdjustmentReason Reason { get; set; }
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// DTO for displaying a wallet adjustment.
/// </summary>
public class WalletAdjustmentDto
{
    public long Id { get; set; }
    public long WalletId { get; set; }
    public string WalletName { get; set; } = string.Empty;
    public string WalletTypeName { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public string AgentCode { get; set; } = string.Empty;
    public WalletAdjustmentReason Reason { get; set; }
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public string ReasonDisplay => Reason switch
    {
        WalletAdjustmentReason.BankShortage => "Bank Shortage",
        WalletAdjustmentReason.FakeNotes => "Fake Notes Confiscated",
        WalletAdjustmentReason.OwnerPayment => "Owner Payment Request",
        WalletAdjustmentReason.UnpaidCustomer => "Unpaid Customer",
        WalletAdjustmentReason.Other => "Other",
        _ => Reason.ToString()
    };
}

/// <summary>
/// Result of saving a wallet adjustment.
/// </summary>
public class WalletAdjustmentSaveResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public long? AdjustmentId { get; set; }

    public static WalletAdjustmentSaveResult Ok(long adjustmentId) => new()
    {
        Success = true,
        AdjustmentId = adjustmentId
    };

    public static WalletAdjustmentSaveResult Error(string message) => new()
    {
        Success = false,
        ErrorMessage = message
    };
}

/// <summary>
/// Summary of wallet adjustments per wallet in a session.
/// </summary>
public class WalletAdjustmentSummaryDto
{
    public long WalletId { get; set; }
    public string WalletName { get; set; } = string.Empty;
    public decimal TotalAdjustments { get; set; }
    public int AdjustmentCount { get; set; }
}

using EastSeat.Agenti.Shared.Domain.Enums;
using System.Text.Json.Serialization;

namespace EastSeat.Agenti.Web.Features.CustomerTransactions;

public sealed class CustomerTransactionForm
{
    [JsonRequired]
    public Guid CommandId { get; set; }
    public long CashSessionId { get; set; }
    public long BankAccountId { get; set; }
    public long CashWalletId { get; set; }
    public CustomerTransactionKind Kind { get; set; }
    public CustomerCaptureOutcome Outcome { get; set; }
    public decimal Amount { get; set; }
    public string CustomerIdentifier { get; set; } = string.Empty;
    public string? BankReference { get; set; }
    public bool ReferenceUnavailable { get; set; }
    public bool CashExchanged { get; set; } = true;
    public bool BankMoved { get; set; } = true;
    public DateTimeOffset OccurredAt { get; set; }
    public long? EvidenceFileId { get; set; }
    public string? Notes { get; set; }
    public long? CorrectionRequestId { get; set; }
}

public sealed class CustomerFollowUpForm
{
    [JsonRequired]
    public Guid CommandId { get; set; }
    public long CashSessionId { get; set; }
    public long RelatedMovementId { get; set; }
    public CustomerMovementKind Kind { get; set; }
    public CustomerCaptureOutcome Outcome { get; set; }
    public decimal Amount { get; set; }
    public bool BankMoved { get; set; } = true;
    public bool CashExchanged { get; set; }
    public bool CustomerCompletionReversed { get; set; }
    public bool BankCredit { get; set; }
    public string? SupportTicketNumber { get; set; }
    public string? BankReference { get; set; }
    public bool ReferenceUnavailable { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public long? EvidenceFileId { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public sealed class CustomerMovementCorrectionForm
{
    [JsonRequired]
    public Guid CommandId { get; set; }
    public long CorrectionRequestId { get; set; }
    public long MovementId { get; set; }
    public bool Void { get; set; }
    public CustomerTransactionKind TransactionKind { get; set; }
    public CustomerCaptureOutcome Outcome { get; set; }
    public decimal Amount { get; set; }
    public bool CashExchanged { get; set; } = true;
    public bool BankMoved { get; set; } = true;
    public string CustomerIdentifier { get; set; } = string.Empty;
    public string? BankReference { get; set; }
    public bool ReferenceUnavailable { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public long EvidenceFileId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class CustomerTransactionDto
{
    public long Id { get; set; }
    public long AgentId { get; set; }
    public long OriginatingSessionId { get; set; }
    public long BankAccountId { get; set; }
    public string BankName { get; set; } = string.Empty;
    public CustomerTransactionKind Kind { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string CustomerIdentifier { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public decimal CustomerSettledAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal CashHeld { get; set; }
    public decimal CustomerCashDue { get; set; }
    public decimal CancelledAmount { get; set; }
    public decimal BankExposure { get; set; }
    public string? SupportTicketNumber { get; set; }
    public bool IsOperationallyResolved => IsVoided ||
        (Amount == CustomerSettledAmount + RefundedAmount + CancelledAmount && CustomerCashDue == 0 && BankExposure == 0);
    public bool IsVoided { get; set; }
    public List<CustomerMovementDto> Movements { get; set; } = [];
}

public sealed record CustomerMovementDto(
    long Id, long CashSessionId, CustomerMovementKind Kind, decimal Amount,
    decimal CashEffect, decimal BankEffect, decimal CustomerSettledAmount,
    string? BankReference, bool ReferenceUnavailable, DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt, string RecordedByUserId, long? EvidenceFileId,
    long? RelatedMovementId, long? SupersedesMovementId, bool IsSuperseded, string? Notes);

public sealed record CustomerCaptureOptionsDto(
    long CashSessionId, DateTimeOffset ServerTime, List<CustomerBankOptionDto> BankAccounts,
    List<CustomerCashWalletOptionDto> CashWallets);

public sealed record CustomerBankOptionDto(long Id, string BankName, string WalletName, string Currency);
public sealed record CustomerCashWalletOptionDto(long Id, string Name, string Currency);

namespace EastSeat.Agenti.Shared.Domain.Enums;

public enum SessionWorkflowVersion
{
    Legacy = 1,
    BankReconciliation = 2
}

public enum CustomerTransactionKind
{
    Deposit,
    Withdrawal
}

public enum CustomerMovementKind
{
    Completed,
    FailedAttempt,
    BankReversal,
    Retry,
    Refund,
    CashPayment,
    PendingAttempt,
    BankCompletionConfirmed,
    BankResolutionVerified,
    Voided,
    CashReceipt,
    BankPostingObserved,
    SupportUpdate,
    Cancelled
}

public enum CustomerCaptureOutcome
{
    Completed,
    Pending,
    Failed
}

public enum FloatSettlementStage
{
    Opening,
    Closing
}

public enum StatementImportStatus
{
    Uploaded,
    Processing,
    ReadyForReview,
    Failed,
    Committed
}

public enum BankBalanceBasis
{
    Unverified,
    Ledger,
    Available,
    Float
}

public enum BankTransactionClassification
{
    Unclassified,
    Fee,
    Commission,
    BankReversal,
    OtherVerifiedBankMovement
}

public enum BankReconciliationStatus
{
    InProgress,
    NeedsCorrection,
    Reconciled,
    WithExceptions,
    NeedsReview,
    LegacyIncomplete
}

public enum BankExceptionKind
{
    BankFailure,
    PostingDelay,
    UnexplainedMovement
}

public enum ReconciliationDecisionKind
{
    Matched,
    Unmatched,
    Classified,
    Approved,
    ReturnedForCorrection,
    ExceptionRecorded,
    ExceptionResolved,
    ReviewInvalidated,
    ImportCorrected
}

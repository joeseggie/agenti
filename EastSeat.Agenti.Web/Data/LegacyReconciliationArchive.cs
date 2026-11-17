using EastSeat.Agenti.Shared.Domain.Entities;

namespace EastSeat.Agenti.Web.Data;

// These types describe retained historical storage, not an executable reconciliation workflow.
internal static class LegacyReconciliationArchive
{
    internal const string Schema = "reconciliation_archive";

    internal static readonly (Type Type, string Table)[] Tables =
    [
        (typeof(BankStatementImport), "BankStatementImports"),
        (typeof(BankStatementRow), "BankStatementRows"),
        (typeof(BankTransaction), "BankTransactions"),
        (typeof(BankStatementOccurrence), "BankStatementOccurrences"),
        (typeof(BankReconciliationReview), "BankReconciliationReviews"),
        (typeof(BankReconciliationResult), "BankReconciliationResults"),
        (typeof(BankReconciliationMatch), "BankReconciliationMatches"),
        (typeof(BankReconciliationException), "BankReconciliationExceptions"),
        (typeof(BankReconciliationDecision), "BankReconciliationDecisions"),
        (typeof(CashCountCorrectionRequest), "CashCountCorrectionRequests")
    ];
}

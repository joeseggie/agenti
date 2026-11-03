using System.Globalization;
using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Services;

namespace EastSeat.Agenti.Web.Features.BankStatements;

internal static class StatementValidation
{
    internal static DateTimeOffset LedgerTime(BankStatementRow row) => row.PostedAt ?? row.OccurredAt;

    internal static List<BankStatementRow> EffectiveRows(IEnumerable<BankStatementRow> rows)
    {
        var all = rows.ToList();
        var superseded = all.Where(r => r.SupersedesRowId.HasValue).Select(r => r.SupersedesRowId!.Value).ToHashSet();
        return all.Where(r => !superseded.Contains(r.Id)).OrderBy(r => r.Sequence).ToList();
    }

    internal static List<string> Check(
        BankStatementImport import, IReadOnlyList<BankStatementRow> rows, string accountIdentifier, string currency,
        EastSeat.Agenti.Shared.Domain.Enums.BankBalanceBasis basis)
    {
        var errors = new List<string>();
        if (import.ReportedAccountIdentifier != accountIdentifier)
            errors.Add("The statement account identity does not match the selected bank account.");
        if (import.Currency != currency || import.BalanceBasis != basis)
            errors.Add("The statement currency or balance basis does not match the configured account.");
        if (!import.CoverageDeclaredComplete)
            errors.Add("Complete statement coverage has not been established by the bank profile.");
        if (!import.CoverageFrom.HasValue || !import.CoverageTo.HasValue || import.CoverageFrom >= import.CoverageTo ||
            !import.OpeningBalance.HasValue || !import.ClosingBalance.HasValue)
        {
            errors.Add("A valid statement interval and opening/closing balances are required.");
            return errors;
        }
        var balance = import.OpeningBalance.Value;
        DateTimeOffset? previousTime = null;
        foreach (var row in rows)
        {
            var time = LedgerTime(row);
            if (row.Currency != currency)
                errors.Add($"Row {row.Sequence} uses a different currency.");
            if (time < import.CoverageFrom || time >= import.CoverageTo)
                errors.Add($"Row {row.Sequence} falls outside the declared statement coverage.");
            if (previousTime.HasValue && time < previousTime.Value)
                errors.Add($"Row {row.Sequence} is out of ledger order.");
            previousTime = time;
            balance += row.SignedAmount;
            if (balance != row.RunningBalance)
                errors.Add($"The running balance does not reconcile at row {row.Sequence}.");
        }
        if (balance != import.ClosingBalance)
            errors.Add("The statement opening balance plus its movements does not equal its closing balance.");
        return errors.Distinct().ToList();
    }

    internal static void ValidateEntry(ParsedStatementEntry row)
    {
        if (row.OccurredAt == default || string.IsNullOrWhiteSpace(row.Currency) || row.Currency.Length != 3 ||
            string.IsNullOrWhiteSpace(row.SourceLocation) || row.SourceLocation.Length > 256 ||
            row.TransactionNumber?.Length > 256 || row.Reference?.Length > 256 || row.CustomerIdentifier?.Length > 256 ||
            row.Narration.Length > 4000 || row.RawData.Length > 16_384 ||
            Math.Abs(row.SignedAmount) > 9_999_999_999_999_999.99m ||
            Math.Abs(row.RunningBalance) > 9_999_999_999_999_999.99m ||
            decimal.Round(row.SignedAmount, 2) != row.SignedAmount || decimal.Round(row.RunningBalance, 2) != row.RunningBalance)
            throw new StatementParsingException("A statement row has missing, oversized, or invalid financial fields.");
    }

    internal static ParsedStatementEntry ToParsed(BankStatementRow row) => new(
        row.SourceLocation, row.RawData, row.TransactionNumber, row.Reference, row.CustomerIdentifier,
        row.Narration, row.Currency, row.SignedAmount, row.RunningBalance, row.OccurredAt, row.PostedAt,
        row.ValueDate, row.HasPreciseTimestamp);

    internal static string Fingerprint(BankStatementRow row) => BankingRequestIdentity.Hash(new
    {
        LedgerAt = LedgerTime(row).ToUniversalTime(), row.Currency,
        SignedAmount = row.SignedAmount.ToString("F2", CultureInfo.InvariantCulture),
        RunningBalance = row.RunningBalance.ToString("F2", CultureInfo.InvariantCulture)
    });

    internal static bool SameFinancialFacts(BankStatementRow left, BankStatementRow right) =>
        left.Currency == right.Currency && left.SignedAmount == right.SignedAmount &&
        left.RunningBalance == right.RunningBalance && LedgerTime(left) == LedgerTime(right);

    internal static bool SameFacts(BankStatementRow left, BankStatementRow right) =>
        SameFinancialFacts(left, right) && left.OccurredAt == right.OccurredAt && left.PostedAt == right.PostedAt &&
        left.ValueDate == right.ValueDate && left.TransactionNumber == right.TransactionNumber &&
        left.Reference == right.Reference && left.CustomerIdentifier == right.CustomerIdentifier &&
        left.Narration == right.Narration && left.HasPreciseTimestamp == right.HasPreciseTimestamp;
}

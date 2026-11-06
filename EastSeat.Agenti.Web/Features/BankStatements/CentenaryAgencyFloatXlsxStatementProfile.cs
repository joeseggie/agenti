using System.Globalization;
using System.Text.RegularExpressions;
using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Web.Features.BankStatements;

/// <summary>
/// Centenary Bank "Account Transaction Report" XLSX export for an agency float account. One sample has been
/// analysed. The profile stays unverified until the release gate in
/// docs/centenary-agency-float-statement-profile.md is met. Rows carry dates without times, so every entry is
/// imported with <see cref="ParsedStatementEntry.HasPreciseTimestamp"/> set to false.
/// </summary>
public sealed partial class CentenaryAgencyFloatXlsxStatementProfile : IStatementImportProfile
{
    public const string ProfileKey = "centenary-agency-float-xlsx";
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const int LastColumn = 12;
    private const string Unedited = "Upload the original bank export unedited.";

    private static readonly string[] TableHeaders =
    [
        "Transaction Date", "Value Date", "Branch", "User", "S/N", "Debit / Credit (D/C)", "Amount",
        "Cumulative Balance", "Transaction", "Justification", "Description", "Comments"
    ];

    public string Key => ProfileKey;
    public string BankIdentifier => "CENTENARY";
    public string DisplayName => "Centenary Bank agency float (XLSX)";
    public int Version => 1;
    public bool IsVerified => false;
    public BankBalanceBasis BalanceBasis => BankBalanceBasis.Ledger;
    public IReadOnlyList<string> SupportedContentTypes => [XlsxContentType];

    // The S/N and Transaction columns have not been proven unique across exports, so duplicates are detected
    // through fingerprints and reviewed overlap decisions instead.
    public string? GetReliableIdentity(ParsedStatementEntry entry) => null;

    public async Task<ParsedStatement> ParseAsync(
        Stream source, string contentType, StatementAccountContext account, CancellationToken cancellationToken)
    {
        if (!string.Equals(contentType, XlsxContentType, StringComparison.Ordinal))
            throw new StatementParsingException("The Centenary agency float profile reads only the bank's XLSX export.");
        var sheet = await XlsxWorksheetReader.ReadSingleWorksheetAsync(source, cancellationToken);
        var parsed = Parse(sheet, Zone(account.TimeZoneId), cancellationToken);
        if (!string.Equals(parsed.Currency, account.Currency, StringComparison.Ordinal))
            throw new StatementParsingException("The statement currency does not match the configured bank account.");
        return parsed;
    }

    private static ParsedStatement Parse(XlsxWorksheet sheet, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        var lastColumnByRow = sheet.Cells.Where(c => !IsBlank(c)).GroupBy(c => c.Row)
            .ToDictionary(g => g.Key, g => g.Max(c => c.Column));
        var tableRow = FindTableHeader(sheet, lastColumnByRow);
        var header = ReadHeaderValues(sheet, tableRow);
        var accountMatch = AccountPattern().Match(HeaderText(header, "Account Number"));
        if (!accountMatch.Success)
            throw new StatementParsingException("The Account Number header must contain the account number followed by its currency code.");
        var accountNumber = accountMatch.Groups["account"].Value;
        var currency = accountMatch.Groups["currency"].Value;
        var from = ParseDate(HeaderText(header, "Date From"), "Date From");
        var to = ParseDate(HeaderText(header, "Date To"), "Date To");
        if (to < from)
            throw new StatementParsingException("The statement's Date To is earlier than its Date From.");
        if (to == DateOnly.MaxValue)
            throw new StatementParsingException("The statement's Date To is outside the supported date range.");
        var opening = ParseAmount(HeaderText(header, "Opening Balance"), "Opening Balance", allowNegative: true);
        var totalCredits = ParseAmount(HeaderText(header, "Total Credits"), "Total Credits", allowNegative: false);
        var totalDebits = ParseAmount(HeaderText(header, "Total Debits"), "Total Debits", allowNegative: false);

        var entries = new List<ParsedStatementEntry>();
        decimal credits = 0, debits = 0;
        var row = tableRow + 1;
        for (; lastColumnByRow.TryGetValue(row, out var lastColumn); row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (lastColumn > LastColumn)
                throw new StatementParsingException($"Row {row} has values outside the expected statement columns. {Unedited}");
            var entry = ReadEntry(sheet, row, currency, zone);
            if (entry.SignedAmount >= 0)
                credits += entry.SignedAmount;
            else
                debits -= entry.SignedAmount;
            entries.Add(entry);
        }
        var trailingRow = lastColumnByRow.Keys.Where(r => r > row).DefaultIfEmpty().Min();
        if (trailingRow > 0)
            throw new StatementParsingException($"Unexpected content was found in row {trailingRow}, after the transaction table. {Unedited}");
        if (credits != totalCredits || debits != totalDebits)
            throw new StatementParsingException(
                "The transaction rows do not add up to the statement's Total Credits and Total Debits. " +
                "The export may be incomplete or edited; export the full period again from the bank.");

        var runningBalance = opening;
        var completeCoverage = true;
        foreach (var entry in entries)
        {
            runningBalance += entry.SignedAmount;
            var ledgerDate = DateOnly.FromDateTime((entry.PostedAt ?? entry.OccurredAt).DateTime);
            if (runningBalance != entry.RunningBalance || ledgerDate < from || ledgerDate > to)
                completeCoverage = false;
        }

        return new ParsedStatement(
            accountNumber, currency, BankBalanceBasis.Ledger, LocalMidnight(from, zone), LocalMidnight(to.AddDays(1), zone),
            opening, entries.Count == 0 ? opening : entries[^1].RunningBalance, completeCoverage, entries);
    }

    private static ParsedStatementEntry ReadEntry(XlsxWorksheet sheet, int row, string currency, TimeZoneInfo zone)
    {
        var values = Enumerable.Range(1, LastColumn).Select(column => sheet.Cell(row, column)?.Value ?? string.Empty).ToArray();
        var date = ParseDate(RequiredText(sheet, row, 1, "Transaction Date"), $"row {row} Transaction Date");
        var valueDateText = OptionalText(sheet, row, 2, "Value Date");
        DateOnly? valueDate = valueDateText == null ? null : ParseDate(valueDateText, $"row {row} Value Date");
        var direction = RequiredText(sheet, row, 6, "Debit / Credit").ToUpperInvariant();
        var amount = ParseAmount(RequiredText(sheet, row, 7, "Amount"), $"row {row} Amount", allowNegative: false);
        var balance = ParseAmount(RequiredText(sheet, row, 8, "Cumulative Balance"), $"row {row} Cumulative Balance", allowNegative: true);
        var signedAmount = direction switch
        {
            "D" => -amount,
            "C" => amount,
            _ => throw new StatementParsingException($"Row {row} has an unknown Debit / Credit indicator.")
        };
        var comments = Clean(values[11]);
        var narration = string.Join(" ", new[] { Clean(values[10]), comments }.Where(v => v.Length > 0));
        var rawData = string.Join(" | ", values);
        if (narration.Length > 4_000 || rawData.Length > 16_384)
            throw new StatementParsingException($"Row {row} is too large to import. {Unedited}");
        var transactionNumber = NullIfEmpty(values[8]) ?? NullIfEmpty(values[4]);
        return new ParsedStatementEntry(
            $"{sheet.Name}!A{row}:L{row}", rawData, transactionNumber, ExtractReference(comments), CustomerIdentifier: null,
            narration, currency, signedAmount, balance, LocalMidnight(date, zone), PostedAt: null, valueDate,
            HasPreciseTimestamp: false);
    }

    // Comments mix customer details with bank references. Only two reference shapes seen in the sample are
    // extracted: an AGENCY-<digits> token and a comment that is entirely <digits>-<digits>-<code>.
    private static string? ExtractReference(string comments)
    {
        var agency = comments.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(token => AgencyReferencePattern().IsMatch(token));
        if (agency != null)
            return agency;
        return TransferReferencePattern().IsMatch(comments) ? comments : null;
    }

    private static int FindTableHeader(XlsxWorksheet sheet, Dictionary<int, int> lastColumnByRow)
    {
        var candidates = sheet.Cells
            .Where(c => c.Column == 1 && c.Kind == XlsxCellKind.Text &&
                string.Equals(Clean(c.Value), TableHeaders[0], StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Row).ToList();
        if (candidates.Count != 1)
            throw new StatementParsingException("The Centenary transaction table header was not found. " + Unedited);
        var row = candidates[0];
        for (var column = 1; column <= LastColumn; column++)
        {
            var cell = sheet.Cell(row, column);
            if (cell == null || !string.Equals(Clean(cell.Value), TableHeaders[column - 1], StringComparison.OrdinalIgnoreCase))
                throw new StatementParsingException(
                    $"The Centenary transaction table must have the columns {string.Join(", ", TableHeaders)}. {Unedited}");
        }
        if (lastColumnByRow[row] > LastColumn)
            throw new StatementParsingException("The Centenary transaction table has unexpected extra columns. " + Unedited);
        return row;
    }

    private static Dictionary<string, XlsxCell?> ReadHeaderValues(XlsxWorksheet sheet, int tableRow)
    {
        var labels = new Dictionary<string, XlsxCell?>(StringComparer.OrdinalIgnoreCase);
        var repeated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var label in sheet.Cells.Where(c => c.Column == 1 && c.Row < tableRow && c.Kind == XlsxCellKind.Text))
        {
            var name = Clean(label.Value).TrimEnd(':', ' ');
            if (!labels.TryAdd(name, sheet.Cell(label.Row, 2)))
                repeated.Add(name);
        }
        foreach (var name in repeated)
            labels[name] = null;
        return labels;
    }

    private static string HeaderText(Dictionary<string, XlsxCell?> header, string label)
    {
        if (!header.TryGetValue(label, out var cell) || cell == null || IsBlank(cell))
            throw new StatementParsingException($"The statement header must contain exactly one {label} value. {Unedited}");
        return cell.Kind == XlsxCellKind.Text
            ? cell.Value.Trim()
            : throw new StatementParsingException($"The {label} header value is not the bank's text value. {Unedited}");
    }

    private static string RequiredText(XlsxWorksheet sheet, int row, int column, string label) =>
        OptionalText(sheet, row, column, label)
        ?? throw new StatementParsingException($"Row {row} is missing its {label}.");

    private static string? OptionalText(XlsxWorksheet sheet, int row, int column, string label)
    {
        var cell = sheet.Cell(row, column);
        if (cell == null || IsBlank(cell))
            return null;
        return cell.Kind == XlsxCellKind.Text
            ? cell.Value.Trim()
            : throw new StatementParsingException($"Row {row} stores its {label} as a spreadsheet number or value instead of the bank's text. {Unedited}");
    }

    private static DateOnly ParseDate(string text, string label) =>
        DateOnly.TryParseExact(text, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new StatementParsingException($"The {label} value must be a dd/MM/yyyy date.");

    private static decimal ParseAmount(string text, string label, bool allowNegative)
    {
        var match = AmountPattern().Match(text);
        if (!match.Success || (!allowNegative && match.Groups["sign"].Success))
            throw new StatementParsingException($"The {label} value must be a bank amount such as 1,234.00.");
        return decimal.Parse(text.Replace(",", string.Empty, StringComparison.Ordinal),
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    }

    private static TimeZoneInfo Zone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new StatementParsingException("The account's statement time zone is not recognised.");
        }
    }

    private static DateTimeOffset LocalMidnight(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local))
            throw new StatementParsingException("The statement time zone has an ambiguous or missing local midnight on a statement date.");
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static bool IsBlank(XlsxCell cell) => string.IsNullOrWhiteSpace(cell.Value);
    private static string Clean(string value) => WhitespacePattern().Replace(value, " ").Trim();
    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"^(?<account>\d{6,20})\s+(?<currency>[A-Z]{3})$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AccountPattern();

    [GeneratedRegex(@"^(?<sign>-)?(?:0|[1-9]\d{0,2}(?:,\d{3}){0,5})\.\d{2}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AmountPattern();

    [GeneratedRegex(@"^AGENCY-\d{6,30}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AgencyReferencePattern();

    [GeneratedRegex(@"^\d{3,20}-\d{3,20}-[A-Z]{2,6}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TransferReferencePattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WhitespacePattern();
}

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Web.Features.BankStatements;

public sealed class UniversalBankCsvStatementProfile : IStatementImportProfile
{
    public const string ProfileKey = "agenti-universal-csv";
    public const string CsvContentType = "text/csv";
    private readonly UniversalBankCsvSchema _schema = UniversalBankCsvSchema.Load();

    public string Key => ProfileKey;
    public string BankIdentifier => "*";
    public string DisplayName => "Agenti universal bank CSV";
    public int Version => _schema.Version;
    public bool IsVerified => true;
    public BankBalanceBasis BalanceBasis => BankBalanceBasis.Ledger;
    public IReadOnlyList<string> SupportedContentTypes => [CsvContentType];
    public string SchemaHash => _schema.Hash;

    public string? GetReliableIdentity(ParsedStatementEntry entry) => null;

    public async Task<ParsedStatement> ParseAsync(
        Stream source, string contentType, StatementAccountContext account, CancellationToken cancellationToken)
    {
        if (!string.Equals(contentType, CsvContentType, StringComparison.Ordinal))
            throw new StatementParsingException("The Agenti universal profile accepts CSV files only.");
        if (!account.CoverageFromDate.HasValue || !account.CoverageToDate.HasValue ||
            account.CoverageFromDate > account.CoverageToDate)
            throw new StatementParsingException("Select a valid statement start and end date.");

        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(account.TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new StatementParsingException("The configured bank account time zone is unavailable.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new StatementParsingException("The configured bank account time zone is invalid.");
        }

        using var reader = new StreamReader(source, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var text = await reader.ReadToEndAsync(cancellationToken);
        var records = CsvRecords.Read(text);
        if (records.Count == 0)
            throw new StatementParsingException("The CSV is empty. The first row must contain the exact Agenti template headers.");

        var errors = new List<string>();
        if (records[0].Fields.Count != _schema.Headers.Count ||
            !records[0].Fields.SequenceEqual(_schema.Headers, StringComparer.Ordinal))
        {
            errors.Add("Row 1 headers must be exactly, and in order: " + string.Join(", ", _schema.Headers) + ".");
            throw new StatementParsingException(string.Join(" ", errors));
        }

        var entries = new List<ParsedStatementEntry>();
        DateOnly? priorDate = null;
        DateTimeOffset? priorPreciseAt = null;
        decimal? opening = null;
        decimal? priorBalance = null;
        for (var index = 1; index < records.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = records[index];
            var rowNumber = index + 1;
            if (record.Fields.Count != _schema.Headers.Count)
            {
                errors.Add($"Row {rowNumber}: expected {_schema.Headers.Count} columns but found {record.Fields.Count}.");
                continue;
            }

            var fields = record.Fields;
            if (!TryDate(fields[_schema.Column("TransactionDate")], _schema.Definition("TransactionDate").Format!, out var date))
                errors.Add($"Row {rowNumber}, TransactionDate: use yyyy-MM-dd.");
            if (!string.IsNullOrEmpty(fields[_schema.Column("TransactionTime")]) &&
                !TryTime(fields[_schema.Column("TransactionTime")], _schema.Definition("TransactionTime").Format!, out _))
                errors.Add($"Row {rowNumber}, TransactionTime: use HH:mm:ss or leave blank.");
            if (string.IsNullOrWhiteSpace(fields[_schema.Column("Description")]))
                errors.Add($"Row {rowNumber}, Description: a description is required.");
            var debitIndex = _schema.Column("Debit");
            var creditIndex = _schema.Column("Credit");
            var debitSpec = _schema.Definition("Debit");
            var creditSpec = _schema.Definition("Credit");
            var balanceSpec = _schema.Definition("RunningBalance");
            var debitOk = TryOptionalAmount(fields[debitIndex], debitSpec, out var debit);
            var creditOk = TryOptionalAmount(fields[creditIndex], creditSpec, out var credit);
            var balanceOk = TryBalance(fields[_schema.Column("RunningBalance")], balanceSpec, out var runningBalance);
            if (!debitOk) errors.Add($"Row {rowNumber}, Debit: enter a positive amount with exactly two decimal places or leave blank.");
            if (!creditOk) errors.Add($"Row {rowNumber}, Credit: enter a positive amount with exactly two decimal places or leave blank.");
            if (!balanceOk) errors.Add($"Row {rowNumber}, RunningBalance: enter an amount with exactly two decimal places.");
            var dateIndex = _schema.Column("TransactionDate");
            var timeIndex = _schema.Column("TransactionTime");
            var descriptionIndex = _schema.Column("Description");
            var referenceIndex = _schema.Column("Reference");
            if (!debitOk || !creditOk || !balanceOk ||
                !TryDate(fields[dateIndex], _schema.Definition("TransactionDate").Format!, out date) ||
                (!string.IsNullOrEmpty(fields[timeIndex]) &&
                 !TryTime(fields[timeIndex], _schema.Definition("TransactionTime").Format!, out _)) ||
                string.IsNullOrWhiteSpace(fields[descriptionIndex]))
                continue;

            if (date < account.CoverageFromDate || date > account.CoverageToDate)
            {
                errors.Add($"Row {rowNumber}, TransactionDate: date is outside the selected period.");
                continue;
            }
            if ((fields[debitIndex].Length == 0) == (fields[creditIndex].Length == 0))
            {
                errors.Add($"Row {rowNumber}, Debit/Credit: exactly one positive amount is required.");
                continue;
            }

            var signedAmount = fields[debitIndex].Length == 0 ? credit : -debit;
            var timeText = fields[timeIndex];
            var hasPreciseTime = timeText.Length > 0;
            var time = hasPreciseTime && TryTime(timeText, _schema.Definition("TransactionTime").Format!, out var parsedTime)
                ? parsedTime : TimeOnly.MinValue;
            if (!TryLocalInstant(date, time, zone, out var occurredAt))
            {
                errors.Add($"Row {rowNumber}, TransactionTime: the local date/time is invalid or ambiguous in {account.TimeZoneId}.");
                continue;
            }
            if (priorDate.HasValue && date < priorDate.Value ||
                hasPreciseTime && priorPreciseAt.HasValue && occurredAt < priorPreciseAt.Value)
            {
                errors.Add($"Row {rowNumber}: transactions must be ordered by date and time.");
                continue;
            }

            priorDate = date;
            if (hasPreciseTime)
                priorPreciseAt = occurredAt;
            opening ??= runningBalance - signedAmount;
            if (priorBalance.HasValue && priorBalance.Value + signedAmount != runningBalance)
                errors.Add($"Row {rowNumber}, RunningBalance: does not equal the prior balance plus this movement.");
            priorBalance = runningBalance;
            entries.Add(new ParsedStatementEntry(
                $"row {rowNumber}", string.Join(",", fields), null, NullIfBlank(fields[referenceIndex]), null,
                fields[descriptionIndex],
                account.Currency, signedAmount, runningBalance, occurredAt, null, date, hasPreciseTime));
        }

        if (errors.Count > 0)
            throw new StatementParsingException(string.Join(Environment.NewLine, errors));

        var firstDateHasTransactions = entries.Any(entry => entry.ValueDate == account.CoverageFromDate);
        if (!firstDateHasTransactions && !account.OpeningBaseline.HasValue)
            throw new StatementParsingException("This first empty statement period requires the audited opening baseline.");
        var openingBalance = opening ?? account.OpeningBaseline
            ?? throw new StatementParsingException("This first empty statement period requires the audited opening baseline.");
        if (opening.HasValue && account.OpeningBaseline.HasValue && opening.Value != account.OpeningBaseline.Value)
            throw new StatementParsingException("The first running-balance-derived opening does not match the supplied continuity balance.");
        var closingBalance = priorBalance ?? openingBalance;
        var from = AtLocalMidnight(account.CoverageFromDate.Value, zone);
        if (account.CoverageToDate.Value == DateOnly.MaxValue)
            throw new StatementParsingException("The selected end date exceeds the supported date range.");
        var to = AtLocalMidnight(account.CoverageToDate.Value.AddDays(1), zone);
        return new ParsedStatement(account.AccountIdentifier, account.Currency, account.BalanceBasis,
            from, to, openingBalance, closingBalance, true, entries);
    }

    private static bool TryDate(string value, string format, out DateOnly date) =>
        DateOnly.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static bool TryTime(string value, string format, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private static bool TryOptionalAmount(
        string value, UniversalBankCsvSchema.ColumnDefinition definition, out decimal amount)
    {
        amount = 0;
        return value.Length == 0 || TryAmount(value, definition, out amount);
    }

    private static bool TryAmount(string value, UniversalBankCsvSchema.ColumnDefinition definition, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace) ||
            !Regex.IsMatch(value, definition.AllowThousandsSeparator
                ? @"^(?:\d+|\d{1,3}(?:,\d{3})+)\.\d{2}$"
                : @"^\d+\.\d{2}$", RegexOptions.CultureInvariant) ||
            !decimal.TryParse(value, NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out amount) || amount <= 0 ||
            decimal.Round(amount, definition.Scale ?? 2) != amount)
            return false;
        return true;
    }

    private static bool TryBalance(
        string value, UniversalBankCsvSchema.ColumnDefinition definition, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace) ||
            !Regex.IsMatch(value, definition.AllowThousandsSeparator
                ? @"^-?(?:\d+|\d{1,3}(?:,\d{3})+)\.\d{2}$"
                : @"^-?\d+\.\d{2}$", RegexOptions.CultureInvariant) ||
            !decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowThousands |
                NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount) ||
            decimal.Round(amount, definition.Scale ?? 2) != amount)
            return false;
        return true;
    }

    private static bool TryLocalInstant(DateOnly date, TimeOnly time, TimeZoneInfo zone, out DateTimeOffset instant)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local))
        {
            instant = default;
            return false;
        }
        instant = new DateTimeOffset(local, zone.GetUtcOffset(local));
        return true;
    }

    private static DateTimeOffset AtLocalMidnight(DateOnly date, TimeZoneInfo zone)
    {
        if (!TryLocalInstant(date, TimeOnly.MinValue, zone, out var instant))
            throw new StatementParsingException($"Midnight is invalid in {zone.Id} for {date:yyyy-MM-dd}.");
        return instant;
    }

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed record CsvRecord(List<string> Fields);

    private static class CsvRecords
    {
        public static List<CsvRecord> Read(string text)
        {
            var result = new List<CsvRecord>();
            var fields = new List<string>();
            var field = new StringBuilder();
            var quoted = false;
            var afterQuote = false;
            var sawAny = false;
            var line = 1;
            var rowStart = 1;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                sawAny = true;
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                            afterQuote = true;
                        }
                    }
                    else
                    {
                        field.Append(c);
                        if (c == '\n') line++;
                    }
                    continue;
                }
                if (afterQuote && c is not ',' and not '\r' and not '\n')
                    throw new StatementParsingException($"Row {rowStart}: unexpected character after a quoted CSV field.");
                if (c == '"' && field.Length == 0 && !afterQuote)
                {
                    quoted = true;
                    continue;
                }
                if (c == ',')
                {
                    fields.Add(field.ToString());
                    field.Clear();
                    afterQuote = false;
                    continue;
                }
                if (c is '\r' or '\n')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    fields.Add(field.ToString());
                    field.Clear();
                    result.Add(new CsvRecord(fields));
                    fields = [];
                    afterQuote = false;
                    line++;
                    rowStart = line;
                    continue;
                }
                field.Append(c);
            }
            if (quoted)
                throw new StatementParsingException($"Row {rowStart}: a quoted CSV field is not closed.");
            if (sawAny && (field.Length > 0 || fields.Count > 0 || afterQuote))
            {
                fields.Add(field.ToString());
                result.Add(new CsvRecord(fields));
            }
            return result;
        }
    }
}

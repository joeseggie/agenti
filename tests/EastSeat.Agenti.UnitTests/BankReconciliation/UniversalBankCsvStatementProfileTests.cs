using System.Text;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Web.Features.BankStatements;

namespace EastSeat.Agenti.UnitTests.BankReconciliation;

public class UniversalBankCsvStatementProfileTests
{
    private static readonly DateOnly From = new(2026, 11, 1);
    private static readonly DateOnly To = new(2026, 11, 2);

    [Fact]
    public async Task Parse_DerivesLedgerBalancesAndPreservesDateOnlyRows()
    {
        var parsed = await ParseAsync(
            "TransactionDate,TransactionTime,Reference,Description,Debit,Credit,RunningBalance\r\n" +
            "2026-11-01,,REF-1,\"Cash deposit, teller\",,1000.00,11000.00\r\n" +
            "2026-11-02,09:15:00,REF-2,Withdrawal,250.00,,10750.00",
            "Africa/Kampala", null);

        Assert.Equal(10_000m, parsed.OpeningBalance);
        Assert.Equal(10_750m, parsed.ClosingBalance);
        Assert.Equal(2, parsed.Entries.Count);
        Assert.Equal(1_000m, parsed.Entries[0].SignedAmount);
        Assert.False(parsed.Entries[0].HasPreciseTimestamp);
        Assert.True(parsed.Entries[1].HasPreciseTimestamp);
        Assert.Equal("Cash deposit, teller", parsed.Entries[0].Narration);
        Assert.Equal(new DateOnly(2026, 11, 1), parsed.Entries[0].ValueDate);
    }

    [Fact]
    public async Task Parse_PreservesSelectedDatesAndConfirmedBalanceBasisAcrossTimeZones()
    {
        var parsed = await ParseAsync(
            "TransactionDate,TransactionTime,Reference,Description,Debit,Credit,RunningBalance\n" +
            "2026-11-01,,REF-1,Deposit,,10.00,110.00",
            "America/New_York", null, BankBalanceBasis.Available);

        Assert.Equal(BankBalanceBasis.Available, parsed.BalanceBasis);
        Assert.Equal(From, parsed.Entries.Single().ValueDate);
        Assert.Equal(From, DateOnly.FromDateTime(parsed.CoverageFrom.ToOffset(TimeSpan.FromHours(-4)).DateTime));
        Assert.Equal(To.AddDays(1), DateOnly.FromDateTime(parsed.CoverageTo.ToOffset(TimeSpan.FromHours(-5)).DateTime));
    }

    [Fact]
    public async Task Parse_AllowsUntimedRowsAfterTimedRowsWhenDatesAreOrdered()
    {
        var parsed = await ParseAsync(
            "TransactionDate,TransactionTime,Reference,Description,Debit,Credit,RunningBalance\n" +
            "2026-11-01,09:15:00,REF-1,Deposit,,10.00,110.00\n" +
            "2026-11-01,,REF-2,Fee,1.00,,109.00",
            "Africa/Kampala", null);

        Assert.Equal(2, parsed.Entries.Count);
        Assert.True(parsed.Entries[0].HasPreciseTimestamp);
        Assert.False(parsed.Entries[1].HasPreciseTimestamp);
    }

    [Fact]
    public async Task Parse_PreservesCrLfAndEscapedQuotesAcrossFormerBufferBoundaries()
    {
        const string header = "TransactionDate,TransactionTime,Reference,Description,Debit,Credit,RunningBalance\r\n";
        const string rowPrefix = "2026-11-01,,REF,\"";
        const string rowSuffix = "\",,1.00,11.00";
        var quotePrefix = header + rowPrefix + new string('x', 4095 - header.Length - rowPrefix.Length);
        var escapedQuoteCsv = quotePrefix + "\"\"x" + rowSuffix;
        const string crLfRowPrefix = "2026-11-01,,REF,\"";
        var crLfRow = "2026-11-01,,REF,\"" +
            new string('x', 4095 - header.Length - crLfRowPrefix.Length - rowSuffix.Length) + rowSuffix;
        var crLfCsv = header + crLfRow + "\r\n";

        var escaped = await ParseAsync(escapedQuoteCsv, "Africa/Kampala", null);
        var splitCrLf = await ParseAsync(crLfCsv, "Africa/Kampala", null);

        Assert.EndsWith("x\"x", escaped.Entries.Single().Narration);
        Assert.Single(splitCrLf.Entries);
    }

    [Fact]
    public async Task Parse_QuotedThousandsAmountsNeedValidCsvQuotingAndKeepRowsChained()
    {
        var csv =
            "TransactionDate,TransactionTime,Reference,Description,Debit,Credit,RunningBalance\n" +
            "2026-11-01,,,Opening move,,\"1,000.00\",\"11,000.00\"\n";

        var parsed = await ParseAsync(csv, "Africa/Kampala", null);

        Assert.Equal(10_000m, parsed.OpeningBalance);
        Assert.Equal(11_000m, parsed.ClosingBalance);
    }

    [Fact]
    public async Task Parse_EmptyPeriodRequiresAndUsesBaseline()
    {
        var csv = "TransactionDate,TransactionTime,Reference,Description,Debit,Credit,RunningBalance\n";

        var missing = await Assert.ThrowsAsync<StatementParsingException>(() =>
            ParseAsync(csv, "Africa/Kampala", null));
        Assert.Contains("audited opening baseline", missing.Message);

        var parsed = await ParseAsync(csv, "Africa/Kampala", 12_345m);

        Assert.Empty(parsed.Entries);
        Assert.Equal(12_345m, parsed.OpeningBalance);
        Assert.Equal(12_345m, parsed.ClosingBalance);
    }

    [Fact]
    public async Task Parse_FirstDateWithoutRowsRequiresBaselineEvenWhenLaterDatesHaveTransactions()
    {
        var csv =
            "TransactionDate,TransactionTime,Reference,Description,Debit,Credit,RunningBalance\n" +
            "2026-11-02,,REF-2,Deposit,,100.00,10100.00\n";

        var missing = await Assert.ThrowsAsync<StatementParsingException>(() =>
            ParseAsync(csv, "Africa/Kampala", null));
        Assert.Contains("first empty statement period", missing.Message);

        var parsed = await ParseAsync(csv, "Africa/Kampala", 10_000m);
        Assert.Equal(10_000m, parsed.OpeningBalance);
        Assert.Equal(10_100m, parsed.ClosingBalance);
    }

    [Theory]
    [InlineData("TransactionDate,TransactionTime,Reference,Description,Credit,Debit,RunningBalance\n", "headers")]
    [InlineData("TransactionDate,TransactionTime,Reference,Description,Debit,Credit,RunningBalance\n2026-11-01,,,Fee,1.0,,10.00\n", "Debit")]
    [InlineData("TransactionDate,TransactionTime,Reference,Description,Debit,Credit,RunningBalance\n2026-11-01,,,Fee,,,10.00\n", "Debit/Credit")]
    [InlineData("TransactionDate,TransactionTime,Reference,Description,Debit,Credit,RunningBalance\n2026-11-01,,,Fee,,1.00,11.00\n2026-11-01,,,Fee,1.00,,9.99\n", "RunningBalance")]
    [InlineData("TransactionDate,TransactionTime,Reference,Description,Debit,Credit,RunningBalance\n2026-11-03,,,Fee,,1.00,10.00\n", "outside the selected period")]
    [InlineData("TransactionDate,TransactionTime,Reference,Description,Debit,Credit,RunningBalance\n2026-11-01,not-time,,Fee,,1.00,10.00\n", "TransactionTime")]
    public async Task Parse_RejectsInvalidSchemaOrRows(string csv, string expectedMessage)
    {
        var error = await Assert.ThrowsAsync<StatementParsingException>(() =>
            ParseAsync(csv, "Africa/Kampala", null));

        Assert.Contains(expectedMessage, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Parse_ReportsMultipleRowAndColumnErrorsTogether()
    {
        var csv =
            "TransactionDate,TransactionTime,Reference,Description,Debit,Credit,RunningBalance\n" +
            "not-date,,REF,,x,,x\n" +
            "2026-11-01,,REF,Fee,1.00,2.00,10.00";

        var error = await Assert.ThrowsAsync<StatementParsingException>(() =>
            ParseAsync(csv, "Africa/Kampala", null));

        Assert.Contains("Row 2, TransactionDate", error.Message);
        Assert.Contains("Row 2, Description", error.Message);
        Assert.Contains("Row 3, Debit/Credit", error.Message);
    }

    [Fact]
    public async Task Parse_RejectsInvalidAndAmbiguousLocalTimes()
    {
        var csv =
            "TransactionDate,TransactionTime,Reference,Description,Debit,Credit,RunningBalance\n" +
            "2026-11-01,01:30:00,REF,Fee,,1.00,10.00";

        var error = await Assert.ThrowsAsync<StatementParsingException>(() =>
            ParseAsync(csv, "America/New_York", null));

        Assert.Contains("local date/time is invalid or ambiguous", error.Message);
    }

    private static Task<ParsedStatement> ParseAsync(
        string csv, string zone, decimal? baseline, BankBalanceBasis basis = BankBalanceBasis.Ledger)
    {
        var profile = new UniversalBankCsvStatementProfile();
        var account = new StatementAccountContext(
            "001234567890", "UGX", zone, basis, From, To, baseline);
        var source = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        return profile.ParseAsync(source, UniversalBankCsvStatementProfile.CsvContentType, account, CancellationToken.None);
    }
}

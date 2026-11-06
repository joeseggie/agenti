using System.IO.Compression;
using System.Security;
using System.Text;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.UnitTests.Helpers;
using EastSeat.Agenti.Web.Features.BankStatements;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EastSeat.Agenti.UnitTests.BankReconciliation;

// Every workbook here is synthetic. It mirrors the layout of the bank's export, never its data.
public class CentenaryAgencyFloatXlsxStatementProfileTests
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string DocumentRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string WorkbookPart = "xl/workbook.xml";
    private const string RelationshipsPart = "xl/_rels/workbook.xml.rels";
    private const string SheetPart = "xl/worksheets/sheet1.xml";
    private static readonly TimeSpan Kampala = TimeSpan.FromHours(3);
    private static readonly StatementAccountContext Account = new("0123456789", "UGX", "Africa/Kampala", BankBalanceBasis.Ledger);

    [Fact]
    public async Task Parse_MapsTheBankLayoutToDateOnlyLedgerRows()
    {
        var profile = new CentenaryAgencyFloatXlsxStatementProfile();

        var statement = await ParseAsync(Standard());

        Assert.Equal("0123456789", statement.AccountIdentifier);
        Assert.Equal("UGX", statement.Currency);
        Assert.Equal(BankBalanceBasis.Ledger, statement.BalanceBasis);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, Kampala), statement.CoverageFrom);
        Assert.Equal(Kampala, statement.CoverageFrom.Offset);
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 0, 0, 0, Kampala), statement.CoverageTo);
        Assert.Equal(1_000_000m, statement.OpeningBalance);
        Assert.Equal(820_000m, statement.ClosingBalance);
        Assert.True(statement.CompleteCoverage);
        Assert.Equal(new[] { -250_000m, 100_000m, -50_000m, 20_000m }, statement.Entries.Select(e => e.SignedAmount));
        Assert.Equal(new[] { 750_000m, 850_000m, 800_000m, 820_000m }, statement.Entries.Select(e => e.RunningBalance));
        Assert.Equal(new[] { null, "AGENCY-1234567890123", "123456-123456789-FTB", null }, statement.Entries.Select(e => e.Reference));
        Assert.Equal(new[] { "2001", "2002", "2003", "1004" }, statement.Entries.Select(e => e.TransactionNumber));
        var first = statement.Entries[0];
        Assert.Equal("Sheet1!A31:L31", first.SourceLocation);
        Assert.Equal("JOURNAL DEBIT 0700000001 TEST PERSON", first.Narration);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, Kampala), first.OccurredAt);
        Assert.All(statement.Entries.Skip(1), e => Assert.Equal(new DateTimeOffset(2026, 11, 2, 0, 0, 0, Kampala), e.OccurredAt));
        Assert.Equal(new DateOnly(2026, 11, 3), statement.Entries[2].ValueDate);
        Assert.All(statement.Entries, e =>
        {
            Assert.False(e.HasPreciseTimestamp);
            Assert.Null(e.PostedAt);
            Assert.Null(e.CustomerIdentifier);
            Assert.Equal("UGX", e.Currency);
            Assert.Null(profile.GetReliableIdentity(e));
        });
    }

    [Fact]
    public async Task Parse_EmptyDayClosesAtTheOpeningBalance()
    {
        var sheet = Standard();
        foreach (var row in sheet.Rows.Keys.Where(r => r > 30).ToList())
            sheet.Rows.Remove(row);
        sheet[16, 2] = new("02/11/2026");
        sheet[23, 2] = new("0.00");
        sheet[24, 2] = new("0.00");

        var statement = await ParseAsync(sheet);

        Assert.Empty(statement.Entries);
        Assert.Equal(statement.OpeningBalance, statement.ClosingBalance);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 0, 0, 0, Kampala), statement.CoverageFrom);
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 0, 0, 0, Kampala), statement.CoverageTo);
    }

    [Fact]
    public async Task Parse_RejectsATruncatedExportThatNoLongerMatchesTheBankTotals()
    {
        var sheet = Standard();
        sheet.Rows.Remove(34);

        var error = await Assert.ThrowsAsync<StatementParsingException>(() => ParseAsync(sheet));

        Assert.Contains("Total Credits and Total Debits", error.Message);
    }

    [Theory]
    [InlineData("running-balance")]
    [InlineData("out-of-coverage-date")]
    public async Task Parse_DoesNotDeclareCoverageCompleteWhenRowsDoNotReconcile(string change)
    {
        var sheet = Standard();
        if (change == "running-balance")
            sheet[31, 8] = new("740,000.00");
        else
            sheet[31, 1] = new("03/11/2026");

        var statement = await ParseAsync(sheet);

        Assert.False(statement.CompleteCoverage);
    }

    [Theory]
    [InlineData("renamed-header", "must have the columns")]
    [InlineData("missing-label", "exactly one Total Credits")]
    [InlineData("duplicate-label", "exactly one Date From")]
    [InlineData("account-without-currency", "currency code")]
    [InlineData("wrong-currency", "does not match the configured bank account")]
    [InlineData("unknown-direction", "unknown Debit / Credit")]
    [InlineData("bad-date", "dd/MM/yyyy")]
    [InlineData("bad-amount", "bank amount")]
    [InlineData("signed-amount", "bank amount")]
    [InlineData("numeric-amount", "spreadsheet number")]
    [InlineData("extra-column", "outside the expected statement columns")]
    [InlineData("content-after-table", "after the transaction table")]
    public async Task Parse_RejectsChangedOrEditedLayouts(string change, string message)
    {
        var sheet = Standard();
        switch (change)
        {
            case "renamed-header": sheet[30, 7] = new("Amt"); break;
            case "missing-label": sheet.Rows.Remove(23); break;
            case "duplicate-label": sheet[29, 1] = new("Date From : "); sheet[29, 2] = new("03/11/2026"); break;
            case "account-without-currency": sheet[1, 2] = new("0123456789"); break;
            case "wrong-currency": sheet[1, 2] = new("0123456789 USD"); break;
            case "unknown-direction": sheet[31, 6] = new("X"); break;
            case "bad-date": sheet[31, 1] = new("2026-11-01"); break;
            case "bad-amount": sheet[31, 7] = new("250000"); break;
            case "signed-amount": sheet[31, 7] = new("-250,000.00"); break;
            case "numeric-amount": sheet[31, 7] = new("250000", "n"); break;
            case "extra-column": sheet[31, 13] = new("unexpected"); break;
            case "content-after-table": sheet[37, 1] = new("Printed by: synthetic user"); break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }

        var error = await Assert.ThrowsAsync<StatementParsingException>(() => ParseAsync(sheet));

        Assert.Contains(message, error.Message);
    }

    [Theory]
    [InlineData("not-a-zip", "not a readable XLSX")]
    [InlineData("dtd", "not a readable XLSX")]
    [InlineData("formula", "formulas")]
    [InlineData("error-cell", "cell errors")]
    [InlineData("duplicate-cell", "repeated or out of order")]
    [InlineData("two-sheets", "exactly one worksheet")]
    [InlineData("hidden-sheet", "worksheet must be visible")]
    [InlineData("macro", "macros or external links")]
    [InlineData("external-link", "macros or external links")]
    [InlineData("external-relationship", "macros or external links")]
    [InlineData("macro-relationship", "macros or external links")]
    [InlineData("external-link-relationship", "macros or external links")]
    [InlineData("relationship-traversal", "relationships are invalid")]
    public async Task Read_RejectsUnsafeOrAmbiguousPackages(string change, string message)
    {
        var sheet = Standard();
        if (change == "formula")
            sheet[31, 7] = new("250,000.00", "str", "TEXT(250000,\"#,##0.00\")");
        if (change == "error-cell")
            sheet[31, 9] = new("#N/A", "e");
        var parts = Parts(sheet);
        switch (change)
        {
            case "dtd":
                Replace(parts, SheetPart, "<worksheet ", "<!DOCTYPE worksheet [<!ENTITY x \"y\">]><worksheet ");
                break;
            case "duplicate-cell":
                Replace(parts, SheetPart, "<c r=\"B31\"", "<c r=\"A31\" t=\"inlineStr\"><is><t>01/11/2026</t></is></c><c r=\"B31\"");
                break;
            case "two-sheets":
                Replace(parts, WorkbookPart, "</sheets>", "<sheet name=\"Sheet2\" sheetId=\"2\" r:id=\"rId4\"/></sheets>");
                break;
            case "hidden-sheet":
                Replace(parts, WorkbookPart, "state=\"visible\"", "state=\"hidden\"");
                break;
            case "macro":
                parts["xl/vbaProject.bin"] = "binary";
                break;
            case "external-link":
                parts["xl/externalLinks/externalLink1.xml"] = "<externalLink/>";
                break;
            case "external-relationship":
                Replace(parts, RelationshipsPart, "</Relationships>",
                    $"<Relationship Id=\"rId9\" Type=\"{DocumentRelationships}/externalLinkPath\" " +
                    "Target=\"https://example.invalid/book.xlsx\" TargetMode=\"External\"/></Relationships>");
                break;
            case "macro-relationship":
                Replace(parts, RelationshipsPart, "</Relationships>",
                    $"<Relationship Id=\"rId9\" Type=\"{DocumentRelationships}/vbaProject\" " +
                    "Target=\"project.bin\"/></Relationships>");
                break;
            case "external-link-relationship":
                Replace(parts, RelationshipsPart, "</Relationships>",
                    $"<Relationship Id=\"rId9\" Type=\"{DocumentRelationships}/externalLink\" " +
                    "Target=\"custom-link.xml\"/></Relationships>");
                break;
            case "relationship-traversal":
                Replace(parts, RelationshipsPart, "Target=\"worksheets/sheet1.xml\"", "Target=\"../worksheets/sheet1.xml\"");
                break;
        }
        var bytes = change == "not-a-zip" ? Encoding.UTF8.GetBytes("Account Number,Amount") : Zip(parts);

        var error = await Assert.ThrowsAsync<StatementParsingException>(() => ParseAsync(bytes));

        Assert.Contains(message, error.Message);
    }

    [Theory]
    [InlineData("package", "exceeds the supported file size")]
    [InlineData("entries", "too many parts")]
    [InlineData("part", "A workbook part expands beyond")]
    [InlineData("total", "The workbook expands beyond")]
    [InlineData("cells", "too many cells")]
    [InlineData("strings", "too many text values")]
    public async Task Read_EnforcesPackageLimits(string limit, string message)
    {
        var limits = limit switch
        {
            "package" => new XlsxReadLimits(MaximumPackageBytes: 512),
            "entries" => new XlsxReadLimits(MaximumEntries: 4),
            "part" => new XlsxReadLimits(MaximumPartBytes: 2_048),
            "total" => new XlsxReadLimits(MaximumTotalPartBytes: 4_096),
            "cells" => new XlsxReadLimits(MaximumCells: 10),
            "strings" => new XlsxReadLimits(MaximumSharedStrings: 10),
            _ => throw new ArgumentOutOfRangeException(nameof(limit))
        };

        var error = await Assert.ThrowsAsync<StatementParsingException>(() =>
            XlsxWorksheetReader.ReadSingleWorksheetAsync(new MemoryStream(Zip(Parts(Standard()))), CancellationToken.None, limits));

        Assert.Contains(message, error.Message);
    }

    [Fact]
    public async Task Read_EnforcesAggregateResolvedCellTextLimit()
    {
        var parts = Parts(Standard());
        var repeatedValue = string.Join("", Enumerable.Range(1, 20)
            .Select(column => $"""<c r="{ColumnName(column)}100" t="s"><v>0</v></c>"""));
        Replace(parts, "xl/sharedStrings.xml", "Account Number : ", new string('x', 10_000));
        Replace(parts, SheetPart, "</sheetData>", $"<row r=\"100\">{repeatedValue}</row></sheetData>");

        var error = await Assert.ThrowsAsync<StatementParsingException>(() =>
            XlsxWorksheetReader.ReadSingleWorksheetAsync(
                new MemoryStream(Zip(parts)), CancellationToken.None,
                new XlsxReadLimits(MaximumTotalTextCharacters: 50_000)));

        Assert.Contains("too much text", error.Message);
    }

    [Fact]
    public async Task VerifiedRelease_ImportsTheExportForReviewWithVerifiedCoverage()
    {
        using var fixture = new BankingTestContext();
        var profile = new VerifiedForTest(new CentenaryAgencyFloatXlsxStatementProfile());
        fixture.Account.AccountIdentifier = "0123456789";
        fixture.Account.ImportProfileKey = profile.Key;
        fixture.Account.ImportProfileVerified = true;
        fixture.Account.BankIdentifier = profile.BankIdentifier;
        fixture.Db.SaveChanges();
        var clock = new BusinessClock(new FixedTimeProvider());
        var access = new BankingAccessService(fixture.Db, clock);
        var evidence = new BankEvidenceService(fixture.Db, access, clock, Options.Create(new BankEvidenceOptions()));
        var service = new BankStatementService(fixture.Db, access, clock, new StatementProfileCatalog([profile]),
            Options.Create(new BankStatementOptions()), NullLogger<BankStatementService>.Instance);
        var file = await evidence.UploadAsync(fixture.Admin.Id, fixture.Account.Id, Guid.NewGuid(),
            "Account-Transaction-Report.xlsx", true, new MemoryStream(Zip(Parts(Standard()))));

        var import = await service.StartAsync(fixture.Admin.Id, fixture.Account.Id, file.Id);
        Assert.True(await service.ProcessNextAsync());
        var preview = await service.GetPreviewAsync(fixture.Admin.Id, import.Id);

        Assert.Equal(StatementImportStatus.ReadyForReview, preview.Import.Status);
        Assert.Empty(preview.Errors);
        Assert.True(preview.Import.CoverageVerified);
        Assert.Equal(4, preview.Rows.Count);
        Assert.All(preview.Rows, row => Assert.False(row.HasPreciseTimestamp));
    }

    private static Task<ParsedStatement> ParseAsync(Sheet sheet) => ParseAsync(Zip(Parts(sheet)));

    private static Task<ParsedStatement> ParseAsync(byte[] workbook) =>
        new CentenaryAgencyFloatXlsxStatementProfile().ParseAsync(new MemoryStream(workbook),
            CentenaryAgencyFloatXlsxStatementProfile.XlsxContentType, Account, CancellationToken.None);

    private static Sheet Standard()
    {
        var sheet = new Sheet();
        sheet[1, 3] = new("SYNTHETIC BANK LIMITED, PLOT 0 TEST ROAD");
        string[][] header =
        [
            ["Account Number : ", "0123456789 UGX"], ["Account Description : ", "SYNTHETIC AGENT"],
            ["Customer Code : ", "000000"], ["Customer Name : ", "SYNTHETIC AGENT"], ["2nd Beneficiary Code:", ""],
            ["2nd Beneficiary Name:", ""], ["Product Code : ", "00000"], ["Product Description : ", "SYNTHETIC FLOAT PRODUCT"],
            ["Branch Code : ", "000"], ["Branch Name : ", "TEST BRANCH"], ["Branch Address : ", "TEST ADDRESS"],
            ["Branch City : ", "TEST CITY"], ["Branch Zip Code : ", ""], ["Branch Telephone : ", "000000000"],
            ["Branch Email : ", "branch@example.invalid"], ["Date From : ", "01/11/2026"], ["Date To : ", "02/11/2026"],
            ["Opening Balance: : ", "1,000,000.00"], ["Book Balance : ", "820,000.00"], ["Available Balance : ", "820,000.00"],
            ["Unclear Balance : ", "0.00"], ["Blocked Balance : ", "0.00"], ["Total Credits : ", "120,000.00"],
            ["Total Debits : ", "300,000.00"], ["Unpaid Cheques : ", "0"], ["Unpaid Cheques Amount : ", "0.00"],
            ["Overdraft Limit : ", "0.00"], ["Overdraft Expiry Date : ", "2026-12-31"]
        ];
        for (var i = 0; i < header.Length; i++)
        {
            sheet[i + 1, 1] = new(header[i][0]);
            if (header[i][1].Length > 0)
                sheet[i + 1, 2] = new(header[i][1]);
            sheet[i + 1, 13] = new(string.Empty, "styled");
        }
        string[] columns =
        [
            "Transaction \nDate", "Value \nDate", "Branch", "User ", "S/N", "Debit / Credit \n(D/C)", "Amount",
            "Cumulative \nBalance", "Transaction", "Justification", "Description", "Comments"
        ];
        for (var column = 0; column < columns.Length; column++)
            sheet[30, column + 1] = new(columns[column], column == 11 ? "rich" : "s");
        string[][] rows =
        [
            ["01/11/2026", "01/11/2026", "001", "TST0001", "1001", "D", "250,000.00", "750,000.00", "2001", "90001", "JOURNAL DEBIT", "0700000001 TEST PERSON"],
            ["02/11/2026", "02/11/2026", "001", "TST0001", "1002", "C", "100,000.00", "850,000.00", "2002", "90002", "JOURNAL CREDIT", "07000000011 AGENCY-1234567890123"],
            ["02/11/2026", "03/11/2026", "001", "TST0001", "1003", "D", "50,000.00", "800,000.00", "2003", "90001", "JOURNAL DEBIT", "123456-123456789-FTB"],
            ["02/11/2026", "02/11/2026", "001", "TST0001", "1004", "C", "20,000.00", "820,000.00", "", "90002", "JOURNAL CREDIT", "0700000002-AGNTBANK DEP TEST PERSON"]
        ];
        for (var row = 0; row < rows.Length; row++)
            for (var column = 0; column < rows[row].Length; column++)
                if (rows[row][column].Length > 0)
                    sheet[31 + row, column + 1] = new(rows[row][column], row == 1 && column == 11 ? "inlineStr" : "s");
        return sheet;
    }

    private static Dictionary<string, string> Parts(Sheet sheet)
    {
        const string Declaration = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""";
        var shared = new StringBuilder();
        var count = 0;
        var cells = new StringBuilder();
        foreach (var (row, columns) in sheet.Rows)
        {
            cells.Append($"""<row r="{row}">""");
            foreach (var (column, cell) in columns)
            {
                var reference = $"{ColumnName(column)}{row}";
                var formula = cell.Formula == null ? string.Empty : $"<f>{Escape(cell.Formula)}</f>";
                cells.Append(cell.Type switch
                {
                    "s" or "rich" => $"""<c r="{reference}" s="1" t="s"><v>{Share(cell)}</v></c>""",
                    "inlineStr" => $"""<c r="{reference}" t="inlineStr"><is><t>{Escape(cell.Value)}</t></is></c>""",
                    "styled" => $"""<c r="{reference}" s="2"/>""",
                    "n" => $"""<c r="{reference}">{formula}<v>{cell.Value}</v></c>""",
                    _ => $"""<c r="{reference}" t="{cell.Type}">{formula}<v>{Escape(cell.Value)}</v></c>"""
                });
            }
            cells.Append("</row>");
        }

        return new Dictionary<string, string>
        {
            ["[Content_Types].xml"] = Declaration +
                """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/></Types>""",
            ["_rels/.rels"] = Declaration +
                $"""<Relationships xmlns="{PackageRelationships}"><Relationship Id="rId1" Type="{DocumentRelationships}/officeDocument" Target="xl/workbook.xml"/></Relationships>""",
            [WorkbookPart] = Declaration +
                $"""<workbook xmlns="{Main}" xmlns:r="{DocumentRelationships}"><sheets><sheet name="Sheet1" sheetId="1" state="visible" r:id="rId4"/></sheets></workbook>""",
            [RelationshipsPart] = Declaration +
                $"""<Relationships xmlns="{PackageRelationships}"><Relationship Id="rId2" Type="{DocumentRelationships}/styles" Target="styles.xml"/><Relationship Id="rId3" Type="{DocumentRelationships}/sharedStrings" Target="sharedStrings.xml"/><Relationship Id="rId4" Type="{DocumentRelationships}/worksheet" Target="worksheets/sheet1.xml"/></Relationships>""",
            ["xl/styles.xml"] = Declaration + $"""<styleSheet xmlns="{Main}"/>""",
            ["xl/sharedStrings.xml"] = Declaration + $"""<sst xmlns="{Main}" count="{count}" uniqueCount="{count}">{shared}</sst>""",
            [SheetPart] = Declaration +
                $"""<worksheet xmlns="{Main}" xmlns:r="{DocumentRelationships}"><sheetData>{cells}</sheetData><mergeCells count="1"><mergeCell ref="C1:L4"/></mergeCells></worksheet>"""
        };

        int Share(Cell cell)
        {
            // A rich-text entry split into runs with a phonetic hint that must not be read as cell text.
            shared.Append(cell.Type == "rich"
                ? $"""<si><r><t>{Escape(cell.Value[..4])}</t></r><r><rPr><b/></rPr><t>{Escape(cell.Value[4..])}</t></r><rPh sb="0" eb="1"><t>HINT</t></rPh></si>"""
                : $"""<si><t xml:space="preserve">{Escape(cell.Value)}</t></si>""");
            return count++;
        }
    }

    private static byte[] Zip(IReadOnlyDictionary<string, string> parts)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var directory in new[] { "_rels/", "xl/", "xl/_rels/", "xl/worksheets/" })
                archive.CreateEntry(directory);
            foreach (var (name, content) in parts)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }
        return output.ToArray();
    }

    private static void Replace(Dictionary<string, string> parts, string part, string find, string replacement)
    {
        Assert.Contains(find, parts[part]);
        parts[part] = parts[part].Replace(find, replacement, StringComparison.Ordinal);
    }

    private static string ColumnName(int column)
    {
        var name = string.Empty;
        for (; column > 0; column = (column - 1) / 26)
            name = (char)('A' + (column - 1) % 26) + name;
        return name;
    }

    private static string Escape(string value) => SecurityElement.Escape(value)!;

    private sealed record Cell(string Value, string Type = "s", string? Formula = null);

    private sealed class Sheet
    {
        public SortedDictionary<int, SortedDictionary<int, Cell>> Rows { get; } = new();

        public Cell this[int row, int column]
        {
            set
            {
                if (!Rows.TryGetValue(row, out var cells))
                    Rows[row] = cells = new();
                cells[column] = value;
            }
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => BankingTestContext.Now.AddDays(1);
    }

    private sealed class VerifiedForTest(IStatementImportProfile inner) : IStatementImportProfile
    {
        public string Key => inner.Key;
        public string BankIdentifier => inner.BankIdentifier;
        public string DisplayName => inner.DisplayName;
        public int Version => inner.Version;
        public bool IsVerified => true;
        public BankBalanceBasis BalanceBasis => inner.BalanceBasis;
        public IReadOnlyList<string> SupportedContentTypes => inner.SupportedContentTypes;
        public Task<ParsedStatement> ParseAsync(Stream source, string contentType, StatementAccountContext account, CancellationToken cancellationToken) =>
            inner.ParseAsync(source, contentType, account, cancellationToken);
        public string? GetReliableIdentity(ParsedStatementEntry entry) => inner.GetReliableIdentity(entry);
    }
}

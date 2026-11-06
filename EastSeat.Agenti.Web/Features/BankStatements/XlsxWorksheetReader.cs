using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace EastSeat.Agenti.Web.Features.BankStatements;

public sealed record XlsxReadLimits(
    long MaximumPackageBytes = 16 * 1024 * 1024,
    int MaximumEntries = 256,
    long MaximumPartBytes = 16 * 1024 * 1024,
    long MaximumTotalPartBytes = 32 * 1024 * 1024,
    int MaximumCells = 200_000,
    int MaximumSharedStrings = 200_000)
{
    public static XlsxReadLimits Default { get; } = new();
}

public enum XlsxCellKind
{
    Text,
    Number,
    Boolean
}

public sealed record XlsxCell(int Row, int Column, XlsxCellKind Kind, string Value);

public sealed class XlsxWorksheet
{
    private readonly Dictionary<(int Row, int Column), XlsxCell> _cells;

    internal XlsxWorksheet(string name, Dictionary<(int Row, int Column), XlsxCell> cells)
    {
        Name = name;
        _cells = cells;
    }

    public string Name { get; }
    public IReadOnlyCollection<XlsxCell> Cells => _cells.Values;

    public XlsxCell? Cell(int row, int column) => _cells.GetValueOrDefault((row, column));
}

/// <summary>
/// Reads cached cell values from a single-sheet XLSX package without third-party dependencies.
/// The package is untrusted: sizes are bounded, DTDs are prohibited and formulas, error cells,
/// macros and external links are rejected instead of being evaluated or followed.
/// </summary>
public static class XlsxWorksheetReader
{
    private const string MainNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string PackageRelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string DocumentRelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const int MaximumRows = 1_048_576;
    private const int MaximumColumns = 16_384;
    private const string Unreadable = "The file is not a readable XLSX workbook. Upload the original bank export unedited.";

    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        CloseInput = true
    };

    public static async Task<XlsxWorksheet> ReadSingleWorksheetAsync(
        Stream source, CancellationToken cancellationToken, XlsxReadLimits? limits = null)
    {
        limits ??= XlsxReadLimits.Default;
        using var package = await CopyBoundedAsync(source, limits.MaximumPackageBytes, cancellationToken);
        try
        {
            using var archive = new ZipArchive(package, ZipArchiveMode.Read);
            var parts = IndexParts(archive, limits);
            var budget = new ReadBudget(limits.MaximumTotalPartBytes);
            var (sheetName, sheetRelationshipId) = ReadWorkbook(parts, limits, budget);
            var relationships = ReadRelationships(parts, limits, budget);
            if (!relationships.TryGetValue(sheetRelationshipId, out var sheetRelationship) ||
                !sheetRelationship.Type.EndsWith("/worksheet", StringComparison.Ordinal))
                throw Fail("The workbook's worksheet relationship is missing or invalid.");
            var sharedStringTargets = relationships.Values
                .Where(r => r.Type.EndsWith("/sharedStrings", StringComparison.Ordinal)).ToList();
            if (sharedStringTargets.Count > 1)
                throw Fail("The workbook declares more than one shared string table.");
            var strings = sharedStringTargets.Count == 0
                ? []
                : ReadSharedStrings(parts, ResolvePart(sharedStringTargets[0].Target), limits, budget, cancellationToken);
            var cells = ReadCells(parts, ResolvePart(sheetRelationship.Target), strings, limits, budget, cancellationToken);
            return new XlsxWorksheet(sheetName, cells);
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException or IOException or
            NotSupportedException or FormatException or OverflowException)
        {
            throw Fail(Unreadable);
        }
    }

    private static async Task<MemoryStream> CopyBoundedAsync(Stream source, long limit, CancellationToken cancellationToken)
    {
        var copy = new MemoryStream();
        var buffer = new byte[81_920];
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (copy.Length + read > limit)
                throw Fail("The workbook exceeds the supported file size.");
            copy.Write(buffer, 0, read);
        }
        copy.Position = 0;
        return copy;
    }

    private static Dictionary<string, ZipArchiveEntry> IndexParts(ZipArchive archive, XlsxReadLimits limits)
    {
        if (archive.Entries.Count > limits.MaximumEntries)
            throw Fail("The workbook contains too many parts.");
        var parts = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (name.EndsWith('/'))
                continue;
            if (name.Contains('\\') || name.Split('/').Any(segment => segment is "" or "." or ".."))
                throw Fail("The workbook contains an invalid part name.");
            if (!parts.TryAdd(name, entry))
                throw Fail("The workbook repeats a part name.");
            if (name.Equals("xl/vbaProject.bin", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("xl/externalLinks/", StringComparison.OrdinalIgnoreCase))
                throw Fail("The workbook contains macros or external links. Upload the original bank export unedited.");
        }
        return parts;
    }

    private static (string Name, string RelationshipId) ReadWorkbook(
        Dictionary<string, ZipArchiveEntry> parts, XlsxReadLimits limits, ReadBudget budget)
    {
        using var reader = OpenPart(parts, "xl/workbook.xml", limits, budget);
        RequireRoot(reader, "workbook");
        var sheets = new List<(string Name, string RelationshipId)>();
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.NamespaceURI != MainNamespace || reader.LocalName != "sheet")
                continue;
            var name = reader.GetAttribute("name");
            var relationshipId = reader.GetAttribute("id", DocumentRelationshipNamespace);
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(relationshipId))
                throw Fail("The workbook's sheet list is invalid.");
            var state = reader.GetAttribute("state");
            if (state != null && !string.Equals(state, "visible", StringComparison.OrdinalIgnoreCase))
                throw Fail("The workbook's worksheet must be visible.");
            sheets.Add((name, relationshipId));
        }
        return sheets.Count == 1 ? sheets[0] : throw Fail("The workbook must contain exactly one worksheet.");
    }

    private static Dictionary<string, (string Type, string Target)> ReadRelationships(
        Dictionary<string, ZipArchiveEntry> parts, XlsxReadLimits limits, ReadBudget budget)
    {
        using var reader = OpenPart(parts, "xl/_rels/workbook.xml.rels", limits, budget);
        var relationships = new Dictionary<string, (string Type, string Target)>(StringComparer.Ordinal);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.NamespaceURI != PackageRelationshipNamespace ||
                reader.LocalName != "Relationship")
                continue;
            var id = reader.GetAttribute("Id");
            var type = reader.GetAttribute("Type");
            var target = reader.GetAttribute("Target");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(target))
                throw Fail("The workbook relationships are invalid.");
            if (string.Equals(reader.GetAttribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase))
                throw Fail("The workbook contains macros or external links. Upload the original bank export unedited.");
            if (!relationships.TryAdd(id, (type, target)))
                throw Fail("The workbook relationships are invalid.");
        }
        return relationships;
    }

    private static List<string> ReadSharedStrings(
        Dictionary<string, ZipArchiveEntry> parts, string partName, XlsxReadLimits limits, ReadBudget budget,
        CancellationToken cancellationToken)
    {
        using var reader = OpenPart(parts, partName, limits, budget);
        RequireRoot(reader, "sst");
        var strings = new List<string>();
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.NamespaceURI != MainNamespace || reader.LocalName != "si")
                continue;
            if (strings.Count >= limits.MaximumSharedStrings)
                throw Fail("The workbook contains too many text values.");
            if (strings.Count % 1_000 == 0)
                cancellationToken.ThrowIfCancellationRequested();
            strings.Add(ReadRichText(reader));
        }
        return strings;
    }

    private static Dictionary<(int Row, int Column), XlsxCell> ReadCells(
        Dictionary<string, ZipArchiveEntry> parts, string partName, IReadOnlyList<string> strings,
        XlsxReadLimits limits, ReadBudget budget, CancellationToken cancellationToken)
    {
        using var reader = OpenPart(parts, partName, limits, budget);
        RequireRoot(reader, "worksheet");
        var cells = new Dictionary<(int Row, int Column), XlsxCell>();
        var sheetDataSeen = false;
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.NamespaceURI != MainNamespace || reader.LocalName != "sheetData")
                continue;
            if (sheetDataSeen)
                throw Fail("The worksheet repeats its cell table.");
            sheetDataSeen = true;
            if (!reader.IsEmptyElement)
                ReadSheetData(reader, strings, cells, limits, cancellationToken);
        }
        return sheetDataSeen ? cells : throw Fail("The worksheet has no cell table.");
    }

    private static void ReadSheetData(
        XmlReader reader, IReadOnlyList<string> strings, Dictionary<(int Row, int Column), XlsxCell> cells,
        XlsxReadLimits limits, CancellationToken cancellationToken)
    {
        var depth = reader.Depth;
        var previousRow = 0;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                return;
            if (reader.NodeType != XmlNodeType.Element)
                continue;
            if (reader.NamespaceURI != MainNamespace || reader.LocalName != "row")
            {
                SkipElement(reader);
                continue;
            }
            cancellationToken.ThrowIfCancellationRequested();
            var rowAttribute = reader.GetAttribute("r");
            var row = rowAttribute == null ? previousRow + 1 : ParseIndex(rowAttribute, MaximumRows);
            if (row <= previousRow)
                throw Fail("The worksheet rows are repeated or out of order.");
            previousRow = row;
            if (reader.IsEmptyElement)
                continue;
            var rowDepth = reader.Depth;
            var previousColumn = 0;
            while (reader.Read() && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == rowDepth))
            {
                if (reader.NodeType != XmlNodeType.Element)
                    continue;
                if (reader.NamespaceURI != MainNamespace || reader.LocalName != "c")
                {
                    SkipElement(reader);
                    continue;
                }
                var cell = ReadCell(reader, row, ref previousColumn, strings);
                if (cell == null)
                    continue;
                if (cells.Count >= limits.MaximumCells)
                    throw Fail("The worksheet contains too many cells.");
                cells.Add((cell.Row, cell.Column), cell);
            }
        }
        throw Fail(Unreadable);
    }

    private static XlsxCell? ReadCell(XmlReader reader, int row, ref int previousColumn, IReadOnlyList<string> strings)
    {
        var reference = reader.GetAttribute("r");
        var type = reader.GetAttribute("t");
        var column = previousColumn + 1;
        if (reference != null)
        {
            var (referenceRow, referenceColumn) = ParseReference(reference);
            if (referenceRow != row)
                throw Fail("A worksheet cell is stored outside its row.");
            column = referenceColumn;
        }
        if (column <= previousColumn || column > MaximumColumns)
            throw Fail("The worksheet cells are repeated or out of order.");
        previousColumn = column;

        string? value = null;
        string? inlineText = null;
        var hasFormula = false;
        if (!reader.IsEmptyElement)
        {
            var depth = reader.Depth;
            while (reader.Read() && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
            {
                if (reader.NodeType != XmlNodeType.Element)
                    continue;
                if (reader.NamespaceURI == MainNamespace && reader.LocalName == "f")
                {
                    hasFormula = true;
                    SkipElement(reader);
                }
                else if (reader.NamespaceURI == MainNamespace && reader.LocalName == "v" && value == null)
                    value = ReadText(reader);
                else if (reader.NamespaceURI == MainNamespace && reader.LocalName == "is" && inlineText == null)
                    inlineText = ReadRichText(reader);
                else
                    SkipElement(reader);
            }
        }
        if (hasFormula)
            throw Fail("The worksheet contains formulas. Upload the original bank export unedited.");
        return type switch
        {
            "s" => value == null ? null : new XlsxCell(row, column, XlsxCellKind.Text, SharedString(value, strings)),
            "inlineStr" => inlineText == null ? null : new XlsxCell(row, column, XlsxCellKind.Text, inlineText),
            "str" => value == null ? null : new XlsxCell(row, column, XlsxCellKind.Text, value),
            null or "n" => value == null ? null : new XlsxCell(row, column, XlsxCellKind.Number, ValidNumber(value)),
            "b" => value == null ? null : new XlsxCell(row, column, XlsxCellKind.Boolean,
                value is "0" or "1" ? value : throw Fail(Unreadable)),
            "e" => throw Fail("The worksheet contains cell errors. Upload the original bank export unedited."),
            _ => throw Fail("The worksheet contains an unsupported cell type.")
        };
    }

    private static string SharedString(string value, IReadOnlyList<string> strings) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < strings.Count
            ? strings[index]
            : throw Fail("A worksheet cell refers to a missing text value.");

    private static string ValidNumber(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
            ? value
            : throw Fail("A worksheet cell contains an invalid number.");

    // Concatenates the plain and rich-text runs of <si>/<is>, ignoring phonetic hints, and leaves the
    // reader on the element's end tag.
    private static string ReadRichText(XmlReader reader)
    {
        if (reader.IsEmptyElement)
            return string.Empty;
        var depth = reader.Depth;
        var text = new StringBuilder();
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                return text.ToString();
            if (reader.NodeType != XmlNodeType.Element)
                continue;
            if (reader.NamespaceURI == MainNamespace && reader.LocalName == "t")
                text.Append(ReadText(reader));
            else if (reader.NamespaceURI != MainNamespace || reader.LocalName is "rPh" or "phoneticPr" or "rPr")
                SkipElement(reader);
        }
        throw Fail(Unreadable);
    }

    private static string ReadText(XmlReader reader)
    {
        if (reader.IsEmptyElement)
            return string.Empty;
        var depth = reader.Depth;
        var text = new StringBuilder();
        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace:
                    text.Append(reader.Value);
                    break;
                case XmlNodeType.EndElement when reader.Depth == depth:
                    return text.ToString();
                case XmlNodeType.Element:
                    throw Fail(Unreadable);
            }
        }
        throw Fail(Unreadable);
    }

    private static void SkipElement(XmlReader reader)
    {
        if (reader.IsEmptyElement)
            return;
        var depth = reader.Depth;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                return;
        }
        throw Fail(Unreadable);
    }

    private static void RequireRoot(XmlReader reader, string localName)
    {
        if (reader.MoveToContent() != XmlNodeType.Element || reader.NamespaceURI != MainNamespace || reader.LocalName != localName)
            throw Fail("The workbook format is not supported. Upload the original bank export unedited.");
    }

    private static XmlReader OpenPart(
        Dictionary<string, ZipArchiveEntry> parts, string name, XlsxReadLimits limits, ReadBudget budget)
    {
        if (!parts.TryGetValue(name, out var entry))
            throw Fail(Unreadable);
        return XmlReader.Create(new BoundedReadStream(entry.Open(), limits.MaximumPartBytes, budget), Settings);
    }

    private static string ResolvePart(string target)
    {
        if (target.IndexOfAny(['\\', ':', '?', '#', '%']) >= 0)
            throw Fail("The workbook relationships are invalid.");
        var path = target.StartsWith('/') ? target[1..] : "xl/" + target;
        if (path.Split('/').Any(segment => segment is "" or "." or ".."))
            throw Fail("The workbook relationships are invalid.");
        return path;
    }

    private static (int Row, int Column) ParseReference(string reference)
    {
        var letters = 0;
        while (letters < reference.Length && letters < 3 && reference[letters] is >= 'A' and <= 'Z')
            letters++;
        if (letters == 0 || letters == reference.Length)
            throw Fail("A worksheet cell has an invalid reference.");
        var column = 0;
        for (var i = 0; i < letters; i++)
            column = column * 26 + reference[i] - 'A' + 1;
        if (column > MaximumColumns)
            throw Fail("A worksheet cell has an invalid reference.");
        return (ParseIndex(reference[letters..], MaximumRows), column);
    }

    private static int ParseIndex(string value, int maximum) =>
        value.Length is > 0 and <= 7 && value[0] != '0' &&
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index <= maximum
            ? index
            : throw Fail("A worksheet row or cell has an invalid reference.");

    private static StatementParsingException Fail(string message) => new(message);

    private sealed class ReadBudget(long limit)
    {
        private long _used;

        public void Consume(int bytes)
        {
            _used += bytes;
            if (_used > limit)
                throw Fail("The workbook expands beyond the supported size.");
        }
    }

    private sealed class BoundedReadStream(Stream inner, long limit, ReadBudget budget) : Stream
    {
        private long _read;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));
        public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }

        private int Count(int read)
        {
            _read += read;
            if (_read > limit)
                throw Fail("A workbook part expands beyond the supported size.");
            budget.Consume(read);
            return read;
        }
    }
}

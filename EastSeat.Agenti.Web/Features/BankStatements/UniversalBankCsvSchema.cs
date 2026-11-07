using System.Security.Cryptography;
using System.Text.Json;

namespace EastSeat.Agenti.Web.Features.BankStatements;

public sealed class UniversalBankCsvSchema
{
    private readonly Dictionary<string, int> _columns;
    private readonly Dictionary<string, ColumnDefinition> _definitions;

    private UniversalBankCsvSchema(
        int version, string hash, IReadOnlyList<string> headers, Dictionary<string, ColumnDefinition> definitions)
    {
        Version = version;
        Hash = hash;
        Headers = headers;
        _definitions = definitions;
        _columns = headers.Select((header, index) => (header, index))
            .ToDictionary(item => item.header, item => item.index, StringComparer.Ordinal);
    }

    public int Version { get; }
    public string Hash { get; }
    public IReadOnlyList<string> Headers { get; }
    public ColumnDefinition Definition(string name) =>
        _definitions.TryGetValue(name, out var definition)
            ? definition
            : throw new InvalidOperationException($"Column '{name}' is missing from the universal bank CSV schema.");

    public int Column(string name) =>
        _columns.TryGetValue(name, out var index)
            ? index
            : throw new InvalidOperationException($"Column '{name}' is missing from the universal bank CSV schema.");

    public static UniversalBankCsvSchema Load()
    {
        var assembly = typeof(UniversalBankCsvSchema).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith("agenti-bank-statement-v1.json", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("The universal bank CSV schema resource is missing.");
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("The universal bank CSV schema could not be opened.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.GetProperty("headerRow").GetInt32() != 1)
            throw new InvalidOperationException("The universal bank CSV schema must put its header on row 1.");
        var version = root.GetProperty("version").GetInt32();
        var headers = root.GetProperty("headerOrder").EnumerateArray().Select(value => value.GetString()!)
            .ToArray();
        var columns = root.GetProperty("columns");
        if (version <= 0 || headers.Length == 0 || headers.Distinct(StringComparer.Ordinal).Count() != headers.Length ||
            headers.Any(header => !columns.TryGetProperty(header, out _)))
            throw new InvalidOperationException("The universal bank CSV schema has invalid version or column definitions.");
        var definitions = headers.ToDictionary(header => header, header =>
        {
            var definition = columns.GetProperty(header);
            return new ColumnDefinition(
                definition.GetProperty("type").GetString()!,
                definition.TryGetProperty("format", out var format) ? format.GetString() : null,
                definition.TryGetProperty("required", out var required) && required.GetBoolean(),
                definition.TryGetProperty("scale", out var scale) ? scale.GetInt32() : null,
                definition.TryGetProperty("allowThousandsSeparator", out var separators) && separators.GetBoolean());
        }, StringComparer.Ordinal);
        ValidateDefinition(definitions, "TransactionDate", "date", "yyyy-MM-dd");
        ValidateDefinition(definitions, "TransactionTime", "time", "HH:mm:ss");
        ValidateDefinition(definitions, "Reference", "string");
        ValidateDefinition(definitions, "Description", "string");
        ValidateDefinition(definitions, "Debit", "decimal", scale: 2);
        ValidateDefinition(definitions, "Credit", "decimal", scale: 2);
        ValidateDefinition(definitions, "RunningBalance", "decimal", scale: 2);
        return new UniversalBankCsvSchema(version, Convert.ToHexString(SHA256.HashData(bytes)), headers, definitions);
    }

    private static void ValidateDefinition(
        IReadOnlyDictionary<string, ColumnDefinition> definitions, string name, string type,
        string? format = null, int? scale = null)
    {
        var definition = definitions[name];
        if (definition.Type != type || definition.Format != format || definition.Scale != scale)
            throw new InvalidOperationException($"Column '{name}' has an unsupported type definition in the universal bank CSV schema.");
    }

    public sealed record ColumnDefinition(
        string Type, string? Format, bool Required, int? Scale, bool AllowThousandsSeparator);
}

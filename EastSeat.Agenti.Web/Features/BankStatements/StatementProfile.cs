using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Web.Features.BankStatements;

public sealed record StatementAccountContext(
    string AccountIdentifier, string Currency, string TimeZoneId, BankBalanceBasis BalanceBasis,
    DateOnly? CoverageFromDate = null, DateOnly? CoverageToDate = null, decimal? OpeningBaseline = null);

public sealed record ParsedStatement(
    string AccountIdentifier, string Currency, BankBalanceBasis BalanceBasis,
    DateTimeOffset CoverageFrom, DateTimeOffset CoverageTo, decimal OpeningBalance, decimal ClosingBalance,
    bool CompleteCoverage, IReadOnlyList<ParsedStatementEntry> Entries);

public sealed record ParsedStatementEntry(
    string SourceLocation, string RawData, string? TransactionNumber, string? Reference,
    string? CustomerIdentifier, string Narration, string Currency, decimal SignedAmount,
    decimal RunningBalance, DateTimeOffset OccurredAt, DateTimeOffset? PostedAt,
    DateOnly? ValueDate, bool HasPreciseTimestamp);

public interface IStatementImportProfile
{
    string Key { get; }
    string BankIdentifier { get; }
    string DisplayName { get; }
    int Version { get; }
    bool IsVerified { get; }
    BankBalanceBasis BalanceBasis { get; }
    IReadOnlyList<string> SupportedContentTypes { get; }
    string SchemaHash => string.Empty;
    Task<ParsedStatement> ParseAsync(Stream source, string contentType, StatementAccountContext account, CancellationToken cancellationToken);
    string? GetReliableIdentity(ParsedStatementEntry entry);
}

public sealed class StatementProfileCatalog(IEnumerable<IStatementImportProfile> profiles)
{
    private readonly Dictionary<string, IStatementImportProfile> _profiles =
        profiles.ToDictionary(p => p.Key, StringComparer.Ordinal);

    public IStatementImportProfile? Find(string? key) =>
        key != null && _profiles.TryGetValue(key, out var profile) ? profile : null;

    public IReadOnlyList<StatementProfileDto> GetProfiles() =>
        _profiles.Values.OrderBy(p => p.DisplayName)
            .Select(p => new StatementProfileDto(p.Key, p.DisplayName, p.Version, p.IsVerified, p.BalanceBasis,
                p.SupportedContentTypes.ToList())).ToList();
}

public sealed class StatementParsingException(string message) : Exception(message);

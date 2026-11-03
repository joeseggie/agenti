using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Web.Features.BankStatements;

public sealed class BankAccountForm
{
    public long WalletId { get; set; }
    public string BankName { get; set; } = string.Empty;
    public string AccountIdentifier { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = "Africa/Kampala";
    public BankBalanceBasis BalanceBasis { get; set; }
    public string? ImportProfileKey { get; set; }
}

public sealed record BankAccountDto(
    long Id, long WalletId, string WalletName, string BankName, string AccountIdentifier, string Currency,
    string TimeZoneId, BankBalanceBasis BalanceBasis, string? ImportProfileKey, bool ProfileVerified,
    bool WorkflowEnabled, bool IsActive, int Version);

public sealed record BankWalletOptionDto(long Id, string Name, string Currency, string AgentName);
public sealed record StatementProfileDto(
    string Key, string DisplayName, int Version, bool IsVerified, BankBalanceBasis BalanceBasis, List<string> SupportedContentTypes);
public sealed record BankAccountSetupDto(List<BankWalletOptionDto> Wallets, IReadOnlyList<StatementProfileDto> Profiles);
public sealed record ConfigureBankAccountRequest(BankAccountForm Account, int ExpectedVersion);

public interface IBankAccountService
{
    Task<List<BankAccountDto>> GetAccountsAsync(string userId, CancellationToken cancellationToken = default);
    Task<BankAccountSetupDto> GetSetupAsync(string userId, CancellationToken cancellationToken = default);
    Task<BankAccountDto> CreateAsync(string userId, BankAccountForm form, CancellationToken cancellationToken = default);
    Task<BankAccountDto> ConfigureAsync(string userId, long accountId, BankAccountForm form, int expectedVersion, CancellationToken cancellationToken = default);
    Task<BankAccountDto> SetWorkflowAsync(string userId, long accountId, bool enabled, int expectedVersion, CancellationToken cancellationToken = default);
}

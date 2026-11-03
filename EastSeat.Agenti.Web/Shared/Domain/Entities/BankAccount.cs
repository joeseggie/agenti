using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Shared.Domain.Entities;

public class BankAccount
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long WalletId { get; set; }
    public string BankName { get; set; } = string.Empty;
    public string? BankIdentifier { get; set; }
    public string AccountIdentifier { get; set; } = string.Empty;
    public string Currency { get; set; } = "UGX";
    public string TimeZoneId { get; set; } = "Africa/Kampala";
    public BankBalanceBasis BalanceBasis { get; set; }
    public string? ImportProfileKey { get; set; }
    public bool ImportProfileVerified { get; set; }
    public bool WorkflowEnabled { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Branch? Branch { get; set; }
    public Wallet? Wallet { get; set; }
}

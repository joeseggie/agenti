namespace EastSeat.Agenti.Shared.Domain.Enums;

/// <summary>
/// Represents the type of wallet in the system.
/// </summary>
public enum WalletTypeEnum
{
    Cash = 1,
    MobileMoney = 2,
    Bank = 3,
    Custom = 4
}

public static class WalletTypeClosingRules
{
    public static bool UsesEnteredClosingAmount(this WalletTypeEnum type) =>
        type is WalletTypeEnum.Cash or WalletTypeEnum.MobileMoney;
}

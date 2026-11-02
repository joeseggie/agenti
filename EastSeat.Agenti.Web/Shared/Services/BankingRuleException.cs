namespace EastSeat.Agenti.Shared.Services;

public class BankingRuleException(string message) : InvalidOperationException(message)
{
}

public sealed class BankingConflictException(string message) : BankingRuleException(message);

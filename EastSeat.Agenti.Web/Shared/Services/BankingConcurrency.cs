using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EastSeat.Agenti.Shared.Services;

public static class BankingConcurrency
{
    public static bool IsConflict(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is DbUpdateConcurrencyException ||
                current is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or
                    PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.UniqueViolation })
                return true;
        }
        return false;
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace EastSeat.Agenti.Shared.Services;

public static class BankingConcurrency
{
    public static async Task RollbackAfterConflictAsync(IDbContextTransaction? transaction)
    {
        if (transaction is null)
            return;

        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch
        {
            // Some providers complete the transaction before surfacing a serialization conflict.
        }
    }

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

using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.Users;

public class UserAuditCleanupService(IServiceProvider serviceProvider, ILogger<UserAuditCleanupService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run once shortly after startup, then daily
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
                var oldLogs = await db.UserAuditLogs
                    .Where(l => l.PerformedAt < cutoff)
                    .ToListAsync(stoppingToken);

                if (oldLogs.Count > 0)
                {
                    db.UserAuditLogs.RemoveRange(oldLogs);
                    await db.SaveChangesAsync(stoppingToken);
                    logger.LogInformation("UserAuditCleanupService removed {Count} old audit log entries.", oldLogs.Count);
                }
            }
            catch (TaskCanceledException)
            {
                // ignore on shutdown
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error running user audit cleanup");
            }

            // Sleep until next day
            await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
        }
    }
}

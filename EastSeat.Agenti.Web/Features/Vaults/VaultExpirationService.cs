using EastSeat.Agenti.Web.Features.Vaults;

namespace EastSeat.Agenti.Web.Features.Vaults;

/// <summary>
/// Background service that periodically expires pending vault transactions after 12 hours.
/// </summary>
public class VaultExpirationService(IServiceProvider serviceProvider, ILogger<VaultExpirationService> logger) : BackgroundService
{
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Vault Expiration Service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var vaultService = scope.ServiceProvider.GetRequiredService<IVaultService>();

                var expiredCount = await vaultService.ExpirePendingTransactionsAsync(stoppingToken);
                if (expiredCount > 0)
                {
                    logger.LogInformation("Expired {Count} pending vault transactions.", expiredCount);
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Vault Expiration Service is stopping.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while expiring vault transactions.");
                // Wait before retrying to avoid tight loop on errors
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        logger.LogInformation("Vault Expiration Service stopped.");
    }
}

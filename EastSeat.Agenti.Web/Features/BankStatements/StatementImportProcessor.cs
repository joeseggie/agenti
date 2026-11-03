namespace EastSeat.Agenti.Web.Features.BankStatements;

public sealed class StatementImportProcessor(
    IServiceScopeFactory scopeFactory, ILogger<StatementImportProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = false;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                processed = await scope.ServiceProvider.GetRequiredService<IBankStatementService>().ProcessNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError("Statement import worker failed with {ExceptionType}.", exception.GetType().Name);
            }
            if (!processed)
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}

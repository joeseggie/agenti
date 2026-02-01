using EastSeat.Agenti.Web.Features.Vaults;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace EastSeat.Agenti.UnitTests.BackgroundServices;

/// <summary>
/// Unit tests for <see cref="VaultExpirationService"/>.
/// </summary>
public class VaultExpirationServiceTests
{
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mock<IServiceScope> _serviceScopeMock;
    private readonly Mock<IServiceScopeFactory> _serviceScopeFactoryMock;
    private readonly Mock<IVaultService> _vaultServiceMock;
    private readonly Mock<ILogger<VaultExpirationService>> _loggerMock;
    private readonly VaultExpirationService _service;

    public VaultExpirationServiceTests()
    {
        _serviceProviderMock = new Mock<IServiceProvider>();
        _serviceScopeMock = new Mock<IServiceScope>();
        _serviceScopeFactoryMock = new Mock<IServiceScopeFactory>();
        _vaultServiceMock = new Mock<IVaultService>();
        _loggerMock = new Mock<ILogger<VaultExpirationService>>();

        // Setup service provider to return service scope factory
        _serviceProviderMock
            .Setup(x => x.GetService(typeof(IServiceScopeFactory)))
            .Returns(_serviceScopeFactoryMock.Object);

        // Setup service scope factory to create scopes
        _serviceScopeFactoryMock
            .Setup(x => x.CreateScope())
            .Returns(_serviceScopeMock.Object);

        // Setup service scope to return service provider
        var scopeServiceProviderMock = new Mock<IServiceProvider>();
        _serviceScopeMock
            .Setup(x => x.ServiceProvider)
            .Returns(scopeServiceProviderMock.Object);

        // Setup scoped service provider to return vault service
        scopeServiceProviderMock
            .Setup(x => x.GetService(typeof(IVaultService)))
            .Returns(_vaultServiceMock.Object);

        _service = new VaultExpirationService(_serviceProviderMock.Object, _loggerMock.Object);
    }

    #region Service Execution Tests

    [Fact]
    public async Task StartAsync_LogsServiceStarted()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        _vaultServiceMock
            .Setup(x => x.ExpirePendingTransactionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0)
            .Callback(() => cts.Cancel()); // Stop after first iteration

        // Act
        await _service.StartAsync(cts.Token);
        await Task.Delay(100); // Give it time to log

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Vault Expiration Service started")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // Cleanup
        await _service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_CallsExpirePendingTransactionsAsync()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var callCount = 0;

        _vaultServiceMock
            .Setup(x => x.ExpirePendingTransactionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0)
            .Callback(() =>
            {
                callCount++;
                if (callCount >= 2) // Stop after 2 calls
                {
                    cts.Cancel();
                }
            });

        // Act
        await _service.StartAsync(cts.Token);
        await Task.Delay(6000); // Wait for at least one check interval (5 minutes simulated)

        try
        {
            await cts.Token.WaitHandle.WaitOneAsync();
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert
        _vaultServiceMock.Verify(
            x => x.ExpirePendingTransactionsAsync(It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);

        // Cleanup
        await _service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTransactionsExpired_LogsExpiredCount()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var expiredCount = 5;

        _vaultServiceMock
            .Setup(x => x.ExpirePendingTransactionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expiredCount)
            .Callback(() => cts.Cancel()); // Stop after first iteration

        // Act
        await _service.StartAsync(cts.Token);
        await Task.Delay(500); // Give it time to execute and log

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Expired {expiredCount} pending vault transactions")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // Cleanup
        await _service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoTransactionsExpired_DoesNotLogExpiredCount()
    {
        // Arrange
        var cts = new CancellationTokenSource();

        _vaultServiceMock
            .Setup(x => x.ExpirePendingTransactionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0)
            .Callback(() => cts.Cancel()); // Stop after first iteration

        // Act
        await _service.StartAsync(cts.Token);
        await Task.Delay(500); // Give it time to execute

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Expired")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);

        // Cleanup
        await _service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_LogsServiceStopping()
    {
        // Arrange
        var cts = new CancellationTokenSource();

        _vaultServiceMock
            .Setup(x => x.ExpirePendingTransactionsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        await _service.StartAsync(cts.Token);
        await Task.Delay(100);
        await _service.StopAsync(CancellationToken.None);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Vault Expiration Service is stopping")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WhenExceptionOccurs_LogsErrorAndContinues()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var exception = new Exception("Test exception");

        _vaultServiceMock
            .Setup(x => x.ExpirePendingTransactionsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception)
            .Callback(() => cts.Cancel()); // Stop after first exception

        // Act
        await _service.StartAsync(cts.Token);
        await Task.Delay(1000); // Wait for error handling

        // Assert - Exception is logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("An error occurred while expiring vault transactions")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // Assert - Service called ExpirePendingTransactionsAsync at least once
        _vaultServiceMock.Verify(
            x => x.ExpirePendingTransactionsAsync(It.IsAny<CancellationToken>()),
            Times.Once);

        // Cleanup
        await _service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_CreatesNewScopeForEachIteration()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var callCount = 0;

        _vaultServiceMock
            .Setup(x => x.ExpirePendingTransactionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0)
            .Callback(() =>
            {
                callCount++;
                if (callCount >= 2) // Stop after 2 calls
                {
                    cts.Cancel();
                }
            });

        // Act
        await _service.StartAsync(cts.Token);
        await Task.Delay(6000); // Wait for multiple iterations

        try
        {
            await cts.Token.WaitHandle.WaitOneAsync();
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert - Should create a new scope for each iteration
        _serviceScopeFactoryMock.Verify(
            x => x.CreateScope(),
            Times.AtLeastOnce);

        // Cleanup
        await _service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_LogsServiceStopped()
    {
        // Arrange
        var cts = new CancellationTokenSource();

        _vaultServiceMock
            .Setup(x => x.ExpirePendingTransactionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        await _service.StartAsync(cts.Token);
        await Task.Delay(100);
        await _service.StopAsync(CancellationToken.None);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Vault Expiration Service stopped")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion
}

/// <summary>
/// Extension methods for testing async wait handles.
/// </summary>
internal static class WaitHandleExtensions
{
    public static Task WaitOneAsync(this WaitHandle waitHandle, CancellationToken cancellationToken = default)
    {
        if (waitHandle == null)
            throw new ArgumentNullException(nameof(waitHandle));

        var tcs = new TaskCompletionSource<bool>();
        var rwh = ThreadPool.RegisterWaitForSingleObject(waitHandle,
            (state, timedOut) => { tcs.TrySetResult(!timedOut); }, null, -1, true);

        var t = tcs.Task;
        t.ContinueWith(_ => rwh.Unregister(null));

        if (cancellationToken != default)
        {
            cancellationToken.Register(() => tcs.TrySetCanceled());
        }

        return t;
    }
}

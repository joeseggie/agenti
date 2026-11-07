using System.Security.Claims;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.Web.Components.Pages;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging.Abstractions;

namespace EastSeat.Agenti.UnitTests.BankReconciliation;

public class BankingPageBaseTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_BusyOperation_RunsOnlyLatestReloadEvenAfterFailure(bool failOperation)
    {
        var page = new TestPage();
        var releaseOperation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reloadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executed = new List<string>();
        var operation = page.RunOperationAsync(async () =>
        {
            executed.Add("operation");
            await releaseOperation.Task;
            if (failOperation) throw new BankingRuleException("Operation failed.");
        });

        Assert.False(await page.RunOperationAsync(() =>
        {
            executed.Add("superseded reload");
            return Task.CompletedTask;
        }, queueIfBusy: true));
        Assert.False(await page.RunOperationAsync(async () =>
        {
            executed.Add("latest reload");
            reloadStarted.SetResult();
            await releaseReload.Task;
        }, queueIfBusy: true));

        releaseOperation.SetResult();
        await reloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(page.IsBusy);
        Assert.False(operation.IsCompleted);
        Assert.False(await page.RunOperationAsync(() =>
        {
            executed.Add("unexpected concurrent action");
            return Task.CompletedTask;
        }));
        releaseReload.SetResult();

        Assert.Equal(!failOperation, await operation);
        Assert.Equal(new[] { "operation", "latest reload" }, executed);
        Assert.Equal(failOperation ? "Operation failed." : null, page.Message);
    }

    [Fact]
    public async Task RunAsync_QueuedLookupFails_SurfacesErrorAndAllowsRetry()
    {
        var page = new TestPage();
        var releaseOperation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = page.RunOperationAsync(() => releaseOperation.Task);
        Assert.False(await page.RunOperationAsync(
            () => throw new BankingRuleException("Lookup failed."), queueIfBusy: true));
        releaseOperation.SetResult();

        Assert.True(await operation);
        Assert.Equal("Lookup failed.", page.Message);
        Assert.True(await page.RunOperationAsync(() => Task.CompletedTask, queueIfBusy: true));
        Assert.Null(page.Message);
    }

    private sealed class TestPage : BankingPageBase
    {
        public TestPage()
        {
            Authentication = new TestAuthentication();
            Log = NullLogger<BankingPageBase>.Instance;
        }

        public bool IsBusy => Busy;
        public string? Message => Error;
        public Task<bool> RunOperationAsync(Func<Task> action, bool queueIfBusy = false) =>
            RunAsync(action, queueIfBusy);
    }

    private sealed class TestAuthentication : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "test-admin")], "Test"))));
    }
}

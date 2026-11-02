using System.Text;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.UnitTests.Helpers;
using EastSeat.Agenti.Web.Features.BankStatements;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EastSeat.Agenti.UnitTests.BankReconciliation;

public class BankEvidenceTests
{
    [Fact]
    public async Task Upload_RetryReturnsOriginalAndRejectsChangedContent()
    {
        using var fixture = new BankingTestContext();
        var service = CreateService(fixture);
        var key = Guid.NewGuid();
        var first = await service.UploadAsync(fixture.Admin.Id, fixture.Account.Id, key,
            "statement.csv", true, Csv("original"));
        var replay = await service.UploadAsync(fixture.Admin.Id, fixture.Account.Id, key,
            "statement.csv", true, Csv("original"));

        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(1, await fixture.Db.BankEvidenceFiles.CountAsync());
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.UploadAsync(
            fixture.Admin.Id, fixture.Account.Id, key, "statement.csv", true, Csv("changed")));
    }

    [Fact]
    public async Task Agent_CannotUploadOrDownloadStatements()
    {
        using var fixture = new BankingTestContext();
        var service = CreateService(fixture);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.UploadAsync(
            fixture.User.Id, fixture.Account.Id, Guid.NewGuid(), "statement.csv", true, Csv("rows")));
        var uploaded = await service.UploadAsync(fixture.Admin.Id, fixture.Account.Id, Guid.NewGuid(),
            "statement.csv", true, Csv("rows"));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.DownloadAsync(fixture.User.Id, uploaded.Id));
    }

    [Fact]
    public async Task Receipt_RemainsAvailableToOwningAgentWithoutStatementAccess()
    {
        using var fixture = new BankingTestContext();
        var service = CreateService(fixture);
        var uploaded = await service.UploadAsync(fixture.User.Id, fixture.Account.Id, Guid.NewGuid(),
            "receipt.pdf", false, Csv("%PDF-synthetic"));

        var downloaded = await service.DownloadAsync(fixture.User.Id, uploaded.Id);

        Assert.Equal("application/pdf", downloaded.Info.ContentType);
        Assert.Equal("%PDF-synthetic", Encoding.UTF8.GetString(downloaded.Content));
    }

    [Fact]
    public async Task Upload_RejectsOversizedEvidenceBeforePersistence()
    {
        using var fixture = new BankingTestContext();
        var service = CreateService(fixture, 4);

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.UploadAsync(
            fixture.Admin.Id, fixture.Account.Id, Guid.NewGuid(), "statement.csv", true, Csv("too large")));
        Assert.Equal(0, await fixture.Db.BankEvidenceFiles.CountAsync());
    }

    private static MemoryStream Csv(string content) => new(Encoding.UTF8.GetBytes(content));

    private static BankEvidenceService CreateService(BankingTestContext fixture, long limit = 1024)
    {
        var clock = new BusinessClock(TimeProvider.System);
        return new BankEvidenceService(fixture.Db, new BankingAccessService(fixture.Db, clock), clock,
            Options.Create(new BankEvidenceOptions { MaxFileBytes = limit }));
    }
}

using EastSeat.Agenti.Android.Models;
using EastSeat.Agenti.Web.Features.Api;
using EastSeat.Agenti.Web.Features.CashCounts;
using Microsoft.AspNetCore.Http;

namespace EastSeat.Agenti.UnitTests.Api;

public class CashCountApiContractTests
{
    [Theory]
    [InlineData(null, 426)]
    [InlineData(1, 426)]
    [InlineData(2, 200)]
    [InlineData(3, 426)]
    public void Form_RequiresSupportedContractForEnteredBalances(int? version, int status)
    {
        var form = Form(1);
        form.WalletEntries[0].UsesEnteredClosingAmount = true;
        var result = CashCountApiContract.Form(form, version);
        Assert.Equal(status, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Equal(version == 2 ? 2 : null, form.ApiContractVersion);
    }

    [Fact]
    public void Form_UnchangedExpectedBalanceRemainsReadableForOldClients()
    {
        Assert.Equal(200, ((IStatusCodeHttpResult)CashCountApiContract.Form(Form(1), null)).StatusCode);
    }

    [Fact]
    public void DraftRefresh_UsesCurrentWalletMetadataAndKeepsOnlyMatchingEdits()
    {
        var old = Form(1, 2);
        old.WalletEntries[0].ExpectedBalance = 12;
        old.WalletEntries[0].CountedAmount = 15;
        var draft = new BankingDraft<CashCountFormModel> { Form = old, Text = new()
        {
            ["amount-1"] = "16", ["observed-2"] = "old", ["amount-3"] = "orphan"
        } };
        var server = Form(1, 3);
        server.WalletEntries[0].ExpectedBalance = 30;
        server.WalletEntries[0].UsesEnteredClosingAmount = true;
        server.WalletEntries[1].CountedAmount = 50;

        CashCountDraftMerger.Refresh(draft, server);

        Assert.Same(server, draft.Form);
        Assert.True(draft.Form.WalletEntries[0].UsesEnteredClosingAmount);
        Assert.Equal(15, draft.Form.WalletEntries[0].CountedAmount);
        Assert.Equal(50, draft.Form.WalletEntries[1].CountedAmount);
        Assert.Equal("16", draft.Text["amount-1"]);
        Assert.DoesNotContain("observed-2", draft.Text.Keys);
        Assert.DoesNotContain("amount-3", draft.Text.Keys);
    }

    [Fact]
    public void DraftRefresh_DifferentSessionOrCountDiscardsStaleEdits()
    {
        var old = Form(1);
        old.CashSessionId = 4;
        old.WalletEntries[0].CountedAmount = 100;
        var draft = new BankingDraft<CashCountFormModel> { Form = old, Text = new() { ["amount-1"] = "100" } };
        var server = Form(1);
        server.CashSessionId = 5;
        server.WalletEntries[0].CountedAmount = 10;

        CashCountDraftMerger.Refresh(draft, server);

        Assert.Equal(10, draft.Form.WalletEntries[0].CountedAmount);
        Assert.Empty(draft.Text);
    }

    private static CashCountFormModel Form(params long[] ids) => new()
    {
        CashSessionId = 1, CashCountId = 2, CountDate = new DateOnly(2026, 1, 1),
        WalletEntries = ids.Select(id => new WalletCountEntryDto { WalletId = id }).ToList()
    };
}

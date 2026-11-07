using System.Security.Claims;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.Web.Features.BankReconciliation;
using EastSeat.Agenti.Web.Features.BankRuns;
using EastSeat.Agenti.Web.Features.BankStatements;
using EastSeat.Agenti.Web.Features.CashCounts;
using EastSeat.Agenti.Web.Features.CustomerTransactions;
using EastSeat.Agenti.Web.Features.WalletAdjustments;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace EastSeat.Agenti.Web.Features.Api;

public static class BankingEndpoints
{
    public static RouteGroupBuilder MapBankingApi(this RouteGroupBuilder group)
    {
        group.RequireAuthorization("BankingApi").AddEndpointFilter<BankingApiExceptionFilter>();
        group.MapGet("/capabilities", (IOptions<BankEvidenceOptions> options) =>
            Ok(new BankingCapabilitiesDto(1, options.Value.MaxFileBytes, "Africa/Kampala")));
        group.MapGet("/current", async (ClaimsPrincipal principal, ICashCountService service) =>
            Ok(await service.GetCurrentSessionAsync(UserId(principal))));
        group.MapGet("/counts/initialize", async (bool isOpening, ClaimsPrincipal principal, ICashCountService service) =>
            Ok(await service.InitializeCashCountFormAsync(UserId(principal), isOpening)));
        group.MapGet("/counts/{id:long}", async (long id, ClaimsPrincipal principal, ICashCountService service) =>
        {
            var form = await service.GetCashCountFormAsync(UserId(principal), id);
            return form == null ? Results.NotFound(ApiResponse<CashCountFormModel>.Fail("Cash count not found.")) : Ok(form);
        });
        group.MapPost("/counts", async (CashCountFormModel form, ClaimsPrincipal principal, ICashCountService service) =>
            CountResult(await service.SaveCashCountAsync(UserId(principal), form)));
        group.MapPost("/counts/{id:long}/submit", async (long id, ClaimsPrincipal principal, ICashCountService service) =>
            CountResult(await service.SubmitCashCountAsync(UserId(principal), id)));
        group.MapGet("/counts/pending", async (ClaimsPrincipal principal, IBankingAccessService access, ICashCountService service, CancellationToken token) =>
        {
            var actor = await access.GetActorAsync(UserId(principal), token);
            if (actor.Role is not (EastSeat.Agenti.Shared.Domain.Enums.UserRole.Admin or EastSeat.Agenti.Shared.Domain.Enums.UserRole.Supervisor))
                throw new UnauthorizedAccessException();
            return Ok(await service.GetPendingApprovalsAsync(actor.BranchId));
        });
        group.MapPost("/counts/{id:long}/approve", async (long id, ClaimsPrincipal principal, ICashCountService service) =>
            CountResult(await service.ApproveCashCountAsync(UserId(principal), id)));
        group.MapPost("/counts/{id:long}/reject", async (long id, ReviewReasonRequest form, ClaimsPrincipal principal, ICashCountService service) =>
            CountResult(await service.RejectCashCountAsync(UserId(principal), id, form.Reason)));

        group.MapGet("/customer-options/{sessionId:long}", async (long sessionId, ClaimsPrincipal principal, ICustomerTransactionService service, CancellationToken token) =>
            Ok(await service.GetCaptureOptionsAsync(UserId(principal), sessionId, token)));
        group.MapGet("/transactions/session/{sessionId:long}", async (long sessionId, ClaimsPrincipal principal, ICustomerTransactionService service, CancellationToken token) =>
            Ok(await service.GetForSessionAsync(UserId(principal), sessionId, token)));
        group.MapGet("/transactions/cases", async (ClaimsPrincipal principal, ICustomerTransactionService service, CancellationToken token) =>
            Ok(await service.GetOpenCasesAsync(UserId(principal), token)));
        group.MapGet("/transactions/{id:long}", async (long id, ClaimsPrincipal principal, ICustomerTransactionService service, CancellationToken token) =>
            Ok(await service.GetAsync(UserId(principal), id, token)));
        group.MapPost("/transactions", async (CustomerTransactionForm form, ClaimsPrincipal principal, ICustomerTransactionService service, CancellationToken token) =>
            Ok(await service.CaptureAsync(UserId(principal), form, token)));
        group.MapPost("/transactions/{id:long}/follow-ups", async (long id, CustomerFollowUpForm form, ClaimsPrincipal principal, ICustomerTransactionService service, CancellationToken token) =>
            Ok(await service.RecordFollowUpAsync(UserId(principal), id, form, token)));
        group.MapPost("/transactions/correct", async (CustomerMovementCorrectionForm form, ClaimsPrincipal principal, ICustomerTransactionService service, CancellationToken token) =>
            Ok(await service.CorrectAsync(UserId(principal), form, token)));
        group.MapGet("/corrections", async (ClaimsPrincipal principal, IBankReconciliationService service, CancellationToken token) =>
            Ok(await service.GetMyCorrectionsAsync(UserId(principal), token)));
        group.MapGet("/corrections/{id:long}/options", async (long id, ClaimsPrincipal principal, ICustomerTransactionService service, CancellationToken token) =>
            Ok(await service.GetCorrectionOptionsAsync(UserId(principal), id, token)));
        group.MapPost("/corrections/{id:long}/submit", async (long id, ResubmitBankCorrectionsRequest form,
            ClaimsPrincipal principal, ICustomerTransactionService service, CancellationToken token) =>
        {
            await service.ResubmitCorrectionsAsync(UserId(principal), id, form.ExpectedVersion, token);
            return Ok("Corrections submitted for review.");
        });

        group.MapGet("/bank-runs", async (ClaimsPrincipal principal, IBankingAccessService access, IBankRunService service, CancellationToken token) =>
        {
            await access.GetActorAsync(UserId(principal), token);
            return Ok(await service.GetBankRunsForAgentAsync(UserId(principal)));
        });
        group.MapGet("/bank-runs/wallet-types", async (ClaimsPrincipal principal, IBankingAccessService access, IBankRunService service, CancellationToken token) =>
        {
            await access.GetActorAsync(UserId(principal), token);
            return Ok(await service.GetAgentNonCashWalletTypesAsync(UserId(principal)));
        });
        group.MapPost("/bank-runs", async (BankRunFormModel form, ClaimsPrincipal principal, IBankingAccessService access, IBankRunService service, CancellationToken token) =>
        {
            await access.GetActorAsync(UserId(principal), token);
            var result = await service.RecordBankRunAsync(UserId(principal), form);
            return result.Success ? Ok(result) : Results.BadRequest(ApiResponse<BankRunSaveResult>.Fail(result.ErrorMessage ?? "Bank run could not be recorded."));
        });
        group.MapPost("/wallet-adjustments", async (WalletAdjustmentFormModel form, ClaimsPrincipal principal,
            IBankingAccessService access, IWalletAdjustmentService service, CancellationToken token) =>
        {
            await access.GetActorAsync(UserId(principal), token);
            var result = await service.RecordAdjustmentAsync(UserId(principal), form);
            return result.Success ? Ok(result) : Results.BadRequest(ApiResponse<WalletAdjustmentSaveResult>.Fail(result.ErrorMessage ?? "Adjustment could not be recorded."));
        });
        group.MapGet("/wallet-adjustments/pending", async (ClaimsPrincipal principal, IBankingAccessService access,
            IWalletAdjustmentService service, CancellationToken token) =>
        {
            var actor = await access.GetActorAsync(UserId(principal), token);
            if (actor.Role is not (EastSeat.Agenti.Shared.Domain.Enums.UserRole.Admin or EastSeat.Agenti.Shared.Domain.Enums.UserRole.Supervisor))
                throw new UnauthorizedAccessException();
            return Ok(await service.GetPendingAdjustmentsAsync(actor.BranchId));
        });
        group.MapPost("/wallet-adjustments/{id:long}/approve", async (long id, ClaimsPrincipal principal,
            IWalletAdjustmentService service) =>
        {
            var result = await service.ApproveAdjustmentAsync(UserId(principal), id);
            return result.Success ? Ok(result) : Results.BadRequest(ApiResponse<WalletAdjustmentSaveResult>.Fail(result.ErrorMessage ?? "Adjustment could not be approved."));
        }).RequireAuthorization("CashCountApproveApi");
        group.MapPost("/wallet-adjustments/{id:long}/reject", async (long id, ReviewReasonRequest form,
            ClaimsPrincipal principal, IWalletAdjustmentService service) =>
        {
            var result = await service.RejectAdjustmentAsync(UserId(principal), id, form.Reason);
            return result.Success ? Ok(result) : Results.BadRequest(ApiResponse<WalletAdjustmentSaveResult>.Fail(result.ErrorMessage ?? "Adjustment could not be rejected."));
        }).RequireAuthorization("CashCountApproveApi");

        group.MapPost("/evidence", UploadEvidenceAsync);
        group.MapGet("/evidence/{id:long}", DownloadEvidenceAsync);

        return group;
    }

    public static void MapBankingEvidence(this WebApplication app) =>
        app.MapGet("/banking-evidence/{id:long}", DownloadEvidenceAsync)
            .RequireAuthorization().AddEndpointFilter<BankingApiExceptionFilter>();

    private static async Task<IResult> UploadEvidenceAsync(
        HttpContext context, IBankEvidenceService service, IOptions<BankEvidenceOptions> options, CancellationToken cancellationToken)
    {
        var maximum = options.Value.MaxFileBytes + 64 * 1024;
        if (context.Request.ContentLength > maximum)
            return Results.Json(ApiResponse<object>.Fail("The evidence upload exceeds the configured size limit."),
                statusCode: StatusCodes.Status413PayloadTooLarge);
        var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false })
            sizeFeature.MaxRequestBodySize = maximum;
        if (!context.Request.HasFormContentType)
            return Results.BadRequest(ApiResponse<BankEvidenceInfoDto>.Fail("Upload one multipart evidence file."));
        var form = await context.Request.ReadFormAsync(cancellationToken);
        if (form.Files.Count != 1 || !long.TryParse(form["bankAccountId"], out var accountId) ||
            !Guid.TryParse(form["uploadKey"], out var uploadKey) || !bool.TryParse(form["isStatement"], out var isStatement))
            return Results.BadRequest(ApiResponse<BankEvidenceInfoDto>.Fail("Account, upload identity, file purpose and one file are required."));
        var file = form.Files[0];
        await using var stream = file.OpenReadStream();
        return Ok(await service.UploadAsync(UserId(context.User), accountId, uploadKey, file.FileName,
            isStatement, stream, cancellationToken));
    }

    private static async Task<IResult> DownloadEvidenceAsync(
        long id, HttpContext context, IBankEvidenceService service, CancellationToken cancellationToken)
    {
        var evidence = await service.DownloadAsync(UserId(context.User), id, cancellationToken);
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.File(evidence.Content, evidence.Info.ContentType, evidence.Info.FileName);
    }

    private static string UserId(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? principal.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)
        ?? throw new UnauthorizedAccessException("An authenticated user identity is required.");
    private static IResult Ok<T>(T value) => Results.Ok(ApiResponse<T>.Ok(value));
    private static IResult CountResult(CashCountSaveResult result) => result.Success
        ? Ok(result) : Results.BadRequest(ApiResponse<CashCountSaveResult>.Fail(result.ErrorMessage ?? "The cash count could not be processed."));
}

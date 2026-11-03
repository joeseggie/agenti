using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.Web.Features.BankStatements;

namespace EastSeat.Agenti.Web.Features.Api;

public sealed class BankingApiExceptionFilter(ILogger<BankingApiExceptionFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Json(ApiResponse<object>.Fail("You do not have permission to access this banking resource."),
                statusCode: StatusCodes.Status403Forbidden);
        }
        catch (BankingConflictException exception)
        {
            return Results.Conflict(ApiResponse<object>.Fail(exception.Message));
        }
        catch (BankingRuleException exception)
        {
            return Results.BadRequest(ApiResponse<object>.Fail(exception.Message));
        }
        catch (StatementParsingException exception)
        {
            return Results.BadRequest(ApiResponse<object>.Fail(exception.Message));
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(ApiResponse<object>.Fail("The request contains an invalid value."));
        }
        catch (BadHttpRequestException exception)
        {
            return Results.Json(ApiResponse<object>.Fail(exception.StatusCode == StatusCodes.Status413PayloadTooLarge
                    ? "The upload exceeds the configured request size limit." : "The request body is invalid."),
                statusCode: exception.StatusCode);
        }
        catch (Exception exception) when (BankingConcurrency.IsConflict(exception))
        {
            return Results.Conflict(ApiResponse<object>.Fail("A concurrent request changed this record. Refresh and retry using the same request identity."));
        }
        catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError("Banking endpoint {Endpoint} failed with {ExceptionType}.",
                context.HttpContext.GetEndpoint()?.DisplayName, exception.GetType().Name);
            return Results.Json(ApiResponse<object>.Fail("The banking operation could not be completed. Refresh its status before retrying."),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}

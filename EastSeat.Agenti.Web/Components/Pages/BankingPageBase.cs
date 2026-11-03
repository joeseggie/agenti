using System.Globalization;
using System.Security.Claims;
using EastSeat.Agenti.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace EastSeat.Agenti.Web.Components.Pages;

public abstract class BankingPageBase : ComponentBase
{
    [Inject] protected AuthenticationStateProvider Authentication { get; set; } = null!;
    [Inject] protected ILogger<BankingPageBase> Log { get; set; } = null!;
    private bool _busy;
    protected string UserId { get; private set; } = string.Empty;
    protected bool IsAdmin { get; private set; }
    // Prerendered markup has no event handlers; keep inputs disabled until the circuit is interactive so typed data is not discarded.
    protected bool Busy => _busy || !RendererInfo.IsInteractive;
    protected bool Loaded { get; private set; }
    protected string? Error { get; private set; }

    protected async Task<bool> RunAsync(Func<Task> action)
    {
        if (_busy)
            return false;
        _busy = true;
        Error = null;
        try
        {
            if (string.IsNullOrEmpty(UserId))
            {
                var auth = await Authentication.GetAuthenticationStateAsync();
                UserId = auth.User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? throw new UnauthorizedAccessException();
                IsAdmin = auth.User.IsInRole("Admin");
            }
            await action();
            Loaded = true;
            return true;
        }
        catch (BankingRuleException exception)
        {
            Error = exception.Message;
        }
        catch (UnauthorizedAccessException)
        {
            Error = "You do not have access to this banking operation.";
        }
        catch (Exception exception) when (BankingConcurrency.IsConflict(exception))
        {
            Error = "Another banking operation changed these records. Refresh the status, then retry using the retained form.";
        }
        catch (Exception exception)
        {
            Log.LogError("Banking page {Page} failed with {ExceptionType}.", GetType().Name, exception.GetType().Name);
            Error = "The operation could not be completed. Your form has been retained; refresh its status before retrying.";
        }
        finally
        {
            _busy = false;
        }
        return false;
    }

    protected static string UgandaTime(DateTimeOffset value) =>
        BusinessTimeText.Format(value);

    protected static DateTimeOffset ParseUgandaTime(string value)
    {
        return BusinessTimeText.Parse(value);
    }

    protected static DateOnly ParseDate(string value)
    {
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new BankingRuleException("Enter the date as yyyy-MM-dd.");
        return date;
    }
}

using System.Text.Json;
using EastSeat.Agenti.Web.Data;
using Microsoft.ApplicationInsights;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Features.Users;

public class LoginTelemetryService(
    ApplicationDbContext db,
    ILogger<LoginTelemetryService> logger,
    TelemetryClient? telemetryClient = null) : ILoginTelemetryService
{
    public async Task RecordLoginSuccessAsync(
        string userId,
        string email,
        string loginMethod,
        string? ipAddress,
        string? userAgent,
        string? role = null,
        long? branchId = null,
        double? durationMs = null)
    {
        db.UserAuditLogs.Add(new UserAuditLog
        {
            UserId = userId,
            Action = UserAuditAction.LoginSuccess,
            OldValue = SerializeContext(ipAddress, userAgent, loginMethod),
            NewValue = $"Role={role}, BranchId={branchId}",
            PerformedByUserId = null,
            PerformedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();

        var properties = new Dictionary<string, string>
        {
            ["UserId"] = userId,
            ["Email"] = email,
            ["LoginMethod"] = loginMethod,
            ["IpAddress"] = ipAddress ?? "unknown",
            ["UserAgent"] = Truncate(userAgent, 200),
            ["Role"] = role ?? "unknown",
            ["BranchId"] = branchId?.ToString() ?? "none"
        };

        telemetryClient?.TrackEvent("login_succeeded", properties);

        if (durationMs.HasValue)
        {
            telemetryClient?.TrackMetric("login_duration_ms", durationMs.Value);
        }

        logger.LogInformation(
            "Login succeeded for {Email} via {LoginMethod} from {IpAddress}",
            email, loginMethod, ipAddress ?? "unknown");
    }

    public async Task RecordLoginFailureAsync(
        string email,
        string failureReason,
        string loginMethod,
        string? ipAddress,
        string? userAgent,
        double? durationMs = null)
    {
        // For failed logins we may not have a userId, so we store email as the identifier.
        // Find the user by email to get userId for the audit log, if they exist.
        var user = await db.Users
            .Where(u => u.Email == email)
            .Select(u => new { u.Id })
            .FirstOrDefaultAsync();

        if (user is not null)
        {
            db.UserAuditLogs.Add(new UserAuditLog
            {
                UserId = user.Id,
                Action = UserAuditAction.LoginFailed,
                OldValue = SerializeContext(ipAddress, userAgent, loginMethod),
                NewValue = failureReason,
                PerformedByUserId = null,
                PerformedAt = DateTimeOffset.UtcNow
            });

            await db.SaveChangesAsync();
        }

        var properties = new Dictionary<string, string>
        {
            ["Email"] = email,
            ["FailureReason"] = failureReason,
            ["LoginMethod"] = loginMethod,
            ["IpAddress"] = ipAddress ?? "unknown",
            ["UserAgent"] = Truncate(userAgent, 200)
        };

        telemetryClient?.TrackEvent("login_failed", properties);

        if (durationMs.HasValue)
        {
            telemetryClient?.TrackMetric("login_duration_ms", durationMs.Value);
        }

        logger.LogWarning(
            "Login failed for {Email} via {LoginMethod} from {IpAddress}: {FailureReason}",
            email, loginMethod, ipAddress ?? "unknown", failureReason);
    }

    public void RecordTokenIssued(string userId, string email, string loginMethod, int expiryMinutes)
    {
        var properties = new Dictionary<string, string>
        {
            ["UserId"] = userId,
            ["Email"] = email,
            ["LoginMethod"] = loginMethod,
            ["ExpiryMinutes"] = expiryMinutes.ToString()
        };

        telemetryClient?.TrackEvent("jwt_token_issued", properties);
    }

    private static string SerializeContext(string? ipAddress, string? userAgent, string loginMethod)
    {
        var context = new { IpAddress = ipAddress ?? "unknown", UserAgent = Truncate(userAgent, 200), LoginMethod = loginMethod };
        return JsonSerializer.Serialize(context);
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return "unknown";
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}

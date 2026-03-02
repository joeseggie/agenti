namespace EastSeat.Agenti.Web.Features.Users;

public interface ILoginTelemetryService
{
    Task RecordLoginSuccessAsync(string userId, string email, string loginMethod, string? ipAddress, string? userAgent, string? role = null, long? branchId = null, double? durationMs = null);
    Task RecordLoginFailureAsync(string email, string failureReason, string loginMethod, string? ipAddress, string? userAgent, double? durationMs = null);
    void RecordTokenIssued(string userId, string email, string loginMethod, int expiryMinutes);
}

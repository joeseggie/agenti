using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.Users;

namespace EastSeat.Agenti.Web.Features.Api;

/// <summary>
/// API endpoints for authentication.
/// </summary>
public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthApi(this RouteGroupBuilder group)
    {
        group.MapPost("/login", async (
            LoginRequest request,
            UserManager<ApplicationUser> userManager,
            IConfiguration configuration,
            ILogger<Program> logger,
            ILoginTelemetryService loginTelemetry,
            HttpContext httpContext) =>
        {
            var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = httpContext.Request.Headers.UserAgent.ToString();
            const string loginMethod = "ApiJwt";

            var stopwatch = Stopwatch.StartNew();

            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                stopwatch.Stop();
                await loginTelemetry.RecordLoginFailureAsync(
                    request.Email ?? "unknown", "ValidationFailed", loginMethod, ipAddress, userAgent,
                    stopwatch.Elapsed.TotalMilliseconds);
                return Results.BadRequest(ApiResponse<LoginResponse>.Fail("Email and password are required."));
            }

            var user = await userManager.FindByEmailAsync(request.Email);

            // Mitigate timing-based user enumeration by performing a dummy password verification
            // even when the user is not found.
            bool passwordValid;
            if (user is not null)
            {
                passwordValid = await userManager.CheckPasswordAsync(user, request.Password);
            }
            else
            {
                // Use a constant dummy hash to consume roughly the same amount of time as a real check.
                // This value should be any valid Identity password hash; the result is intentionally ignored.
                const string dummyHash = "AQAAAAEAACcQAAAAEJ1nK1z0zSxN1W/7M7lT9BMG3c0kF4xHCV2+DummyHashForTimingOnly==";
                var dummyUser = new ApplicationUser();
                _ = userManager.PasswordHasher.VerifyHashedPassword(dummyUser, dummyHash, request.Password);
                passwordValid = false;
            }

            stopwatch.Stop();
            var durationMs = stopwatch.Elapsed.TotalMilliseconds;

            if (user is null)
            {
                await loginTelemetry.RecordLoginFailureAsync(
                    request.Email, "UserNotFound", loginMethod, ipAddress, userAgent, durationMs);
                return Results.Unauthorized();
            }

            if (!user.IsActive)
            {
                logger.LogWarning("Login attempt for inactive account: {Email}", request.Email);
                await loginTelemetry.RecordLoginFailureAsync(
                    request.Email, "InactiveAccount", loginMethod, ipAddress, userAgent, durationMs);
                return Results.Unauthorized();
            }

            if (!passwordValid)
            {
                await loginTelemetry.RecordLoginFailureAsync(
                    request.Email, "InvalidCredentials", loginMethod, ipAddress, userAgent, durationMs);
                return Results.Unauthorized();
            }

            var jwtKey = configuration["Jwt:Key"];
            if (string.IsNullOrWhiteSpace(jwtKey))
            {
                logger.LogError("JWT key is not configured. Cannot generate authentication token.");
                return Results.Json(
                    ApiResponse<LoginResponse>.Fail("Authentication is temporarily unavailable. Please contact support."),
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            var token = GenerateJwtToken(user, configuration);

            await loginTelemetry.RecordLoginSuccessAsync(
                user.Id, user.Email ?? request.Email, loginMethod, ipAddress, userAgent,
                user.Role.ToString(), user.BranchId, durationMs);

            loginTelemetry.RecordTokenIssued(
                user.Id, user.Email ?? request.Email, loginMethod,
                configuration.GetValue<int>("Jwt:ExpiryMinutes", 60));

            var response = new LoginResponse
            {
                AccessToken = token.Token,
                ExpiresIn = token.ExpiresIn,
                UserId = user.Id,
                Email = user.Email ?? string.Empty,
                FullName = user.FullName,
                Role = user.Role.ToString(),
                AgentId = user.AgentId,
                BranchId = user.BranchId
            };

            return Results.Ok(ApiResponse<LoginResponse>.Ok(response));
        })
        .AllowAnonymous()
        .WithName("ApiLogin")
        .WithSummary("Authenticate and obtain a JWT bearer token");

        return group;
    }

    private static (string Token, int ExpiresIn) GenerateJwtToken(ApplicationUser user, IConfiguration configuration)
    {
        var jwtKey = configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(jwtKey))
            throw new InvalidOperationException("JWT Key is not configured.");
        var issuer = configuration["Jwt:Issuer"] ?? "EastSeat.Agenti";
        var audience = configuration["Jwt:Audience"] ?? "EastSeat.Agenti.Android";
        var expiryMinutes = configuration.GetValue<int>("Jwt:ExpiryMinutes", 60);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, user.UserName ?? user.Email ?? string.Empty),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("full_name", user.FullName),
            new Claim("agent_id", user.AgentId?.ToString() ?? string.Empty),
            new Claim("branch_id", user.BranchId?.ToString() ?? string.Empty)
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiryMinutes * 60);
    }
}

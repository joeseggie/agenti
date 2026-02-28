using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using EastSeat.Agenti.Web.Data;

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
            ILogger<Program> logger) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                return Results.BadRequest(ApiResponse<LoginResponse>.Fail("Email and password are required."));

            var user = await userManager.FindByEmailAsync(request.Email);
            if (user is null)
                return Results.Unauthorized();

            if (!user.IsActive)
            {
                // Log inactive account attempt for security monitoring
                logger.LogWarning("Login attempt for inactive account: {Email}", request.Email);
                return Results.Unauthorized();
            }

            var passwordValid = await userManager.CheckPasswordAsync(user, request.Password);
            if (!passwordValid)
                return Results.Unauthorized();

            var jwtKey = configuration["Jwt:Key"];
            if (string.IsNullOrWhiteSpace(jwtKey))
            {
                logger.LogError("JWT key is not configured. Cannot generate authentication token.");
                return Results.Json(
                    ApiResponse<LoginResponse>.Fail("Authentication is temporarily unavailable. Please contact support."),
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            var token = GenerateJwtToken(user, configuration);
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

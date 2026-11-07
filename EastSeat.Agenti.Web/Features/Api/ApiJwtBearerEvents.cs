using System.Security.Claims;
using EastSeat.Agenti.Web.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;

namespace EastSeat.Agenti.Web.Features.Api;

public sealed class ApiJwtBearerEvents(UserManager<ApplicationUser> users) : JwtBearerEvents
{
    public override async Task TokenValidated(TokenValidatedContext context)
    {
        var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = userId == null ? null : await users.FindByIdAsync(userId);
        if (user == null || !user.IsActive || user.IsDeleted ||
            context.Principal?.FindFirstValue(ClaimTypes.Role) != user.Role.ToString())
        {
            context.Fail("The account or its permissions have changed. Sign in again.");
        }
    }
}

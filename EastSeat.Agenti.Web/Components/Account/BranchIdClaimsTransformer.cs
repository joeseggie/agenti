using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using EastSeat.Agenti.Web.Data;

namespace EastSeat.Agenti.Web.Components.Account;

/// <summary>
/// Claims transformer that adds BranchId claim from the user's BranchId property.
/// This ensures that authenticated users have access to their branch information in the client-side code.
/// </summary>
public class BranchIdClaimsTransformer(UserManager<ApplicationUser> userManager) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user?.BranchId.HasValue == true)
        {
            var identity = principal.Identity as ClaimsIdentity;
            if (identity != null && !principal.HasClaim(ClaimTypes.UserData, "BranchId"))
            {
                identity.AddClaim(new Claim("BranchId", user.BranchId.Value.ToString()));
            }
        }

        return principal;
    }
}

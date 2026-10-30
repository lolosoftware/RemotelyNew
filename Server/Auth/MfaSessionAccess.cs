using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Remotely.Server.Services;
using System.Security.Claims;

namespace Remotely.Server.Auth;

public interface IMfaSessionAccess
{
    Task<bool> IsAuthorized(ClaimsPrincipal? principal);
    Task<bool> IsCurrentIdentity(ClaimsPrincipal? principal);
}

/// <summary>Checks the authenticated session, not just the account's MFA setting.</summary>
public sealed class MfaSessionAccess(IDataService dataService, IOptions<IdentityOptions> options) : IMfaSessionAccess
{
    // Identity rebuilds the principal after validating its stamp. Preserve the verified MFA proof.
    public static Task PreserveMfaOnRefresh(SecurityStampRefreshingPrincipalContext context)
    {
        if (context.CurrentPrincipal?.HasClaim("amr", "mfa") == true &&
            context.NewPrincipal is { Identity: ClaimsIdentity identity } refreshed && !refreshed.HasClaim("amr", "mfa"))
            identity.AddClaim(new Claim("amr", "mfa"));
        return Task.CompletedTask;
    }

    public Task<bool> IsAuthorized(ClaimsPrincipal? principal) => Validate(principal, requireMfa: true);
    public Task<bool> IsCurrentIdentity(ClaimsPrincipal? principal) => Validate(principal, requireMfa: false);

    private async Task<bool> Validate(ClaimsPrincipal? principal, bool requireMfa)
    {
        if (principal?.Identity is not { IsAuthenticated: true, Name: { } name } ||
            (requireMfa && !principal.HasClaim("amr", "mfa")))
        {
            return false;
        }

        var result = await dataService.GetUserByName(name);
        if (!result.IsSuccess)
        {
            return false;
        }

        var user = result.Value;
        var claims = options.Value.ClaimsIdentity;
        return (!requireMfa || user.TwoFactorEnabled) &&
            !(user.LockoutEnabled && user.LockoutEnd > DateTimeOffset.UtcNow) &&
            principal.FindFirstValue(claims.UserIdClaimType) == user.Id &&
            !string.IsNullOrEmpty(user.SecurityStamp) &&
            principal.FindFirstValue(claims.SecurityStampClaimType) == user.SecurityStamp;
    }
}

public sealed class MfaSessionRequirement : IAuthorizationRequirement;

public sealed class MfaSessionHandler(IMfaSessionAccess access) : AuthorizationHandler<MfaSessionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, MfaSessionRequirement requirement)
    {
        if (await access.IsAuthorized(context.User))
        {
            context.Succeed(requirement);
        }
    }
}

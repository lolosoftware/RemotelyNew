using Microsoft.AspNetCore.Authorization;
using Remotely.Server.Auth;

namespace Remotely.Server.Auth;

public class TwoFactorRequiredHandler(IHttpContextAccessor contextAccessor, IMfaSessionAccess access)
    : AuthorizationHandler<TwoFactorRequiredRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, TwoFactorRequiredRequirement requirement)
    {
        // Public pages and account enrollment must remain reachable before MFA setup.
        // Remote-control endpoints use the separate, unconditional RemoteControl policy.
        var path = contextAccessor.HttpContext?.Request.Path ?? PathString.Empty;
        if (context.User.Identity?.IsAuthenticated != true ||
            path.StartsWithSegments("/Account") ||
            await access.IsAuthorized(context.User))
        {
            context.Succeed(requirement);
        }
    }
}

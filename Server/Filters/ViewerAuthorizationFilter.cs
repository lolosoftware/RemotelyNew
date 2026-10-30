using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Remotely.Server.Auth;

namespace Remotely.Server.Filters;

internal class ViewerAuthorizationFilter(IMfaSessionAccess access) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (await access.IsAuthorized(context.HttpContext.User)) return;
        context.Result = context.HttpContext.User.Identity?.IsAuthenticated == true
            ? new RedirectResult("/Account/Manage/TwoFactorAuthentication")
            : new RedirectResult("/Account/Login?ReturnUrl=%2FViewer");
    }
}

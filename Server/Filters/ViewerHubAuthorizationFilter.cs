using Microsoft.AspNetCore.SignalR;
using Remotely.Server.Auth;

namespace Remotely.Server.Filters;

/// <summary>MVC filters do not protect SignalR hub invocations.</summary>
public sealed class ViewerHubAuthorizationFilter(IMfaSessionAccess access) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var items = context.Context.Items;
        var now = DateTimeOffset.UtcNow;
        if (items.TryGetValue("MfaValidatedUtc", out var value) && value is DateTimeOffset validated &&
            now - validated < TimeSpan.FromSeconds(5)) return await next(context);
        if (!await access.IsAuthorized(context.Context.User))
        {
            context.Context.Abort();
            throw new HubException("Your authorization has expired. Sign in with two-factor authentication.");
        }

        items["MfaValidatedUtc"] = now;
        return await next(context);
    }
}

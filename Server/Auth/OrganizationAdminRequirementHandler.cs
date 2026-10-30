using Microsoft.AspNetCore.Authorization;
using Remotely.Server.Services;

namespace Remotely.Server.Auth;

public class OrganizationAdminRequirementHandler : AuthorizationHandler<OrganizationAdminRequirement>
{
    private readonly IDataService _dataService;
    private readonly IMfaSessionAccess _mfa;

    public OrganizationAdminRequirementHandler(IDataService dataService, IMfaSessionAccess mfa)
    {
        _dataService = dataService;
        _mfa = mfa;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, OrganizationAdminRequirement requirement)
    {
        if (!await _mfa.IsAuthorized(context.User) || context.User.Identity?.IsAuthenticated != true ||
            string.IsNullOrWhiteSpace(context.User.Identity.Name))
        {
            context.Fail();
            return;
        }

        var userResult = await _dataService.GetUserByName(context.User.Identity.Name);

        if (!userResult.IsSuccess ||
            !userResult.Value.IsAdministrator)
        {
            context.Fail();
            return;
        }

        context.Succeed(requirement);
    }
}

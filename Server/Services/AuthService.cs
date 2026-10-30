using Microsoft.AspNetCore.Components.Authorization;
using Remotely.Shared.Entities;

namespace Remotely.Server.Services;

public interface IAuthService
{
    Task<bool> IsAuthenticated();
    Task<Result<RemotelyUser>> GetUser();
}

public class AuthService : IAuthService
{
    private readonly AuthenticationStateProvider _authProvider;
    private readonly IDataService _dataService;
    private readonly Remotely.Server.Auth.IMfaSessionAccess _mfa;

    public AuthService(
        AuthenticationStateProvider authProvider,
        IDataService dataService, Remotely.Server.Auth.IMfaSessionAccess mfa)
    {
        _authProvider = authProvider;
        _dataService = dataService;
        _mfa = mfa;
    }

    public async Task<bool> IsAuthenticated()
    {
        var principal = await _authProvider.GetAuthenticationStateAsync();
        return await _mfa.IsAuthorized(principal.User);
    }

    public async Task<Result<RemotelyUser>> GetUser()
    {
        var principal = await _authProvider.GetAuthenticationStateAsync();

        if (await _mfa.IsAuthorized(principal.User))
        {
            return await _dataService.GetUserByName($"{principal.User.Identity!.Name}");
        }

        return Result.Fail<RemotelyUser>("Not authenticated.");
    }
}

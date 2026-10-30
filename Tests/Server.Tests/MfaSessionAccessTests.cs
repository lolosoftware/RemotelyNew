using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Remotely.Server.Auth;
using Remotely.Server.Services;
using Remotely.Shared.Entities;
using Remotely.Shared.Primitives;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Remotely.Server.Tests;

[TestClass]
public class MfaSessionAccessTests
{
    private readonly RemotelyUser _user = new()
    {
        Id = "user", UserName = "operator@example.test", TwoFactorEnabled = true, SecurityStamp = "current"
    };

    private MfaSessionAccess CreateAccess()
    {
        var data = new Mock<IDataService>();
        data.Setup(x => x.GetUserByName(_user.UserName!, null)).ReturnsAsync(Result.Ok(_user));
        return new MfaSessionAccess(data.Object, Microsoft.Extensions.Options.Options.Create(new IdentityOptions()));
    }

    private ClaimsPrincipal Principal(bool mfa = true, string stamp = "current", string id = "user")
    {
        var identity = new ClaimsIdentity("Identity.Application");
        identity.AddClaim(new Claim(ClaimTypes.Name, _user.UserName!));
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, id));
        identity.AddClaim(new Claim("AspNet.Identity.SecurityStamp", stamp));
        if (mfa) identity.AddClaim(new Claim("amr", "mfa"));
        return new ClaimsPrincipal(identity);
    }

    [TestMethod]
    public async Task CookieRefreshPreservesMfaOnlyWhenAlreadyValidated()
    {
        var refreshed = Principal(mfa: false);
        await MfaSessionAccess.PreserveMfaOnRefresh(new SecurityStampRefreshingPrincipalContext
            { CurrentPrincipal = Principal(), NewPrincipal = refreshed });
        Assert.IsTrue(refreshed.HasClaim("amr", "mfa"));
        var passwordOnly = Principal(mfa: false);
        await MfaSessionAccess.PreserveMfaOnRefresh(new SecurityStampRefreshingPrincipalContext
            { CurrentPrincipal = Principal(mfa: false), NewPrincipal = passwordOnly });
        Assert.IsFalse(passwordOnly.HasClaim("amr", "mfa"));
    }

    [TestMethod] public async Task RejectsAnonymous() => Assert.IsFalse(await CreateAccess().IsAuthorized(new ClaimsPrincipal()));
    [TestMethod] public async Task RejectsPasswordOnlyEvenWhenMfaIsEnrolled() => Assert.IsFalse(await CreateAccess().IsAuthorized(Principal(mfa: false)));
    [TestMethod] public async Task RejectsRevokedCookie() => Assert.IsFalse(await CreateAccess().IsAuthorized(Principal(stamp: "old")));
    [TestMethod] public async Task RejectsWrongAccountId() => Assert.IsFalse(await CreateAccess().IsAuthorized(Principal(id: "other")));
    [TestMethod] public async Task EnrollmentStillRejectsARevokedPasswordOnlyCookie()
    {
        _user.TwoFactorEnabled = false;
        Assert.IsFalse(await CreateAccess().IsCurrentIdentity(Principal(mfa: false, stamp: "old")));
        Assert.IsTrue(await CreateAccess().IsCurrentIdentity(Principal(mfa: false)));
    }
    [TestMethod] public async Task AcceptsCurrentMfaSession() => Assert.IsTrue(await CreateAccess().IsAuthorized(Principal()));

    [TestMethod]
    public async Task RejectsDisabledMfaAndLockedAccounts()
    {
        _user.TwoFactorEnabled = false;
        Assert.IsFalse(await CreateAccess().IsAuthorized(Principal()));
        _user.TwoFactorEnabled = true;
        _user.LockoutEnabled = true;
        _user.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(10);
        Assert.IsFalse(await CreateAccess().IsAuthorized(Principal()));
    }
}

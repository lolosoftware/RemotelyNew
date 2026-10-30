using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Remotely.Server.Auth;
using Remotely.Server.Hubs;
using Remotely.Server.Models;
using Remotely.Server.Services;
using Remotely.Server.Tests.Mocks;
using Remotely.Shared.Entities;
using Remotely.Shared.Enums;
using Remotely.Shared.Interfaces;
using Remotely.Shared.Models;
using System.Collections.Generic;
using System;
using System.Linq;
using Remotely.Shared.Primitives;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Remotely.Server.Tests;

[TestClass]
public class ViewerHubConsentTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnattendedForwardingRequiresAcceptedConsent(bool accepted)
    {
        var session = new RemoteControlSession { RequireConsent = true, OrganizationId = "org", DeviceId = "device" };
        // Internal setters are intentionally kept inaccessible to external callers.
        typeof(RemoteControlSession).GetProperty(nameof(session.Mode))!.SetValue(session, RemoteControlMode.Unattended);
        typeof(RemoteControlSession).GetProperty(nameof(session.AccessKey))!.SetValue(session, "test-key");
        typeof(RemoteControlSession).GetProperty(nameof(session.DesktopConnectionId))!.SetValue(session, "desktop");
        var cache = new Mock<IRemoteControlSessionCache>();
        RemoteControlSession? cached = session;
        cache.Setup(x => x.TryGetValue("session", out cached)).Returns(true);
        var data = new Mock<IDataService>();
        data.Setup(x => x.GetUserByName("operator", It.IsAny<Action<IQueryable<RemotelyUser>>>())).ReturnsAsync(Result.Ok(new RemotelyUser { OrganizationID = "org" }));
        data.Setup(x => x.DoesUserHaveAccessToDevice("device", It.IsAny<RemotelyUser>())).Returns(true);
        var mfa = new Mock<IMfaSessionAccess>();
        mfa.Setup(x => x.IsAuthorized(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(true);
        var desktop = new HubContextFixture<DesktopHub, IDesktopHubClient>();
        desktop.SingleClientProxyMock.Setup(x => x.PromptForAccess(It.IsAny<RemoteControlAccessRequest>()))
            .ReturnsAsync(accepted ? PromptForAccessResult.Accepted : PromptForAccessResult.Denied);
        var context = new Mock<HubCallerContext>();
        context.SetupGet(x => x.Features).Returns(new Microsoft.AspNetCore.Http.Features.FeatureCollection());
        context.SetupGet(x => x.ConnectionId).Returns("viewer");
        context.SetupGet(x => x.Items).Returns(new Dictionary<object, object?>());
        context.SetupGet(x => x.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "operator") }, "test")));
        var hub = new ViewerHub(cache.Object, Mock.Of<IDesktopStreamCache>(), Mock.Of<IHubContext<AgentHub, IAgentHubClient>>(),
            desktop.HubContextMock.Object, Mock.Of<ISessionRecordingSink>(), data.Object, NullLogger<ViewerHub>.Instance, mfa.Object)
            { Context = context.Object };

        var result = await hub.SendScreenCastRequestToDevice("session", "test-key", "Operator");
        Assert.AreEqual(accepted, result.IsSuccess);
        Assert.AreEqual(accepted, session.ViewerList.Contains("viewer"));
        if (!accepted)
        {
            await Assert.ThrowsExceptionAsync<HubException>(() => hub.SendDtoToClient(new byte[] { 0 }));
            desktop.SingleClientProxyMock.Verify(x => x.SendDtoToClient(It.IsAny<byte[]>(), "viewer"), Times.Never);
        }
    }
}

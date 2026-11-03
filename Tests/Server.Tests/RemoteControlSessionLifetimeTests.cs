using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Remotely.Server.Hubs;
using Remotely.Server.Models;
using Remotely.Server.Services;
using Remotely.Shared.Enums;
using Remotely.Shared.Interfaces;
using Remotely.Shared.Services;

namespace Remotely.Server.Tests;

[TestClass]
public class RemoteControlSessionLifetimeTests
{
    [TestMethod]
    public async Task DisposalReleasesWaitersAndIgnoresLateReadinessEvents()
    {
        var session = new RemoteControlSession();
        var timedWait = session.WaitForSessionReady(TimeSpan.FromMinutes(1));
        var cancellableWait = session.WaitForSessionReady(CancellationToken.None);
        session.Dispose();
        session.SetSessionReadyState(false);
        session.SetSessionReadyState(true);
        session.Dispose();
        Assert.IsFalse(await timedWait.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.IsFalse(await cancellableWait.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.IsFalse(await session.WaitForSessionReady(TimeSpan.Zero));
    }

    [TestMethod]
    public async Task DisposingOriginalDoesNotInvalidateClonedSession()
    {
        var original = new RemoteControlSession { Mode = RemoteControlMode.Unattended };
        using var clone = original.CreateNew();
        original.Dispose();
        clone.SetSessionReadyState(true);
        Assert.IsTrue(await clone.WaitForSessionReady(TimeSpan.Zero));
        Assert.IsFalse(clone.IsDisposed);
    }

    [TestMethod]
    public async Task ReadinessAndDisposalAreIndependentBetweenClones()
    {
        using var original = new RemoteControlSession { Mode = RemoteControlMode.Unattended };
        using var clone = original.CreateNew();
        original.SetSessionReadyState(true);
        Assert.IsFalse(await clone.WaitForSessionReady(TimeSpan.Zero));
        clone.Dispose();
        Assert.IsTrue(await original.WaitForSessionReady(TimeSpan.Zero));
    }

    [TestMethod]
    public async Task ResetRequiresANewReadinessNotification()
    {
        using var session = new RemoteControlSession();
        session.SetSessionReadyState(true);
        session.SetSessionReadyState(false);
        var wait = session.WaitForSessionReady(CancellationToken.None);
        Assert.IsFalse(wait.IsCompleted);
        session.SetSessionReadyState(true);
        Assert.IsTrue(await wait);
    }

    [TestMethod]
    public async Task CancellingOneWaiterDoesNotCancelOtherWaiters()
    {
        using var session = new RemoteControlSession();
        using var cancellation = new CancellationTokenSource();
        var cancelledWait = session.WaitForSessionReady(cancellation.Token);
        var activeWait = session.WaitForSessionReady(CancellationToken.None);
        cancellation.Cancel();
        Assert.IsFalse(await cancelledWait);
        session.SetSessionReadyState(true);
        Assert.IsTrue(await activeWait);
    }

    [TestMethod]
    public async Task ConcurrentDisposalAndReadinessAreSafe()
    {
        for (var i = 0; i < 100; i++)
        {
            using var session = new RemoteControlSession();
            await Task.WhenAll(
                Task.Run(() => { session.SetSessionReadyState(true); session.SetSessionReadyState(false); }),
                Task.Run(session.Dispose), Task.Run(session.Dispose));
            Assert.IsFalse(await session.WaitForSessionReady(TimeSpan.Zero));
        }
    }

    [TestMethod]
    public async Task RefreshingSameCacheEntryDoesNotDisposeIt()
    {
        using var session = new RemoteControlSession();
        var cache = new RemoteControlSessionCache(Mock.Of<ISystemTime>(), NullLogger<RemoteControlSessionCache>.Instance);
        cache.AddOrUpdate("session", session);
        cache.AddOrUpdate("session", session);
        session.SetSessionReadyState(true);
        Assert.IsTrue(await session.WaitForSessionReady(TimeSpan.Zero));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LateDesktopDisconnectDoesNotTouchExpiredOrReconnectedSession(bool disposed)
    {
        using var session = new RemoteControlSession { Mode = RemoteControlMode.Unattended, DesktopConnectionId = "new-desktop" };
        session.SetSessionReadyState(true);
        if (disposed) session.Dispose();
        var context = new Mock<HubCallerContext>();
        context.SetupGet(x => x.ConnectionId).Returns("old-desktop");
        context.SetupGet(x => x.Items).Returns(new Dictionary<object, object?> { ["SessionInfo"] = session });
        var cache = new Mock<IRemoteControlSessionCache>(MockBehavior.Strict);
        var hub = new DesktopHub(cache.Object, Mock.Of<IDesktopStreamCache>(), Mock.Of<IAgentHubSessionCache>(),
            Mock.Of<IHubContext<AgentHub, IAgentHubClient>>(), Mock.Of<IHubContext<ViewerHub, IViewerHubClient>>(),
            NullLogger<DesktopHub>.Instance) { Context = context.Object };
        await hub.OnDisconnectedAsync(null);
        Assert.AreEqual(!disposed, await session.WaitForSessionReady(TimeSpan.Zero));
        cache.VerifyNoOtherCalls();
    }
}

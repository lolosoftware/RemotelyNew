using Remotely.Server.Enums;
using Remotely.Server.Services;
using Remotely.Shared.Helpers;

namespace Remotely.Server.Models;

/// <summary>
/// Contains data related to a remote control session.  Consuming projects
/// are expected to derive a class from this and add project-specific properties.
/// They should add these derived classes to<see cref="IRemoteControlSessionCache"/>
/// when sessions are created.
/// </summary>
public class RemoteControlSession : IDisposable
{
    private object _sessionStateLock = new();
    private TaskCompletionSource<bool> _sessionReadySignal = NewReadySignal();
    private bool _disposedValue;
    private StreamerState _streamerState;

    public RemoteControlSession()
    {
        Created = DateTimeOffset.Now;
    }
    public string AccessKey { get; internal set; } = string.Empty;
    public string AgentConnectionId { get; set; } = string.Empty;
    public string AttendedSessionId { get; set; } = string.Empty;
    public DateTimeOffset Created { get; internal set; }
    public string DesktopConnectionId { get; internal set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public DateTimeOffset LastStateChange { get; private set; } = DateTimeOffset.Now;
    public string MachineName { get; internal set; } = string.Empty;
    public RemoteControlMode Mode { get; internal set; }
    /// <summary>
    /// Whether to notify the user via system notification when a remote
    /// control session has started.
    /// </summary>
    public bool NotifyUserOnStart { get; set; } = true;

    public string OrganizationId { get; set; } = string.Empty;
    public string OrganizationName { get; internal set; } = string.Empty;
    public string RelativeAccessUri => $"/Viewer?mode=Unattended&sessionId={UnattendedSessionId}&accessKey={AccessKey}&viewonly=False";
    public string RequesterName { get; set; } = string.Empty;
    public string RequesterUserName { get; internal set; } = string.Empty;
    /// <summary>
    /// Whether the remote control session requires the end user's consent
    /// before the view can connect.
    /// </summary>
    public bool RequireConsent { get; set; }

    public DateTimeOffset StartTime { get; internal set; }

    /// <summary>
    /// Current state of the streamer (desktop process) that's associated
    /// with this session.
    /// </summary>
    public StreamerState StreamerState
    {
        get => _streamerState;
        set
        {
            _streamerState = value;
            LastStateChange = DateTimeOffset.Now;
        }
    }

    public Guid StreamId { get; internal set; }
    public Guid UnattendedSessionId { get; set; }
    public string UserConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// Contains a collection of viewer SignalR connection IDs.
    /// </summary>
    public HashSet<string> ViewerList { get; private set; } = new();

    public bool ViewOnly { get; set; }
    /// <summary>
    /// Creates a new session based off this existing one, but with
    /// a new UnattendedSessionId and AccessKey.
    /// </summary>
    /// <returns></returns>
    public RemoteControlSession CreateNew()
    {
        if (Mode != RemoteControlMode.Unattended)
        {
            throw new InvalidOperationException("Only available in unattended mode.");
        }

        var clone = (RemoteControlSession)MemberwiseClone();
        // MemberwiseClone must not share lifetime state with the original session.
        clone._sessionStateLock = new();
        clone._sessionReadySignal = NewReadySignal();
        clone._disposedValue = false;
        clone.Created = DateTimeOffset.Now;
        clone.UnattendedSessionId = Guid.NewGuid();
        clone.AccessKey = RandomGenerator.GenerateAccessKey();
        clone.ViewerList = new();
        clone.ViewOnly = ViewOnly;
        return clone;
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    internal bool IsDisposed
    {
        get { lock (_sessionStateLock) { return _disposedValue; } }
    }

    private static TaskCompletionSource<bool> NewReadySignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<bool> WaitForSessionReady(TimeSpan waitTime)
    {
        Task<bool> ready;
        lock (_sessionStateLock)
        {
            if (_disposedValue) return false;
            ready = _sessionReadySignal.Task;
        }
        try { return await ready.WaitAsync(waitTime); }
        catch (TimeoutException) { return false; }
        catch (OperationCanceledException) { return false; }
    }

    public async Task<bool> WaitForSessionReady(CancellationToken cancellationToken)
    {
        Task<bool> ready;
        lock (_sessionStateLock)
        {
            if (_disposedValue) return false;
            ready = _sessionReadySignal.Task;
        }
        try { return await ready.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) { return false; }
    }

    internal void SetSessionReadyState(bool isReady)
    {
        lock (_sessionStateLock)
        {
            if (_disposedValue) return;
            if (isReady) _sessionReadySignal.TrySetResult(true);
            else if (_sessionReadySignal.Task.IsCompleted) _sessionReadySignal = NewReadySignal();
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        lock (_sessionStateLock)
        {
            if (_disposedValue) return;
            _disposedValue = true;
            // Wake pending requests without leaving blocked worker threads behind.
            _sessionReadySignal.TrySetResult(false);
        }
    }
}

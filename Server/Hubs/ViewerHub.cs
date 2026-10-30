using Remotely.Server.Enums;
using Remotely.Server.Filters;
using Remotely.Server.Models;
using Remotely.Server.Services;
using Remotely.Shared.Interfaces;
using Remotely.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Remotely.Server.Auth;
using Microsoft.AspNetCore.SignalR;

namespace Remotely.Server.Hubs;

[Authorize(Policy = PolicyNames.RemoteControl)]
public class ViewerHub : Hub<IViewerHubClient>
{
    private readonly IHubContext<DesktopHub, IDesktopHubClient> _desktopHub;
    private readonly IHubContext<AgentHub, IAgentHubClient> _agentHub;
    private readonly IDataService _dataService;
    private readonly IMfaSessionAccess _mfaAccess;
    private readonly ISessionRecordingSink _sessionRecordingSink;
    private readonly IRemoteControlSessionCache _desktopSessionCache;
    private readonly ILogger<ViewerHub> _logger;
    private readonly IDesktopStreamCache _streamCache;

    public ViewerHub(
        IRemoteControlSessionCache desktopSessionCache,
        IDesktopStreamCache streamCache,
        IHubContext<AgentHub, IAgentHubClient> agentHub,
        IHubContext<DesktopHub, IDesktopHubClient> desktopHub,
        ISessionRecordingSink sessionRecordingSink,
        IDataService dataService,
        ILogger<ViewerHub> logger,
        IMfaSessionAccess mfaAccess)
    {
        _desktopSessionCache = desktopSessionCache;
        _streamCache = streamCache;
        _desktopHub = desktopHub;
        _agentHub = agentHub;
        _dataService = dataService;
        _mfaAccess = mfaAccess;
        _sessionRecordingSink = sessionRecordingSink;
        _logger = logger;
    }

    private string RequesterDisplayName
    {
        get
        {
            if (Context.Items.TryGetValue(nameof(RequesterDisplayName), out var result) &&
                result is string requesterName)
            {
                return requesterName;
            }
            return string.Empty;
        }
        set
        {
            Context.Items[nameof(RequesterDisplayName)] = value;
        }
    }

    private RemoteControlSession SessionInfo
    {
        get
        {
            if (Context.Items.TryGetValue(nameof(SessionInfo), out var result) &&
                result is RemoteControlSession session)
            {
                return session;
            }

            var newSession = new RemoteControlSession();
            Context.Items[nameof(SessionInfo)] = newSession;
            return newSession;
        }
        set
        {
            Context.Items[nameof(SessionInfo)] = value;
        }
    }
    public async Task<Result> ChangeWindowsSession(int targetWindowsSession)
    {
        try
        {
            await EnsureSessionAccess();
            if (SessionInfo.Mode != RemoteControlMode.Unattended)
            {
                return Result.Fail("Only available in unattended mode.");
            }

            SessionInfo.ViewerList.Remove(Context.ConnectionId);
            await _desktopHub.Clients
                .Client(SessionInfo.DesktopConnectionId)
                .ViewerDisconnected(Context.ConnectionId);

            SessionInfo = SessionInfo.CreateNew();
            _desktopSessionCache.AddOrUpdate($"{SessionInfo.UnattendedSessionId}", SessionInfo);

            await _agentHub.Clients
                .Client(SessionInfo.AgentConnectionId)
                .ChangeWindowsSession(
                    Context.ConnectionId,
                    $"{SessionInfo.UnattendedSessionId}",
                    SessionInfo.AccessKey,
                    SessionInfo.UserConnectionId,
                    SessionInfo.RequesterUserName,
                    SessionInfo.OrganizationName,
                    SessionInfo.OrganizationId,
                    targetWindowsSession);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while changing Windows session.");
            return Result.Fail("An error occurred while changing Windows session.");
        }
    }

    public async IAsyncEnumerable<byte[]> GetDesktopStream()
    {
        await EnsureSessionAccess();
        var sessionResult = await _streamCache.WaitForStreamSession(
            SessionInfo.StreamId,
            Context.ConnectionId,
            TimeSpan.FromSeconds(30));

        if (!sessionResult.IsSuccess)
        {
            _logger.LogError("Timed out while waiting for desktop stream.");
            await Clients.Caller.ShowMessage("Request timed out");
            yield break;
        }

        var signaler = sessionResult.Value;

        if (signaler.Stream is null)
        {
            _logger.LogError("Stream was null.");
            yield break;
        }

        SessionInfo.StreamerState = StreamerState.Connected;

        var lastValidation = DateTimeOffset.MinValue;
        try
        {
            await foreach (var chunk in signaler.Stream.WithCancellation(Context.ConnectionAborted))
            {
                if (DateTimeOffset.UtcNow - lastValidation > TimeSpan.FromSeconds(5))
                {
                    await EnsureSessionAccess();
                    lastValidation = DateTimeOffset.UtcNow;
                }
                yield return chunk;
            }
        }
        finally
        {
            signaler.EndSignal.Release();
            _logger.LogInformation("Streaming session ended for {sessionId}.", SessionInfo.StreamId);
        }
    }

    public async Task<RemoteControlViewerOptions> GetViewerOptions()
    {
        var settings = await _dataService.GetSettings();
        return new RemoteControlViewerOptions()
        {
            ShouldRecordSession = settings.EnableRemoteControlRecording
        };
    }

    public async Task InvokeCtrlAltDel()
    {
        try
        {
            await EnsureSessionAccess();
            if (SessionInfo.ViewOnly) throw new HubException("This session is view-only.");
            await _agentHub.Clients.Client(SessionInfo.AgentConnectionId).InvokeCtrlAltDel();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while invoking Ctrl+Alt+Del.");
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (!string.IsNullOrWhiteSpace(SessionInfo.DesktopConnectionId))
        {
            await _desktopHub.Clients
                .Client(SessionInfo.DesktopConnectionId)
                .ViewerDisconnected(Context.ConnectionId);
        }

        SessionInfo.ViewerList.Remove(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task SendDtoToClient(byte[] dtoWrapper)
    {
        await EnsureSessionAccess();
        if (SessionInfo.ViewOnly)
        {
            var wrapper = MessagePack.MessagePackSerializer.Deserialize<Remotely.Shared.Models.Dtos.DtoWrapper>(
                dtoWrapper, Remotely.Shared.Helpers.ProtocolSerialization.Options);
            if (wrapper.DtoType is not (Remotely.Shared.Models.Dtos.DtoType.FrameReceived or
                Remotely.Shared.Models.Dtos.DtoType.SelectScreen or Remotely.Shared.Models.Dtos.DtoType.WindowsSessions or
                Remotely.Shared.Models.Dtos.DtoType.SetQuality))
                throw new HubException("This session is view-only.");
        }
        if (string.IsNullOrWhiteSpace(SessionInfo.DesktopConnectionId)) return;

        await _desktopHub.Clients
            .Client(SessionInfo.DesktopConnectionId)
            .SendDtoToClient(dtoWrapper, Context.ConnectionId);
    }
    public async Task<Result> SendScreenCastRequestToDevice(string sessionId, string accessKey, string requesterName)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return Result.Fail("Session ID cannot be empty.");
        }

        if (!_desktopSessionCache.TryGetValue(sessionId, out var session))
        {
            return Result.Fail("Session ID not found.");
        }

        if (session.Mode == RemoteControlMode.Unattended &&
            accessKey != session.AccessKey)
        {
            _logger.LogError("Access key does not match for unattended session.  " +
                "Session ID: {sessionId}.  " +
                "Requester Name: {requesterName}.  " +
                "Requester Connection ID: {connectionId}",
                sessionId,
                requesterName,
                Context.ConnectionId);
            return Result.Fail("Authorization failed.");
        }

        if (!await CanAccessSession(session))
        {
            return Result.Fail("Authorization failed.");
        }

        SessionInfo.ViewerList.Remove(Context.ConnectionId);
        SessionInfo = session;
        SessionInfo.StreamId = Guid.NewGuid();
        RequesterDisplayName = requesterName;

        if (Context.User?.Identity?.IsAuthenticated == true)
        {
            SessionInfo.RequesterUserName = Context.User.Identity.Name ?? string.Empty;
        }

        var logMessage = $"Remote control session requested.  " +
                            $"Login ID (if logged in): {Context.User?.Identity?.Name}.  " +
                            $"Machine Name: {SessionInfo.MachineName}.  " +
                            $"Stream ID: {SessionInfo.StreamId}.  " +
                            $"Requester Name (if specified): {RequesterDisplayName}.  " +
                            $"Connection ID: {Context.ConnectionId}. User ID: {Context.UserIdentifier}.  " +
                            $"Screen Caster Connection ID: {SessionInfo.DesktopConnectionId}.  " +
                            $"Mode: {SessionInfo.Mode}.  " +
                            $"Requester IP Address: {Context.GetHttpContext()?.Connection?.RemoteIpAddress}";

        _logger.LogInformation("{msg}", logMessage);

        if (SessionInfo.Mode == RemoteControlMode.Unattended)
        {
            if (SessionInfo.RequireConsent)
            {
                var request = new RemoteControlAccessRequest(
                    Context.ConnectionId,
                    RequesterDisplayName,
                    SessionInfo.OrganizationName);

                var result = await _desktopHub.Clients
                    .Client(SessionInfo.DesktopConnectionId)
                    .PromptForAccess(request);

                if (result != Shared.Enums.PromptForAccessResult.Accepted)
                {
                    return Result.Fail($"Access request failed.  Reason: {result}");
                }
            }

            // Grant forwarding only after local consent succeeds.
            SessionInfo.ViewerList.Add(Context.ConnectionId);
            // Consent remains required for every new viewer of this session.

            await _desktopHub.Clients
                .Client(SessionInfo.DesktopConnectionId)
                .GetScreenCast(
                    Context.ConnectionId,
                    RequesterDisplayName,
                    SessionInfo.NotifyUserOnStart,
                    SessionInfo.StreamId);
        }
        else
        {
            SessionInfo.Mode = RemoteControlMode.Attended;
            SessionInfo.ViewerList.Add(Context.ConnectionId);
            await _desktopHub.Clients
                .Client(SessionInfo.DesktopConnectionId)
                .RequestScreenCast(
                    Context.ConnectionId,
                    RequesterDisplayName,
                    SessionInfo.NotifyUserOnStart,
                    SessionInfo.StreamId);
        }

        return Result.Ok();
    }

    public async Task StoreSessionRecording(IAsyncEnumerable<byte[]> webmStream)
    {
        await EnsureSessionAccess();
        try
        {
            await _sessionRecordingSink.SinkWebmStream(webmStream, SessionInfo);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Session recording stopped for stream {streamId}.", SessionInfo.StreamId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while storing session recording for stream {streamId}.", SessionInfo.StreamId);
        }
    }

    private async Task<bool> CanAccessSession(RemoteControlSession session)
    {
        if (!await _mfaAccess.IsAuthorized(Context.User)) return false;
        if (session.Mode != RemoteControlMode.Unattended) return true;
        var user = await _dataService.GetUserByName(Context.User!.Identity!.Name!);
        return user.IsSuccess && user.Value.OrganizationID == session.OrganizationId &&
            _dataService.DoesUserHaveAccessToDevice(session.DeviceId, user.Value);
    }

    private async Task EnsureSessionAccess()
    {
        var now = DateTimeOffset.UtcNow;
        var validMembership = SessionInfo.ViewerList.Contains(Context.ConnectionId);
        var cached = Context.Items.TryGetValue("SessionValidatedUtc", out var value) &&
            value is DateTimeOffset validated && now - validated < TimeSpan.FromSeconds(5) &&
            Context.Items.TryGetValue("ValidatedStreamId", out var stream) && stream is Guid id && id == SessionInfo.StreamId;
        if (!validMembership || (!cached && !await CanAccessSession(SessionInfo)))
        {
            Context.Abort();
            throw new HubException("Access to this session has been revoked.");
        }
        if (!cached)
        {
            Context.Items["SessionValidatedUtc"] = now;
            Context.Items["ValidatedStreamId"] = SessionInfo.StreamId;
        }
    }
}

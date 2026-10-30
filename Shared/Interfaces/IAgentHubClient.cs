using Remotely.Shared.Enums;

namespace Remotely.Shared.Interfaces;
public interface IAgentHubClient
{
    Task ChangeWindowsSession(
        string viewerConnectionId,
        string sessionId,
        string accessKey,
        string userConnectionId,
        string requesterName,
        string orgName,
        string orgId,
        int targetSessionId);

    Task SendChatMessage(
        string senderName, 
        string message, 
        string orgName, 
        string orgId, 
        bool disconnected, 
        string senderConnectionId);

    Task InvokeCtrlAltDel();

    Task DeleteLogs();


    Task GetLogs(string senderConnectionId);

    Task ReinstallAgent();

    Task UninstallAgent();

    Task RemoteControl(
        Guid sessionId, 
        string accessKey, 
        string userConnectionId, 
        string requesterName, 
        string orgName, 
        string orgId);

    Task RestartScreenCaster(
        string[] viewerIds, 
        string sessionId, 
        string accessKey, 
        string userConnectionId, 
        string requesterName, 
        string orgName, 
        string orgId);

    Task TransferFileFromBrowserToAgent(
        string transferId, 
        string[] fileIds, 
        string requesterId, 
        string expiringToken);

    Task TriggerHeartbeat();

    Task WakeDevice(string macAddress);
}

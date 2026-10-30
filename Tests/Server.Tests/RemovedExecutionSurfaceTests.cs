using Microsoft.VisualStudio.TestTools.UnitTesting;
using Remotely.Server.Hubs;
using Remotely.Shared.Interfaces;
using System.Linq;

namespace Remotely.Server.Tests;

[TestClass]
public class RemovedExecutionSurfaceTests
{
    [TestMethod]
    [DataRow("ExecuteCommand")]
    [DataRow("ExecuteCommandFromApi")]
    [DataRow("RunScript")]
    [DataRow("GetPowerShellCompletions")]
    public void AgentContractDoesNotExposeExecution(string method) => Assert.IsNull(typeof(IAgentHubClient).GetMethod(method));

    [TestMethod]
    public void ServerDoesNotExposeScriptingControllersOrQueueDispatcher()
    {
        var types = typeof(AgentHub).Assembly.GetTypes();
        foreach(var name in new[] { "ScriptingController", "SavedScriptsController", "ScriptResultsController", "ScriptScheduler", "ScriptScheduleDispatcher" })
            Assert.IsFalse(types.Any(x => x.Name == name), name + " must not ship in the control-only server.");
        Assert.IsNull(typeof(AgentHub).GetMethod("CheckForPendingScriptRuns"));
    }
}

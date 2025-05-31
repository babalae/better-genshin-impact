using System.Collections.Generic;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.GameTask.TaskProgress;

namespace BetterGenshinImpact.Service.Interface;

public interface IScriptService
{
    Task RunMulti(IEnumerable<ScriptGroupProject> projectList, string? groupName = null,TaskProgress? taskProgress = null);
}

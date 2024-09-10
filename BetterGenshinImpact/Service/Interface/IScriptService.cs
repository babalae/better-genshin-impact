using BetterGenshinImpact.Core.Script.Group;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Service.Interface;

public interface IScriptService
{
    Task RunMulti(IEnumerable<ScriptGroupProject> projectList, string? groupName = null);
}

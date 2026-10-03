using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script.Group;

namespace BetterGenshinImpact.Service.Interface;

public interface IScriptService
{
    /// <summary>
    /// 使用所属顶层任务的取消令牌执行配置组项目。
    /// </summary>
    Task RunMulti(IEnumerable<ScriptGroupProject> projectList, string? groupName, CancellationToken ct);
}

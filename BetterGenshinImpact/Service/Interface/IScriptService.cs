using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.GameTask.Runtime;

namespace BetterGenshinImpact.Service.Interface;

public interface IScriptService
{
    /// <summary>
    /// 使用所属顶层任务的运行环境与取消令牌执行配置组项目。
    /// </summary>
    /// <param name="runtime">所属顶层任务使用的运行环境</param>
    /// <param name="projectList">要执行的项目</param>
    /// <param name="groupName">配置组名称</param>
    /// <param name="ct">所属顶层任务的取消令牌</param>
    Task RunMulti(GameRuntime runtime, IEnumerable<ScriptGroupProject> projectList, string? groupName, CancellationToken ct);
}

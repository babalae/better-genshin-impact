using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>一条龙入口的默认清单；仅负责创建计划数据，不参与执行。</summary>
internal static class PuloniaOneDragonDefaults
{
    /// <summary>
    /// 按独立任务注册表声明的一条龙顺序创建默认清单，不复制全局任务配置。
    /// </summary>
    /// <param name="name">计划名称。</param>
    /// <param name="definitions">任务能力定义；只取声明了一条龙顺序的独立任务。</param>
    /// <returns>新的一条龙计划。</returns>
    internal static PuloniaTaskPlan CreatePlan(string name, IEnumerable<PuloniaTaskDefinition> definitions) => new()
    {
        Name = name, Purpose = PuloniaTaskPlanPurpose.OneDragon,
        RootTask = new PuloniaTask
        {
            Name = name,
            // OneDragonOrder 为 0 表示不默认加入；参数留空，运行时使用任务默认参数
            Children = definitions
                .Where(definition => definition.IsSoloTask && definition.OneDragonOrder > 0)
                .OrderBy(definition => definition.OneDragonOrder)
                .Select(definition => new PuloniaTask { Name = definition.DisplayName, TaskType = definition.TaskType })
                .ToList()
        }
    };
}

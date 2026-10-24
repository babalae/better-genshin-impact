using System;
using BetterGenshinImpact.GameTask.SoloTask;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.SoloTaskSettings;

/// <summary>按独立任务描述符创建专属设置 VM；宿主不再按任务类型写死 switch。</summary>
public static class SoloTaskSettingsFactory
{
    /// <summary>
    /// 为指定任务创建专属设置 VM。VM 构造函数接收当前有效参数，其余依赖从 DI 解析。
    /// </summary>
    /// <param name="registry">独立任务注册表。</param>
    /// <param name="taskId">独立任务标识；为空或未注册时返回 null。</param>
    /// <param name="effectiveParameters">当前有效参数快照。</param>
    /// <param name="services">可选服务容器；省略时沿用应用容器，离屏验证可注入隔离容器。</param>
    /// <returns>设置 VM；任务没有声明专属界面时返回 null，由宿主回退通用参数卡片。</returns>
    /// <exception cref="InvalidOperationException">声明的 VM 类型没有继承 <see cref="SoloTaskSettingsViewModel"/>。</exception>
    public static SoloTaskSettingsViewModel? Create(SoloTaskRegistry registry, string? taskId, JObject effectiveParameters,
        IServiceProvider? services = null)
    {
        if (!registry.TryGet(taskId, out var descriptor) || descriptor.SettingsViewModelType is not { } vmType)
        {
            return null;
        }

        return ActivatorUtilities.CreateInstance(services ?? App.ServiceProvider, vmType, effectiveParameters) as SoloTaskSettingsViewModel
               ?? throw new InvalidOperationException($"设置 VM {vmType.Name} 必须继承 {nameof(SoloTaskSettingsViewModel)}。");
    }
}

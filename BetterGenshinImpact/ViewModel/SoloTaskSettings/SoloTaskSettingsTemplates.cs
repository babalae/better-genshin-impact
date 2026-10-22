using System.Windows;
using BetterGenshinImpact.GameTask.SoloTask;

namespace BetterGenshinImpact.ViewModel.SoloTaskSettings;

/// <summary>为声明了专属界面的独立任务注册全局隐式 DataTemplate，ContentControl 按 VM 类型自动匹配视图。</summary>
public static class SoloTaskSettingsTemplates
{
    /// <summary>
    /// 为每个声明了设置界面的描述符注册以 VM 类型为键的隐式模板。必须在 UI 线程调用。
    /// </summary>
    /// <param name="resources">目标资源字典，通常是应用资源。</param>
    /// <param name="registry">独立任务注册表。</param>
    public static void Register(ResourceDictionary resources, SoloTaskRegistry registry)
    {
        foreach (var descriptor in registry.All)
        {
            if (descriptor.SettingsViewType is not { } viewType || descriptor.SettingsViewModelType is not { } vmType)
            {
                continue;
            }

            // 模板只描述“VM 类型 → 视图类型”，视图的 DataContext 由 ContentControl 设为 VM
            var template = new DataTemplate(vmType) { VisualTree = new FrameworkElementFactory(viewType) };
            template.Seal();
            resources[new DataTemplateKey(vmType)] = template;
        }
    }
}

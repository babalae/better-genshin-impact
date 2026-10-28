using System.Linq;
using System.Windows;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.ViewModel.Resin;
using BetterGenshinImpact.ViewModel.SoloTaskSettings;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.View.Windows;

/// <summary>体力计划子任务设置弹窗的视图层入口；按注册表中的参数类型生成 Schema，取消不返回草稿。</summary>
/// <param name="registry">独立任务注册表，提供子任务参数类型。</param>
/// <param name="resinDialogs">树脂设置弹窗，供嵌套字段使用。</param>
public sealed class ResinPlanSettingsDialogs(SoloTaskRegistry registry, IResinSettingsDialogs resinDialogs) : IResinPlanSettingsDialogs
{
    /// <inheritdoc />
    public JObject? EditSubTask(ResinPlanSubTaskSpec spec, JObject parameters)
    {
        var schema = PuloniaSoloTaskSchema.Create(registry.Get(spec.TaskId).ParamType);
        var viewModel = new ResinPlanSubTaskSettingsViewModel(spec, schema, parameters, resinDialogs);
        var window = new ResinPlanSubTaskSettingsWindow(viewModel) { Owner = GetOwner() };
        return window.ShowDialog() == true ? window.Result : null;
    }

    /// <summary>优先使用活动窗口，使弹窗显示在打开入口的窗口上方。</summary>
    private static Window? GetOwner()
        => Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
           ?? Application.Current?.MainWindow;
}

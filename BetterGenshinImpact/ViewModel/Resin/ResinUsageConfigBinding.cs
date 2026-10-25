using System;
using System.ComponentModel;
using BetterGenshinImpact.GameTask.Common.Resin;

namespace BetterGenshinImpact.ViewModel.Resin;

/// <summary>单例页面入口与配置对象之间的同步，只整体替换值，以结构相等比较阻止通知回环。</summary>
public static class ResinUsageConfigBinding
{
    /// <summary>创建入口并订阅同生命周期的配置；多个入口可以绑定同一配置，任何一处修改都会刷新其他入口。</summary>
    /// <param name="taskName">任务名。</param>
    /// <param name="capability">任务能力。</param>
    /// <param name="config">发出 ResinUsage 属性通知的配置。</param>
    /// <param name="read">读取并确保迁移后的设置。</param>
    /// <param name="write">整体替换配置属性，不原地修改嵌套值。</param>
    /// <param name="dialogs">弹窗服务。</param>
    public static ResinUsageEntryViewModel Create(string taskName, ResinUsageCapability capability,
        INotifyPropertyChanged config, Func<ResinUsage> read, Action<ResinUsage> write, IResinSettingsDialogs? dialogs)
    {
        var entry = new ResinUsageEntryViewModel(taskName, capability, read(), dialogs);
        entry.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(entry.Value) && read() != entry.Value)
                write(entry.Value);
        };
        config.PropertyChanged += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == "ResinUsage")
                entry.Reset(read());
        };
        return entry;
    }
}

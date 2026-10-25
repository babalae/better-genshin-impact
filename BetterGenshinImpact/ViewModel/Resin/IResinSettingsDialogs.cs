using BetterGenshinImpact.GameTask.Common.Resin;

namespace BetterGenshinImpact.ViewModel.Resin;

/// <summary>树脂设置弹窗入口；由视图层实现，调用方只接收确认后的新值。</summary>
public interface IResinSettingsDialogs
{
    /// <summary>复制并编辑树脂使用设置；取消时返回 null。</summary>
    ResinUsage? EditResinUsage(string taskName, ResinUsageCapability capability, ResinUsage current);

    /// <summary>编辑合成后保留量；返回 0 表示默认合成，取消时返回 null。</summary>
    int? EditCraftResin(int minResinToKeep);
}

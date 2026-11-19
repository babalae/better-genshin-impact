using BetterGenshinImpact.Service.I18n;

namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>
/// 方案管理窗口列表中的一项。
/// </summary>
/// <param name="Id">方案 ID。</param>
/// <param name="Name">方案名称。</param>
/// <param name="SettingCount">方案包含的设置项数。</param>
/// <param name="UsageCount">打开中的计划里使用它的节点数。</param>
public sealed record PuloniaSettingsProfileListItemViewModel(string Id, string Name, int SettingCount, int UsageCount)
{
    /// <summary>列表第二行的说明。</summary>
    public string Detail => UsageCount == 0
        ? string.Format(I18nService.Instance.Translate("{0} 项设置 · 未被使用"), SettingCount)
        : string.Format(I18nService.Instance.Translate("{0} 项设置 · 被 {1} 个节点使用"), SettingCount, UsageCount);
}

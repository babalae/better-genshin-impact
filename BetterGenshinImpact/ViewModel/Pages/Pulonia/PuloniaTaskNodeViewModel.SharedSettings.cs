using System.Linq;
using BetterGenshinImpact.Pulonia.Services;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>任务树与一条龙清单上的共享设置标记：本节点覆盖数量与所挂方案。</summary>
public partial class PuloniaTaskNodeViewModel
{
    /// <summary>本节点自身覆盖的共享设置项数量。</summary>
    public int SharedSettingCount => _model.Settings.Count;

    /// <summary>本节点是否有自身的共享设置覆盖。</summary>
    public bool HasSharedSettings => _model.Settings.Count > 0;

    /// <summary>本节点是否挂了设置方案。</summary>
    public bool HasProfile => _model.ProfileId is not null;

    /// <summary>所挂方案已不存在；运行准备会拒绝提交。</summary>
    public bool IsProfileMissing => _model.ProfileId is { } id && _document.FindProfile(id) is null;

    /// <summary>方案标签文字；缺失时提示。</summary>
    public string ProfileLabel => _model.ProfileId is not { } id
        ? string.Empty
        : _document.FindProfile(id)?.Name ?? "方案缺失";

    /// <summary>
    /// 计划卡片上的方案文字（取计划根节点）：所挂方案名、方案缺失提示或未使用方案。
    /// </summary>
    public string ProfileDisplayText => _model.ProfileId is not { } id
        ? "未使用设置方案"
        : _document.FindProfile(id) is { } profile ? "方案：" + profile.Name : "⚠ 设置方案已不存在";

    /// <summary>是否显示任一共享设置标记。</summary>
    public bool HasSharedSettingsBadge => HasSharedSettings || HasProfile;

    /// <summary>
    /// 本节点是否提供共享设置入口：分组、计划根和引用总是提供；叶子只有能力使用共享设置时提供。
    /// </summary>
    public bool UsesSharedSettings => !IsLeaf || _document.GetSharedSettingUsage(this).Keys is { Count: > 0 };

    /// <summary>共享设置摘要：所挂方案与本节点修改的项数；非叶子补充向下传播说明。</summary>
    public string SharedSettingsSummary
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();
            if (_model.ProfileId is { } id)
                parts.Add(_document.FindProfile(id) is { } profile ? $"方案「{profile.Name}」" : "⚠ 设置方案已不存在");
            parts.Add(_model.Settings.Count == 0 ? "本节点没有修改，全部沿用上层设置" : $"本节点修改了 {_model.Settings.Count} 项");
            return string.Join(" · ", parts) + (IsLeaf ? "" : "；修改会传给下面所有任务");
        }
    }

    /// <summary>悬停说明：方案与本节点覆盖的设置名和值。</summary>
    public string SharedSettingsToolTip
    {
        get
        {
            var lines = new System.Collections.Generic.List<string>();
            if (_model.ProfileId is { } id)
                lines.Add(_document.FindProfile(id) is { } profile
                    ? $"设置方案：{profile.Name}（{profile.Settings.Count} 项）"
                    : $"设置方案 {id} 已不存在，运行前请重新选择。");
            if (_model.Settings.Count > 0)
            {
                lines.Add($"本节点覆盖 {_model.Settings.Count} 项共享设置：");
                lines.AddRange(_model.Settings.Properties().Take(12).Select(property =>
                    "· " + (PuloniaSettingCatalog.TryGet(property.Name, out var definition) ? definition.DisplayName : property.Name)
                         + "：" + PuloniaSharedSettingText.Format(property.Name, property.Value)));
                if (_model.Settings.Count > 12)
                    lines.Add($"… 另有 {_model.Settings.Count - 12} 项");
            }
            return string.Join("\n", lines);
        }
    }

    /// <summary>共享设置或方案变化后刷新标记。</summary>
    internal void NotifySharedSettingsChanged()
    {
        OnPropertyChanged(nameof(SharedSettingCount));
        OnPropertyChanged(nameof(HasSharedSettings));
        OnPropertyChanged(nameof(HasProfile));
        OnPropertyChanged(nameof(IsProfileMissing));
        OnPropertyChanged(nameof(ProfileLabel));
        OnPropertyChanged(nameof(ProfileDisplayText));
        OnPropertyChanged(nameof(HasSharedSettingsBadge));
        OnPropertyChanged(nameof(SharedSettingsToolTip));
        OnPropertyChanged(nameof(SharedSettingsSummary));
    }
}

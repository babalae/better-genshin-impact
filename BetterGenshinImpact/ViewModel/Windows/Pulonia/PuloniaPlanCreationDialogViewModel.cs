using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Pulonia.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>
/// 新建任务计划弹窗的确认结果。
/// </summary>
/// <param name="Name">计划名称。</param>
/// <param name="ProfileId">挂在计划根节点上的设置方案 ID；为空表示不使用方案。</param>
public sealed record PuloniaPlanCreationResult(string Name, string? ProfileId);

/// <summary>
/// 新建任务计划弹窗视图模型：填写计划名称并选择计划级设置方案，确认前不创建计划。
/// </summary>
public partial class PuloniaPlanCreationDialogViewModel : ObservableObject
{
    /// <summary>可选择的设置方案，首项为“不使用方案”。</summary>
    public IReadOnlyList<PuloniaSettingsProfileOptionViewModel> ProfileOptions { get; }

    /// <summary>方案下拉框下方的说明；没有方案时提示新建入口。</summary>
    public string ProfileHint => ProfileOptions.Count > 1
        ? "方案中的设置会传给计划中的全部任务；之后可在“计划共享设置…”中更换。"
        : "还没有设置方案。可在计划列表右键“设置方案…”中新建，再到“计划共享设置…”中选用。";

    /// <summary>计划名称草稿。</summary>
    [ObservableProperty]
    private string _planName;

    /// <summary>当前选择的方案；下拉框清空选择时按不使用方案处理。</summary>
    [ObservableProperty]
    private PuloniaSettingsProfileOptionViewModel? _selectedProfileOption;

    /// <summary>校验失败等提示信息。</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>确认后的名称与方案；取消时为空。</summary>
    public PuloniaPlanCreationResult? Result { get; private set; }

    /// <summary>请求宿主窗口按确认（true）或取消（false）关闭。</summary>
    public event EventHandler<bool>? RequestClose;

    /// <summary>
    /// 建立新建任务计划弹窗视图模型。
    /// </summary>
    /// <param name="defaultName">预填的计划名称。</param>
    /// <param name="profiles">当前已加载的设置方案。</param>
    public PuloniaPlanCreationDialogViewModel(string defaultName, IEnumerable<PuloniaSettingsProfile> profiles)
    {
        _planName = defaultName;
        // 选项文字与共享设置窗口的方案下拉框一致，默认不使用方案
        ProfileOptions =
        [
            new PuloniaSettingsProfileOptionViewModel(null, "不使用方案"),
            .. profiles.Select(profile => new PuloniaSettingsProfileOptionViewModel(profile.Id,
                $"{profile.Name}（{profile.Settings.Count} 项）"))
        ];
        _selectedProfileOption = ProfileOptions[0];
    }

    /// <summary>
    /// 校验名称后记录结果并关闭弹窗。
    /// </summary>
    [RelayCommand]
    private void Confirm()
    {
        var name = PlanName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = "计划名称不能为空。";
            return;
        }
        Result = new PuloniaPlanCreationResult(name, SelectedProfileOption?.Id);
        RequestClose?.Invoke(this, true);
    }

    /// <summary>取消新建，不创建计划。</summary>
    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        RequestClose?.Invoke(this, false);
    }
}

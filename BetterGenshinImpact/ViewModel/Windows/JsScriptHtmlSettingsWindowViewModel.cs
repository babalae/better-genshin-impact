using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Windows;

/// <summary>
/// HTML 设置窗口的显示状态：标题中的保存时间与页面加载错误。页面内容和保存按钮全部由脚本作者实现。
/// </summary>
/// <param name="scriptName">脚本名称。</param>
/// <param name="scriptVersion">脚本版本。</param>
public partial class JsScriptHtmlSettingsWindowViewModel(string scriptName, string scriptVersion) : ObservableObject
{
    /// <summary>
    /// 最近一次保存成功的时间；未保存时为空。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private DateTime? _lastSavedTime;

    /// <summary>
    /// 页面加载失败或网页进程退出的说明；正常时为空。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    /// <summary>
    /// 窗口标题。WebView2 是原生窗口，WPF 内容不能叠在页面上，保存时间显示在标题中，作者没做保存提示时用户也能确认。
    /// </summary>
    public string Title => $"脚本设置：{scriptName} {scriptVersion} · 页面由脚本作者提供"
                           + (LastSavedTime is { } time ? $" · 已保存 {time:HH:mm:ss}" : string.Empty);

    /// <summary>
    /// 是否显示错误提示条。
    /// </summary>
    public bool HasError => ErrorMessage is not null;
}

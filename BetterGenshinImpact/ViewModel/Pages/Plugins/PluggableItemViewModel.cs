using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using BetterGenshinImpact.Core.Plugins.Pluggables;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages.Plugins;

/// <summary>
/// 一项可插拔能力的选择控件：可选实现、当前选择和回退提示。
/// 插件页“功能选择”的每一行使用它；功能自己的设置界面（如设置页的 OCR 配置）也可以使用，各处的选择通过注册表保持同步。
/// </summary>
public sealed partial class PluggableItemViewModel : ObservableObject
{
    /// <summary>
    /// 可插拔能力注册表。
    /// </summary>
    private readonly PluggableRegistry _registry;

    /// <summary>
    /// 正在按注册表的状态刷新界面，期间的选择变化不写回注册表。
    /// </summary>
    private bool _syncing;

    /// <summary>
    /// 可选实现列表。
    /// </summary>
    public ObservableCollection<PluggableImplOption> Options { get; } = [];

    /// <summary>
    /// 当前选中的实现。
    /// </summary>
    [ObservableProperty]
    private PluggableImplOption? _selectedOption;

    /// <summary>
    /// 回退提示：保存的实现不可用或加载失败时说明当前实际使用的实现。
    /// </summary>
    [ObservableProperty]
    private string? _fallbackText;

    /// <summary>
    /// 是否正在回退到默认实现。
    /// </summary>
    [ObservableProperty]
    private bool _hasFallback;

    /// <summary>
    /// 创建一行功能选择。
    /// </summary>
    /// <param name="registry">可插拔能力注册表。</param>
    /// <param name="descriptor">可插拔能力。</param>
    public PluggableItemViewModel(PluggableRegistry registry, PluggableDescriptor descriptor)
    {
        _registry = registry;
        Descriptor = descriptor;
        // 实现增减、选择变化（包括在其他界面改选）、创建失败时刷新；事件可能来自任意线程，切回界面线程处理。
        // 使用方都是单例 ViewModel，订阅随进程结束，不需要取消
        _registry.Changed += (_, e) =>
        {
            if (e.PluggableId == Descriptor.Id)
            {
                _ = Application.Current?.Dispatcher.InvokeAsync(Refresh);
            }
        };
        Refresh();
    }

    /// <summary>
    /// 可插拔能力。
    /// </summary>
    public PluggableDescriptor Descriptor { get; }

    /// <summary>
    /// 显示名称。
    /// </summary>
    public string DisplayName => Descriptor.DisplayName;

    /// <summary>
    /// 说明。
    /// </summary>
    public string Description => Descriptor.Description ?? string.Empty;

    /// <summary>
    /// 按注册表的当前状态重建选项和选择。在界面线程调用。
    /// </summary>
    public void Refresh()
    {
        var state = _registry.GetSelection(Descriptor.Id);
        _syncing = true;
        try
        {
            Options.Clear();
            foreach (var implementation in _registry.GetImplementations(Descriptor.Id))
            {
                Options.Add(new PluggableImplOption(
                    implementation.Id,
                    $"{implementation.DisplayName} · {implementation.PluginName ?? "内置"}",
                    false));
            }

            // 保存的实现来自未启用的插件：保留一项占位并选中，用户主动改选之前原来的选择不会丢失
            if (state is { SavedImplementationId: { } savedId, Saved: null })
            {
                var placeholder = new PluggableImplOption(savedId, $"{savedId}（插件未启用）", true);
                Options.Add(placeholder);
                SelectedOption = placeholder;
            }
            else
            {
                var selectedId = state.Saved?.Id ?? state.Effective.Id;
                SelectedOption = Options.FirstOrDefault(o => o.Id == selectedId);
            }

            HasFallback = state.IsFallback;
            FallbackText = state switch
            {
                { IsFallback: true, SavedFailed: true } =>
                    $"{state.Saved!.DisplayName} 加载失败，当前使用 {state.Effective.DisplayName}，原因见日志。",
                { IsFallback: true } => $"所选实现来自未启用的插件，当前使用 {state.Effective.DisplayName}。",
                _ => null,
            };
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>
    /// 用户改选实现时保存选择，下一次调用即使用新实现。
    /// </summary>
    partial void OnSelectedOptionChanged(PluggableImplOption? value)
    {
        if (_syncing || value is null || value.IsUnavailable)
        {
            return;
        }

        // 注册表随后触发 Changed，各处的选择控件据此刷新回退提示
        _registry.Select(Descriptor.Id, value.Id);
    }
}

/// <summary>
/// 功能选择下拉框中的一项。
/// </summary>
/// <param name="Id">实现标识。</param>
/// <param name="Text">显示文字：“显示名 · 来源”。</param>
/// <param name="IsUnavailable">是否为未启用插件的占位项，选中它不会保存。</param>
public sealed record PluggableImplOption(string Id, string Text, bool IsUnavailable);

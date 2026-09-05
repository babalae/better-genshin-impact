using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.ComponentModel;

namespace BetterGenshinImpact.Model;

public partial class StatusItem : ObservableObject, IDisposable
{
    public string Name { get; set; }
    private INotifyPropertyChanged _sourceObject { get; set; }
    private string _propertyName { get; set; }

    /// <summary>
    /// 关联的 HotKeyConfig 快捷键属性名（如 nameof(HotKeyConfig.AutoPickEnabledHotkey)）；
    /// 为 null 表示该状态项没有可绑定的快捷键（如尚未支持的功能）。
    /// </summary>
    public string? HotkeyConfigPropertyName { get; set; }

    /// <summary>
    /// 关联的 HotKeyConfig 快捷键类型属性名（HotkeyConfigPropertyName + "Type"）
    /// </summary>
    public string? HotkeyTypeConfigPropertyName { get; set; }

    [ObservableProperty] private bool _isEnabled;

    /// <summary>
    /// 已绑定且实际生效的快捷键紧凑文本（如 "Alt+G"）；
    /// 未绑定或键鼠监听下的组合键（无法触发）时为空字符串，视图层据此隐藏徽章。
    /// </summary>
    [ObservableProperty] private string _hotkeyText = string.Empty;

    public StatusItem(string name, INotifyPropertyChanged sourceObject, string propertyName = "Enabled")
    {
        Name = name;
        _sourceObject = sourceObject;
        _propertyName = propertyName;

        _sourceObject.PropertyChanged += OnSourcePropertyChanged;
        IsEnabled = GetSourceValue();
    }

    /// <summary>
    /// 带快捷键徽章的状态项
    /// </summary>
    /// <param name="name">显示名</param>
    /// <param name="sourceObject">功能开关所在的配置对象</param>
    /// <param name="propertyName">开关属性名</param>
    /// <param name="hotkeyConfigPropertyName">HotKeyConfig 上的快捷键属性名</param>
    public StatusItem(string name, INotifyPropertyChanged sourceObject, string propertyName, string? hotkeyConfigPropertyName)
        : this(name, sourceObject, propertyName)
    {
        if (hotkeyConfigPropertyName != null)
        {
            HotkeyConfigPropertyName = hotkeyConfigPropertyName;
            HotkeyTypeConfigPropertyName = hotkeyConfigPropertyName + "Type";
        }

        RefreshHotkey();
    }

    private bool GetSourceValue()
    {
        var property = _sourceObject.GetType().GetProperty(_propertyName);
        ArgumentNullException.ThrowIfNull(property);
        var value = property.GetValue(_sourceObject);
        ArgumentNullException.ThrowIfNull(value);
        return (bool)value;
    }

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == _propertyName)
        {
            this.IsEnabled = GetSourceValue();
        }
    }

    /// <summary>
    /// 重新读取 HotKeyConfig 计算快捷键文本。
    /// StatusList 仅在遮罩加载时初始化一次，用户在快捷键设置页改键后，
    /// 由 MaskWindowViewModel.RefreshSettings 调用本方法刷新徽章，避免重建集合。
    /// </summary>
    public void RefreshHotkey()
    {
        if (HotkeyConfigPropertyName == null)
        {
            return;
        }

        try
        {
            var hotKeyConfig = TaskContext.Instance().Config.HotKeyConfig;
            var value = hotKeyConfig.GetType().GetProperty(HotkeyConfigPropertyName)?.GetValue(hotKeyConfig) as string;
            var type = hotKeyConfig.GetType().GetProperty(HotkeyTypeConfigPropertyName ?? "")?.GetValue(hotKeyConfig) as string;

            HotkeyText = OverlayHotkeyItemDefaults.IsHotkeyEffective(value, type) && !string.IsNullOrWhiteSpace(value)
                ? OverlayHotkeyItemDefaults.ToCompactHotkeyText(value)
                : string.Empty;
        }
        catch (Exception)
        {
            // 配置尚未加载等异常场景下不显示徽章即可，不中断状态栏初始化
            HotkeyText = string.Empty;
        }
    }

    public void Dispose()
    {
        _sourceObject.PropertyChanged -= OnSourcePropertyChanged;
    }
}

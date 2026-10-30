using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Xaml.Behaviors;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>保存表格初始列宽，并响应所有账号记录分页共享的恢复信号。</summary>
public sealed class AccountRecordsColumnWidthsBehavior : Behavior<DataGrid>
{
    /// <summary>初次显示时的声明列宽，避免重复维护宽度常量。</summary>
    private readonly Dictionary<DataGridColumn, DataGridLength> _defaults = [];
    /// <summary>最后处理的恢复信号，隐藏分页在重新加载时补做恢复。</summary>
    private int _appliedVersion;
    /// <summary>由页面统一递增的恢复信号。</summary>
    public static readonly DependencyProperty ResetVersionProperty = DependencyProperty.Register(
        nameof(ResetVersion), typeof(int), typeof(AccountRecordsColumnWidthsBehavior),
        new PropertyMetadata(0, (sender, _) => ((AccountRecordsColumnWidthsBehavior)sender).Restore()));
    /// <summary>绑定页面的恢复信号。</summary>
    public int ResetVersion
    {
        get => (int)GetValue(ResetVersionProperty);
        set => SetValue(ResetVersionProperty, value);
    }

    /// <summary>列声明完成后记录默认宽度。</summary>
    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.Loaded += OnLoaded;
        if (AssociatedObject.IsLoaded) OnLoaded(AssociatedObject, new RoutedEventArgs());
    }

    /// <summary>首次记录，后续加载只处理尚未应用的恢复信号。</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        foreach (var column in AssociatedObject.Columns)
            _defaults.TryAdd(column, column.Width);
        Restore();
    }

    /// <summary>恢复列宽，不修改显示顺序、排序或选中行。</summary>
    private void Restore()
    {
        if (_defaults.Count == 0 || _appliedVersion == ResetVersion) return;
        foreach (var pair in _defaults) pair.Key.SetCurrentValue(DataGridColumn.WidthProperty, pair.Value);
        _appliedVersion = ResetVersion;
    }

    /// <summary>解除视图事件引用。</summary>
    protected override void OnDetaching()
    {
        AssociatedObject.Loaded -= OnLoaded;
        _defaults.Clear();
        base.OnDetaching();
    }
}

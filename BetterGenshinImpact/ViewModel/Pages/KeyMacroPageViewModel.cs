using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.KeyMacro;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service.KeyMacro;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Pages.KeyMacro;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// 按键宏页面：左侧宏列表，右侧编辑选中的宏。列表与 User/BgiMacro 目录同步
/// </summary>
public partial class KeyMacroPageViewModel : ViewModel
{
    private readonly ILogger<KeyMacroPageViewModel> _logger = App.GetLogger<KeyMacroPageViewModel>();
    private readonly KeyMacroStore _store;
    private readonly KeyMacroHotkeyHost _host;

    /// <summary>
    /// 配置根，页面绑定总开关
    /// </summary>
    public AllConfig Config { get; }

    /// <summary>
    /// 全部宏，顺序与宏目录一致（按名称排序）
    /// </summary>
    public ObservableCollection<KeyMacroItemViewModel> Items { get; } = [];

    /// <summary>
    /// 选中的宏
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private KeyMacroItemViewModel? _selectedItem;

    /// <summary>
    /// 新建模板
    /// </summary>
    public IReadOnlyList<KeyMacroTemplate> Templates => KeyMacroTemplates.All;

    /// <summary>
    /// 是否选中了宏
    /// </summary>
    public bool HasSelection => SelectedItem != null;

    /// <summary>
    /// 是否有宏
    /// </summary>
    public bool HasItems => Items.Count > 0;

    /// <summary>
    /// 宏目录
    /// </summary>
    public string DirectoryPath => _store.DirectoryPath;

    /// <summary>
    /// 创建页面 ViewModel
    /// </summary>
    public KeyMacroPageViewModel(KeyMacroStore store, KeyMacroHotkeyHost host, IConfigService configService)
    {
        _store = store;
        _host = host;
        Config = configService.Get();
        _store.EnsureLoaded();
        _store.Macros.CollectionChanged += OnMacrosChanged;
        _host.StatusChanged += OnHostStatusChanged;
        Config.KeyMacroConfig.PropertyChanged += (_, _) => UpdateAllStatus();
        SyncItems();
        SelectedItem = Items.FirstOrDefault();
    }

    /// <inheritdoc />
    public override void OnNavigatedTo()
    {
        // 监听可能失败或有延迟，进入页面时重新扫描一次目录
        _store.Reload();
    }

    /// <inheritdoc />
    public override void OnNavigatedFrom()
    {
        _store.Flush();
    }

    /// <summary>
    /// 按模板新建宏并选中
    /// </summary>
    [RelayCommand]
    private void Create(KeyMacroTemplate? template)
    {
        template ??= KeyMacroTemplates.All[0];
        try
        {
            var definition = _store.Create(template.Name, template.Mode, template.Script);
            SelectedItem = Items.FirstOrDefault(i => ReferenceEquals(i.Definition, definition));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(e, "新建按键宏失败");
            Toast.Error($"新建按键宏失败：{e.Message}");
        }
    }

    /// <summary>
    /// 复制选中的宏
    /// </summary>
    [RelayCommand]
    private void Duplicate(KeyMacroItemViewModel? item)
    {
        item ??= SelectedItem;
        if (item == null)
        {
            return;
        }

        try
        {
            _store.Flush();
            var definition = _store.Duplicate(item.Definition);
            SelectedItem = Items.FirstOrDefault(i => ReferenceEquals(i.Definition, definition));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(e, "复制按键宏失败");
            Toast.Error($"复制按键宏失败：{e.Message}");
        }
    }

    /// <summary>
    /// 删除选中的宏：确认后文件移到回收站
    /// </summary>
    [RelayCommand]
    private void Delete(KeyMacroItemViewModel? item)
    {
        item ??= SelectedItem;
        if (item == null)
        {
            return;
        }

        var result = ThemedMessageBox.Question($"确定删除按键宏「{item.Name}」吗？文件会移到回收站。", "删除按键宏",
            MessageBoxButton.OKCancel, MessageBoxResult.Cancel);
        if (result != MessageBoxResult.OK)
        {
            return;
        }

        var index = Items.IndexOf(item);
        try
        {
            _store.Delete(item.Definition);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            _logger.LogError(e, "删除按键宏失败");
            Toast.Error($"删除按键宏失败：{e.Message}");
            return;
        }

        SelectedItem = Items.Count == 0 ? null : Items[Math.Clamp(index, 0, Items.Count - 1)];
    }

    /// <summary>
    /// 导入 txt 宏文件，快捷键清空以免与本机已有快捷键冲突
    /// </summary>
    [RelayCommand]
    private void Import()
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入按键宏",
            Filter = "按键宏 (*.txt)|*.txt",
            Multiselect = true
        };
        if (dialog.ShowDialog(Application.Current.MainWindow) != true)
        {
            return;
        }

        var imported = _store.Import(dialog.FileNames);
        if (imported.Count == 0)
        {
            Toast.Warning("没有导入任何宏，请查看日志");
            return;
        }

        SelectedItem = Items.FirstOrDefault(i => ReferenceEquals(i.Definition, imported[0]));
        Toast.Success($"已导入 {imported.Count} 个宏，请为它们设置快捷键");
    }

    /// <summary>
    /// 在资源管理器中打开宏目录
    /// </summary>
    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(_store.DirectoryPath);
            Process.Start("explorer.exe", _store.DirectoryPath);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "打开按键宏目录失败");
            Toast.Error($"无法打开目录：{e.Message}");
        }
    }

    /// <summary>
    /// 宏目录变化后同步列表
    /// </summary>
    private void OnMacrosChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncItems();
    }

    /// <summary>
    /// 与宏目录同步：复用已有的编辑对象，按目录顺序排列，保留选中项
    /// </summary>
    private void SyncItems()
    {
        var existing = Items.ToDictionary(i => i.Definition);
        var desired = _store.Macros
            .Select(d => existing.TryGetValue(d, out var vm) ? vm : new KeyMacroItemViewModel(d, _store, _host, Config))
            .ToList();

        // 移除已删除的宏
        foreach (var removed in Items.Except(desired).ToList())
        {
            Items.Remove(removed);
            removed.Dispose();
        }

        // 按目录顺序插入或移动，不重建整个集合，避免列表失去选中状态
        for (var i = 0; i < desired.Count; i++)
        {
            var current = Items.IndexOf(desired[i]);
            if (current < 0)
            {
                Items.Insert(i, desired[i]);
            }
            else if (current != i)
            {
                Items.Move(current, i);
            }
        }

        if (SelectedItem != null && !Items.Contains(SelectedItem))
        {
            SelectedItem = Items.FirstOrDefault();
        }

        OnPropertyChanged(nameof(HasItems));
    }

    /// <summary>
    /// 热键注册或运行状态变化
    /// </summary>
    private void OnHostStatusChanged(object? sender, EventArgs e)
    {
        UpdateAllStatus();
    }

    /// <summary>
    /// 刷新全部宏的状态显示
    /// </summary>
    private void UpdateAllStatus()
    {
        foreach (var item in Items)
        {
            item.UpdateStatus();
        }
    }
}

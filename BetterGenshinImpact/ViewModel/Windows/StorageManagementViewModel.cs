using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Infrastructure.Storage;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel.Windows;

/// <summary>
/// 存储空间窗口：按分组展示各存储区域的占用，提供逐项清理和一键清理
/// </summary>
public partial class StorageManagementViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// 不计入“占用总览”的分组：用户数据不由程序管理
    /// </summary>
    private static readonly StorageGroup[] ExcludedFromOverview = [StorageGroup.UserData];

    /// <summary>
    /// 存储区域的统计与清理
    /// </summary>
    private readonly IStorageService _storageService;

    /// <summary>
    /// 日志
    /// </summary>
    private readonly ILogger<StorageManagementViewModel> _logger;

    /// <summary>
    /// 取消进行中的统计；重新计算或关闭窗口时取消
    /// </summary>
    private CancellationTokenSource _measureCts = new();

    public StorageManagementViewModel(IStorageService storageService, IConfigService configService,
        ILogger<StorageManagementViewModel> logger)
    {
        _storageService = storageService;
        _logger = logger;
        StorageConfig = configService.Get().StorageConfig;

        // 配置里是选项之外的值（手动修改过配置文件）时补进列表，避免下拉框显示为空
        var limit = StorageConfig.FileCacheLimitMb;
        CacheLimitOptions = CacheLimitPresets.Any(option => option.Key == limit)
            ? CacheLimitPresets
            : [.. CacheLimitPresets, new KeyValuePair<int, string>(limit, ByteSizeFormatter.Format(limit * 1024L * 1024))];
    }

    /// <summary>
    /// 文件缓存上限的预设选项（MB）
    /// </summary>
    private static readonly IReadOnlyList<KeyValuePair<int, string>> CacheLimitPresets =
    [
        new(256, "256 MB"),
        new(512, "512 MB"),
        new(1024, "1 GB"),
        new(2048, "2 GB"),
        new(5120, "5 GB"),
    ];

    /// <summary>
    /// 存储空间设置，下拉框直接绑定其中的文件缓存上限，修改后自动保存
    /// </summary>
    public StorageConfig StorageConfig { get; }

    /// <summary>
    /// 文件缓存上限的下拉选项
    /// </summary>
    public IReadOnlyList<KeyValuePair<int, string>> CacheLimitOptions { get; }

    /// <summary>
    /// 请求关闭窗口
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// 程序生成数据的目录
    /// </summary>
    public string DataDirectory => AppPaths.Root;

    /// <summary>
    /// 分组列表
    /// </summary>
    public ObservableCollection<StorageGroupViewModel> Groups { get; } = [];

    /// <summary>
    /// 占用比例条的分段
    /// </summary>
    public ObservableCollection<StorageOverviewSegment> OverviewSegments { get; } = [];

    /// <summary>
    /// 占用总览的总大小
    /// </summary>
    [ObservableProperty]
    private string _totalSizeText = "计算中…";

    /// <summary>
    /// 一键清理的说明
    /// </summary>
    [ObservableProperty]
    private string _quickCleanupText = "正在统计可清理的内容…";

    /// <summary>
    /// 是否正在统计
    /// </summary>
    [ObservableProperty]
    private bool _isMeasuring;

    /// <summary>
    /// 一键清理可释放的字节数
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(QuickCleanupCommand))]
    private long _quickCleanupBytes;

    /// <summary>
    /// 全部行
    /// </summary>
    private IEnumerable<StorageAreaItemViewModel> AllItems => Groups.SelectMany(group => group.Items);

    /// <summary>
    /// 重新读取存储区域并统计占用。窗口加载时和点击“重新计算”时执行
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        await _measureCts.CancelAsync();
        _measureCts.Dispose();
        _measureCts = new CancellationTokenSource();
        var ct = _measureCts.Token;

        BuildGroups();
        IsMeasuring = true;
        TotalSizeText = "计算中…";
        try
        {
            // 各行并行统计，完成一行显示一行
            await Task.WhenAll(AllItems.Select(item => MeasureAsync(item, ct)));
        }
        catch (OperationCanceledException)
        {
            return;
        }

        IsMeasuring = false;
        foreach (var group in Groups)
        {
            group.SortBySize();
        }

        UpdateOverview();
    }

    /// <summary>
    /// 清理一行。会丢失用户数据的区域先确认
    /// </summary>
    [RelayCommand]
    private async Task CleanupAsync(StorageAreaItemViewModel? item)
    {
        if (item is not { CanClean: true })
        {
            return;
        }

        if (item.Area.RequiresConfirmation)
        {
            var confirm = await ThemedMessageBox.WarningAsync(
                $"确定清除「{item.DisplayName}」（{item.SizeText}）吗？\n{item.Area.CleanupEffect}",
                "清除确认", MessageBoxButton.OKCancel);
            if (confirm != MessageBoxResult.OK)
            {
                return;
            }
        }

        var result = await CleanupItemAsync(item);
        if (result != null)
        {
            Toast.Success(Describe(result));
        }

        UpdateOverview();
    }

    /// <summary>
    /// 一键清理：列出将清理的内容并确认后依次清理
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanQuickCleanup))]
    private async Task QuickCleanupAsync()
    {
        var items = AllItems.Where(item => item.IsQuickCleanable && item.CanClean).ToList();
        if (items.Count == 0)
        {
            return;
        }

        var list = string.Join("\n", items.Select(item => $"· {item.DisplayName}（{item.SizeText}）"));
        var confirm = await ThemedMessageBox.QuestionAsync(
            $"将清理以下内容，共约 {ByteSizeFormatter.Format(QuickCleanupBytes)}：\n{list}\n\n不会删除脚本、配置、截图和登录状态。",
            "一键清理", MessageBoxButton.OKCancel);
        if (confirm != MessageBoxResult.OK)
        {
            return;
        }

        var total = StorageCleanupResult.Empty;
        foreach (var item in items)
        {
            total = total.Add(await CleanupItemAsync(item) ?? StorageCleanupResult.Empty);
        }

        Toast.Success(Describe(total));
        UpdateOverview();
    }

    /// <summary>
    /// 一键清理是否可用
    /// </summary>
    private bool CanQuickCleanup() => QuickCleanupBytes > 0;

    /// <summary>
    /// 打开区域所在目录
    /// </summary>
    [RelayCommand]
    private void OpenFolder(StorageAreaItemViewModel? item)
    {
        if (item?.FolderPath is { } folder)
        {
            OpenInExplorer(folder);
        }
    }

    /// <summary>
    /// 打开数据目录
    /// </summary>
    [RelayCommand]
    private void OpenDataFolder()
    {
        Directory.CreateDirectory(DataDirectory);
        OpenInExplorer(DataDirectory);
    }

    /// <summary>
    /// 关闭窗口
    /// </summary>
    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// 关闭窗口时取消进行中的统计
    /// </summary>
    public void Dispose()
    {
        _measureCts.Cancel();
        _measureCts.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 按分组的固定顺序重建列表
    /// </summary>
    private void BuildGroups()
    {
        Groups.Clear();
        foreach (var areas in _storageService.GetAreas().GroupBy(area => area.Group).OrderBy(group => group.Key))
        {
            var group = StorageGroupViewModel.Create(areas.Key);
            foreach (var area in areas)
            {
                group.Items.Add(new StorageAreaItemViewModel(area, _storageService.IsCleanupPending(area))
                {
                    IsFirst = group.Items.Count == 0,
                });
            }

            Groups.Add(group);
        }
    }

    /// <summary>
    /// 统计一行的占用；统计失败时显示为未知
    /// </summary>
    private async Task MeasureAsync(StorageAreaItemViewModel item, CancellationToken ct)
    {
        item.IsMeasuring = true;
        try
        {
            item.SizeBytes = await _storageService.MeasureAsync(item.Area, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "统计存储区域 {Area} 失败", item.Area.Id);
        }
        finally
        {
            item.IsMeasuring = false;
        }
    }

    /// <summary>
    /// 清理一行并重新统计；不能清理或清理失败时提示原因并返回 null
    /// </summary>
    private async Task<StorageCleanupResult?> CleanupItemAsync(StorageAreaItemViewModel item)
    {
        item.IsCleaning = true;
        try
        {
            var result = await _storageService.CleanupAsync(item.Area, CancellationToken.None);
            if (result.Deferred)
            {
                item.IsPending = true;
            }
            else
            {
                item.SizeBytes = await _storageService.MeasureAsync(item.Area, CancellationToken.None);
            }

            return result;
        }
        catch (InvalidOperationException e)
        {
            Toast.Warning(e.Message);
            return null;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "清理存储区域 {Area} 失败", item.Area.Id);
            Toast.Error($"清理「{item.DisplayName}」失败：{e.Message}");
            return null;
        }
        finally
        {
            item.IsCleaning = false;
        }
    }

    /// <summary>
    /// 更新占用总览和一键清理的说明
    /// </summary>
    private void UpdateOverview()
    {
        var overviewGroups = Groups.Where(group => !ExcludedFromOverview.Contains(group.Group)).ToList();
        TotalSizeText = $"共 {ByteSizeFormatter.Format(overviewGroups.Sum(group => group.SizeBytes))}";

        OverviewSegments.Clear();
        foreach (var group in overviewGroups.Where(group => group.SizeBytes > 0))
        {
            OverviewSegments.Add(StorageOverviewSegment.From(group));
        }

        QuickCleanupBytes = AllItems.Where(item => item.IsQuickCleanable && item.CanClean).Sum(item => item.SizeBytes ?? 0);
        QuickCleanupText = QuickCleanupBytes > 0
            ? $"可安全清理 {ByteSizeFormatter.Format(QuickCleanupBytes)}，不会删除脚本、配置、截图和登录状态"
            : "没有可以安全清理的内容";
    }

    /// <summary>
    /// 用一句话描述清理结果
    /// </summary>
    private static string Describe(StorageCleanupResult result)
    {
        var text = result.FreedBytes > 0 || !result.Deferred
            ? $"已释放 {ByteSizeFormatter.Format(result.FreedBytes)}"
            : "";
        if (result.Deferred)
        {
            text += text.Length > 0 ? "，部分内容将在下次启动时清理" : "将在下次启动时清理";
        }

        if (result.SkippedFiles > 0)
        {
            text += $"，{result.SkippedFiles} 个文件正在使用，已跳过";
        }

        return text;
    }

    /// <summary>
    /// 在资源管理器中打开目录
    /// </summary>
    private void OpenInExplorer(string folder)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "打开目录失败：{Folder}", folder);
        }
    }
}

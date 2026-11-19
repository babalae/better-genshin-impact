using System.IO;
using System.Linq;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Infrastructure.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using BetterGenshinImpact.Service.I18n;

namespace BetterGenshinImpact.ViewModel.Windows;

/// <summary>
/// 存储空间窗口中的一行：一个存储区域的占用、说明和可用操作
/// </summary>
public partial class StorageAreaItemViewModel : ObservableObject
{
    /// <param name="area">对应的存储区域</param>
    /// <param name="isPending">是否已登记、等待下次启动清理</param>
    public StorageAreaItemViewModel(StorageArea area, bool isPending)
    {
        Area = area;
        _isPending = isPending;
        BusyReason = area.GetBusyReason?.Invoke();
        FolderPath = FindFolder(area);
    }

    /// <summary>
    /// 对应的存储区域
    /// </summary>
    public StorageArea Area { get; }

    /// <summary>
    /// 展示名称
    /// </summary>
    public string DisplayName => I18nService.Instance.Translate(Area.DisplayName);

    /// <summary>
    /// 打开窗口时区域不能清理的原因；为 null 表示可以清理
    /// </summary>
    public string? BusyReason { get; }

    /// <summary>
    /// 说明文字：先说明内容，再说明清理影响；不能清理时直接写明原因
    /// </summary>
    public string DetailText => string.Join(" · ", new[] { Area.Description, BusyReason ?? Area.CleanupEffect }
        .Where(text => !string.IsNullOrWhiteSpace(text))
        .Select(text => I18nService.Instance.Translate(text)));

    /// <summary>
    /// 按界面语言显示的清理影响说明，用于清除确认
    /// </summary>
    public string CleanupEffectText => I18nService.Instance.Translate(Area.CleanupEffect);

    /// <summary>
    /// “打开”按钮打开的目录；区域内没有已存在的目录时为 null
    /// </summary>
    public string? FolderPath { get; }

    /// <summary>
    /// 是否显示“打开”按钮
    /// </summary>
    public bool CanOpenFolder => FolderPath != null;

    /// <summary>
    /// 是否提供清理操作；只展示占用的区域不显示清理按钮
    /// </summary>
    public bool IsCleanable => Area.Timing != StorageCleanupTiming.None;

    /// <summary>
    /// 清理按钮文字；会丢失用户数据的操作带省略号，表示会先确认
    /// </summary>
    public string CleanButtonText => Area.RequiresConfirmation
        ? I18nService.Instance.Translate("清除…")
        : I18nService.Instance.Translate("清理");

    /// <summary>
    /// 是否参与一键清理
    /// </summary>
    public bool IsQuickCleanable => IsCleanable && Area.IncludedInQuickCleanup && !Area.RequiresConfirmation;

    /// <summary>
    /// 占用的字节数；尚未统计完成时为 null
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText), nameof(CanClean))]
    private long? _sizeBytes;

    /// <summary>
    /// 是否正在统计
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText), nameof(IsWorking))]
    private bool _isMeasuring;

    /// <summary>
    /// 是否正在清理
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanClean), nameof(IsWorking))]
    private bool _isCleaning;

    /// <summary>
    /// 是否已登记、等待下次启动清理
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText), nameof(CanClean))]
    private bool _isPending;

    /// <summary>
    /// 是否为分组中的第一行，第一行上方不画分隔线
    /// </summary>
    [ObservableProperty]
    private bool _isFirst;

    /// <summary>
    /// 是否显示进度环
    /// </summary>
    public bool IsWorking => IsMeasuring || IsCleaning;

    /// <summary>
    /// 清理按钮是否可用：有内容、当前可清理且没有进行中的操作
    /// </summary>
    public bool CanClean => IsCleanable && BusyReason == null && !IsCleaning && !IsPending && SizeBytes > 0;

    /// <summary>
    /// 占用文字
    /// </summary>
    public string SizeText
    {
        get
        {
            if (SizeBytes is not { } bytes)
            {
                return IsMeasuring ? I18nService.Instance.Translate("计算中…") : "—";
            }

            var text = ByteSizeFormatter.Format(bytes);
            return IsPending ? string.Format(I18nService.Instance.Translate("{0} · 待清理"), text) : text;
        }
    }

    /// <summary>
    /// 找到区域中第一个已存在的目录；只有文件时取文件所在目录
    /// </summary>
    private static string? FindFolder(StorageArea area)
    {
        foreach (var path in area.Paths)
        {
            if (Directory.Exists(path))
            {
                return path;
            }

            if (File.Exists(path))
            {
                return Path.GetDirectoryName(path);
            }
        }

        return null;
    }
}

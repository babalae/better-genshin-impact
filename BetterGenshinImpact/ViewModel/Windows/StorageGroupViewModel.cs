using System.Collections.ObjectModel;
using System.Linq;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Infrastructure.Storage;

namespace BetterGenshinImpact.ViewModel.Windows;

/// <summary>
/// 存储空间窗口中的一个分组，例如“缓存”“下载”
/// </summary>
public sealed class StorageGroupViewModel
{
    /// <param name="group">分组</param>
    /// <param name="name">分组名称</param>
    /// <param name="description">分组说明</param>
    /// <param name="color">比例条颜色</param>
    private StorageGroupViewModel(StorageGroup group, string name, string description, string color)
    {
        Group = group;
        Name = name;
        Description = description;
        Color = color;
    }

    /// <summary>
    /// 分组
    /// </summary>
    public StorageGroup Group { get; }

    /// <summary>
    /// 分组名称
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 分组说明，告诉用户这组内容清理后会怎样
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// 分组在占用比例条中的颜色（#RRGGBB）
    /// </summary>
    public string Color { get; }

    /// <summary>
    /// 分组内的区域
    /// </summary>
    public ObservableCollection<StorageAreaItemViewModel> Items { get; } = [];

    /// <summary>
    /// 分组已统计的总占用
    /// </summary>
    public long SizeBytes => Items.Sum(item => item.SizeBytes ?? 0);

    /// <summary>
    /// 按占用从大到小排序，并更新分隔线
    /// </summary>
    public void SortBySize()
    {
        var sorted = Items.OrderByDescending(item => item.SizeBytes ?? 0).ToList();
        for (var index = 0; index < sorted.Count; index++)
        {
            var current = Items.IndexOf(sorted[index]);
            if (current != index)
            {
                Items.Move(current, index);
            }

            sorted[index].IsFirst = index == 0;
        }
    }

    /// <summary>
    /// 创建分组；名称、说明和颜色在这里统一定义
    /// </summary>
    public static StorageGroupViewModel Create(StorageGroup group) => group switch
    {
        StorageGroup.Cache => new(group, "缓存", "删除后会在需要时重新下载或生成", "#4F7EDB"),
        StorageGroup.Download => new(group, "下载", "安装包和未完成的下载", "#E8A33D"),
        StorageGroup.WebData => new(group, "网页数据", "软件内网页和云原神网页版的数据", "#3BB273"),
        StorageGroup.Temp => new(group, "临时文件", "程序运行中产生，退出时自动删除", "#9B6BD6"),
        StorageGroup.Diagnostics => new(group, "日志与诊断", "排查问题用的日志和截图", "#8A8F98"),
        _ => new(group, "用户数据", "不会被一键清理删除", "#D9644A"),
    };
}

/// <summary>
/// 占用比例条中的一段
/// </summary>
/// <param name="Label">图例文字，例如“缓存 1.2 GB”</param>
/// <param name="Color">颜色（#RRGGBB）</param>
/// <param name="Weight">占用字节数，决定宽度比例</param>
public sealed record StorageOverviewSegment(string Label, string Color, double Weight)
{
    /// <summary>
    /// 由分组生成比例条分段
    /// </summary>
    public static StorageOverviewSegment From(StorageGroupViewModel group) =>
        new($"{group.Name} {ByteSizeFormatter.Format(group.SizeBytes)}", group.Color, group.SizeBytes);
}

using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BetterGenshinImpact.GameTask.MapMask;

[Serializable]
public sealed class MapMaskDataSourceState
{
    /// <summary>
    /// 当前数据源下用户勾选的点位标签。
    /// </summary>
    public List<MapMaskSelectedLabelState> SelectedLabelItems { get; set; } = [];

    /// <summary>
    /// 当前数据源下被用户隐藏的点位唯一标识。
    /// </summary>
    public List<string> HiddenMapPointKeys { get; set; } = [];
}

[Serializable]
public sealed class MapMaskSelectedLabelState
{
    /// <summary>
    /// 标签主标识。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 标签合并项包含的全部标签标识。
    /// </summary>
    public List<string> LabelIds { get; set; } = [];

    /// <summary>
    /// 标签所属分类标识。
    /// </summary>
    public string ParentId { get; set; } = string.Empty;

    /// <summary>
    /// 用于离线恢复选中项时展示的标签名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 标签图标地址。
    /// </summary>
    public string IconUrl { get; set; } = string.Empty;

    /// <summary>
    /// 数据源提供的点位数量，用于界面展示。
    /// </summary>
    public int PointCount { get; set; }
}

/// <summary>
/// 地图遮罩状态的本地存储。
/// 每个点位数据源使用一个独立 JSON 文件，避免切换数据源时相互覆盖。
/// </summary>
public static class MapMaskStateStorage
{
    /// <summary>
    /// 所有地图遮罩状态文件的相对目录。
    /// </summary>
    private const string StateDirectoryRelativePath = @"User/MapMask";

    /// <summary>
    /// 保护内存缓存和文件读写，确保同一时刻只有一个状态更新操作。
    /// </summary>
    private static readonly object Locker = new();

    /// <summary>
    /// 已读取的数据源状态缓存，键与对应 JSON 文件名一致。
    /// </summary>
    private static readonly Dictionary<string, MapMaskDataSourceState> StateByDataSource = new(StringComparer.Ordinal);

    /// <summary>
    /// 读取指定数据源的地图遮罩状态。
    /// </summary>
    /// <param name="dataSourceKey">数据源标识，同时决定状态文件名。</param>
    /// <returns>状态副本；调用方修改副本不会直接影响缓存。</returns>
    public static MapMaskDataSourceState Read(string dataSourceKey)
    {
        lock (Locker)
        {
            if (!StateByDataSource.TryGetValue(dataSourceKey, out var state))
            {
                // 首次访问该数据源时才读取磁盘，后续读操作直接使用内存缓存。
                state = ReadFromFile(dataSourceKey);
                StateByDataSource[dataSourceKey] = state;
            }

            // 避免调用方绕过 Update 修改缓存，导致修改未持久化到文件。
            return Clone(state);
        }
    }

    /// <summary>
    /// 原子地更新并保存指定数据源的地图遮罩状态。
    /// </summary>
    /// <param name="dataSourceKey">数据源标识，同时决定状态文件名。</param>
    /// <param name="update">对缓存状态执行的更新操作。</param>
    public static void Update(string dataSourceKey, Action<MapMaskDataSourceState> update)
    {
        ArgumentNullException.ThrowIfNull(update);

        lock (Locker)
        {
            if (!StateByDataSource.TryGetValue(dataSourceKey, out var state))
            {
                // 写入前同样需先加载，防止只更新其中一个字段时覆盖另一字段的已有数据。
                state = ReadFromFile(dataSourceKey);
                StateByDataSource[dataSourceKey] = state;
            }

            update(state);
            // 将反序列化或调用方写入的空值、重复项收敛为可安全持久化的状态。
            Normalize(state);
            WriteToFile(dataSourceKey, state);
        }
    }

    /// <summary>
    /// 从指定数据源的 JSON 文件读取状态；文件不存在或读取失败时返回空状态。
    /// </summary>
    private static MapMaskDataSourceState ReadFromFile(string dataSourceKey)
    {
        try
        {
            var filePath = GetFilePath(dataSourceKey);
            if (!File.Exists(filePath))
            {
                return new MapMaskDataSourceState();
            }

            var json = File.ReadAllText(filePath);
            // 统一使用项目配置的 JSON 选项，兼容现有序列化命名策略。
            return Normalize(JsonSerializer.Deserialize<MapMaskDataSourceState>(json, ConfigService.JsonOptions));
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            Console.WriteLine(e.StackTrace);
            return new MapMaskDataSourceState();
        }
    }

    /// <summary>
    /// 规范化状态，保证后续 UI 恢复和序列化无需处理空集合、空字段或重复隐藏项。
    /// </summary>
    private static MapMaskDataSourceState Normalize(MapMaskDataSourceState? state)
    {
        state ??= new MapMaskDataSourceState();
        state.SelectedLabelItems ??= [];
        state.HiddenMapPointKeys ??= [];
        state.SelectedLabelItems = state.SelectedLabelItems.Where(x => x != null).ToList();
        state.HiddenMapPointKeys = state.HiddenMapPointKeys
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var item in state.SelectedLabelItems)
        {
            item.Id ??= string.Empty;
            item.LabelIds ??= [];
            item.ParentId ??= string.Empty;
            item.Name ??= string.Empty;
            item.IconUrl ??= string.Empty;
        }

        return state;
    }

    /// <summary>
    /// 创建状态深拷贝，隔离存储缓存与调用方持有的数据。
    /// </summary>
    private static MapMaskDataSourceState Clone(MapMaskDataSourceState state)
    {
        return new MapMaskDataSourceState
        {
            SelectedLabelItems = state.SelectedLabelItems.Select(x => new MapMaskSelectedLabelState
            {
                Id = x.Id,
                LabelIds = x.LabelIds.ToList(),
                ParentId = x.ParentId,
                Name = x.Name,
                IconUrl = x.IconUrl,
                PointCount = x.PointCount
            }).ToList(),
            HiddenMapPointKeys = state.HiddenMapPointKeys.ToList()
        };
    }

    /// <summary>
    /// 将指定数据源状态写入其独立 JSON 文件。
    /// </summary>
    private static void WriteToFile(string dataSourceKey, MapMaskDataSourceState state)
    {
        try
        {
            var filePath = GetFilePath(dataSourceKey);
            var directoryPath = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directoryPath) && !Directory.Exists(directoryPath))
            {
                // 首次保存时创建 User/MapMask 目录。
                Directory.CreateDirectory(directoryPath);
            }

            File.WriteAllText(filePath, JsonSerializer.Serialize(state, ConfigService.JsonOptions));
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            Console.WriteLine(e.StackTrace);
        }
    }

    /// <summary>
    /// 根据数据源标识生成状态文件绝对路径，并阻止非法文件名写入目录外。
    /// </summary>
    private static string GetFilePath(string dataSourceKey)
    {
        if (string.IsNullOrWhiteSpace(dataSourceKey) || dataSourceKey.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("数据源标识不能作为文件名。", nameof(dataSourceKey));
        }

        return Global.Absolute(Path.Combine(StateDirectoryRelativePath, $"{dataSourceKey}.json"));
    }
}

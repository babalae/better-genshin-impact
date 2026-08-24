using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;
using BetterGenshinImpact.Core.Recognition.OpenCv.Model;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

/// <summary>
/// 每层的特征
/// 同时承载普通整图层和分组分层片段的通用元数据；具体匹配方式可按需使用对应的资源字段。
/// </summary>
public class BaseMapLayer(SceneBaseMap baseMap)
{
    // 以下三个字段是原先为分层地图预留的元数据，保留原注释并正式启用对应属性。
    // public string Name { get; set; } = string.Empty;
    //
    // public string LayerId { get; set; } = string.Empty;
    //
    // public string LayerGroupId { get; set; } = string.Empty;

    /// <summary>
    /// 地图层的可读名称，主要用于日志和诊断。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 地图层的稳定唯一标识。
    /// </summary>
    public string LayerId { get; set; } = string.Empty;

    /// <summary>
    /// 地图层所属分组的稳定标识。
    /// </summary>
    public string LayerGroupId { get; set; } = string.Empty;

    /// <summary>
    /// 层级
    /// </summary>
    public int Floor { get; set; } = 0;

    /// <summary>
    /// 分组分层片段在整张参考底图中的裁剪范围
    /// </summary>
    public Rect CropRect { get; set; }

    /// <summary>
    /// 当前楼层实际高亮图层的范围
    /// </summary>
    public Rect HighlightRect { get; set; }

    /// <summary>
    /// 当前特征匹配资源中记录的特征点数量。
    /// </summary>
    public int FeatureCount { get; set; }

    /// <summary>
    /// 当前特征匹配实现使用的关键点资源完整路径。
    /// </summary>
    public string KeyPointFilePath { get; set; } = string.Empty;

    /// <summary>
    /// 当前特征匹配实现使用的描述矩阵资源完整路径。
    /// </summary>
    public string DescriptorFilePath { get; set; } = string.Empty;

    /// <summary>
    /// 是否为从分组分层清单创建的地图层。
    /// 此标记只描述层的组织方式，不限定具体匹配算法。
    /// </summary>
    public bool IsGroupLayer { get; set; }

    /// <summary>
    /// 当前层的所有特征
    /// </summary>
    public Mat TrainDescriptors { get; set; } = new();

    /// <summary>
    /// 当前层的所有关键点；分组分层资源加载后会统一转换到参考底图坐标系。
    /// </summary>
    public KeyPoint[] TrainKeyPoints { get; set; } = [];

    /// <summary>
    /// 切割后的特征块
    /// </summary>
    public KeyPointFeatureBlock[][] SplitBlocks { get; set; } = [];

    /// <summary>
    /// 最近一次合并的特征块
    /// </summary>
    private KeyPointFeatureBlock? _lastMergedBlock = null;

    /// <summary>
    /// 保护分组分层特征资源的首次加载，避免多个导航实例重复读取同一份文件。
    /// </summary>
    private readonly object _featureLoadLock = new();

    /// <summary>
    /// 特征资源是否已经成功加载并可供复用。
    /// </summary>
    private volatile bool _isFeatureLoaded;

    /// <summary>
    /// 特征资源是否已经加载失败；失败后本进程内不再反复读取损坏或缺失的文件。
    /// </summary>
    private volatile bool _featureLoadFailed;

    /// <summary>
    /// 获取当前层的特征资源是否已加载。
    /// </summary>
    public bool IsFeatureLoaded => _isFeatureLoaded;

    /// <summary>
    /// 从本地文件加载分层地图信息
    /// 路径 Assets\Map
    /// - Teyvat
    ///     - Teyvat_0_2048_SIFT.kp.bin
    ///     - Teyvat_0_2048_SIFT.mat.png
    ///     - Teyvat_-1_2048_SIFT.kp.bin
    ///     - Teyvat_-1_2048_SIFT.mat.png
    /// </summary>
    /// <param name="baseMap"></param>
    /// <returns></returns>
    public static List<BaseMapLayer> LoadLayers(SceneBaseMap baseMap)
    {
        var layers = new List<BaseMapLayer>();
        var layerDir = Path.Combine(Global.Absolute(@"Assets\Map\"), baseMap.Type.ToString());
        if (!Directory.Exists(layerDir))
        {
            return layers;
        }

        var files = Directory.GetFiles(layerDir);
        var validFiles = files.Where(f => (f.EndsWith(".kp.bin") || f.EndsWith(".mat.png"))
                                          && !f.EndsWith("Teyvat_0_256_SIFT.kp.bin")
                                          && !f.EndsWith("Teyvat_0_256_SIFT.mat.png"));
        // 解析后按 floor 分组，然后按 floor 创建BaseMapLayer
        var groupedFiles = validFiles.GroupBy(file =>
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            var parts = fileName.Split('_');
            if (parts.Length < 3)
            {
                throw new Exception($"分层地图数据文件夹中中存在无法解析的文件名: {fileName}");
            }

            return int.TryParse(parts[1], out var floor) ? floor : throw new Exception($"分层地图数据文件夹中中存在无法解析的文件名: {fileName}");
        });

        foreach (var group in groupedFiles)
        {
            var floor = group.Key;
            var layer = new BaseMapLayer(baseMap)
            {
                Floor = floor,
                Name = floor == 0 ? $"{baseMap.Type} 主地图" : $"{baseMap.Type} floor {floor}",
                LayerId = $"{baseMap.Type}_{floor}",
                LayerGroupId = baseMap.Type.ToString()
            };

            // 查找特征文件路径
            var kpFilePath = group.First(f => f.EndsWith(".kp.bin"));
            var matFilePath = group.First(f => f.EndsWith(".mat.png"));

            SpeedTimer speedTimer = new($"加载 {Path.GetFileNameWithoutExtension(kpFilePath)} 地图特征");
            // 加载特征数据
            layer.TrainKeyPoints = FeatureStorageHelper.LoadKeyPointArray(kpFilePath) ?? throw new Exception($"地图数据加载失败，文件: {kpFilePath}");
            speedTimer.Record("特征点");
            layer.TrainDescriptors.Dispose();
            layer.TrainDescriptors = FeatureStorageHelper.LoadDescriptorMat(matFilePath) ?? throw new Exception($"地图数据加载失败，文件: {matFilePath}");
            layer.FeatureCount = layer.TrainKeyPoints.Length;
            layer._isFeatureLoaded = true;
            speedTimer.Record("特征描述");

            // 切割特征数据
            if (baseMap.SplitRow > 0 || baseMap.SplitCol > 0)
            {
                layer.SplitBlocks = KeyPointFeatureBlockHelper.SplitFeatures(baseMap.MapSize, baseMap.SplitRow, baseMap.SplitCol, layer.TrainKeyPoints, layer.TrainDescriptors);
                speedTimer.Record("切割特征点");
            }

            speedTimer.DebugPrint();

            layers.Add(layer);
        }

        // 从 0, -1, -2 这样的顺序对这个list排序
        layers.Sort((a, b) =>
        {
            if (a.Floor == b.Floor)
            {
                return 0;
            }

            return a.Floor < b.Floor ? 1 : -1;
        });
        return layers;
    }

    /// <summary>
    /// 从 layers_sift.json 加载分组分层地图元数据，不在此处加载具体匹配资源
    /// </summary>
    /// <param name="baseMap">清单所属的场景地图。</param>
    /// <returns>清单中有效的分组分层地图元数据；清单不存在或不可用时返回空集合。</returns>
    public static IReadOnlyList<BaseMapLayer> LoadGroupLayers(SceneBaseMap baseMap)
    {
        // 目录名称属于当前资源格式；加载后的领域对象使用 GroupLayer 命名，不与 SIFT 算法绑定。
        var groupLayerDirectory = Path.Combine(Global.Absolute(@"Assets\Map\"), baseMap.Type.ToString(), "layers_sift");
        var manifestPath = Path.Combine(groupLayerDirectory, "layers_sift.json");
        if (!File.Exists(manifestPath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(manifestPath);
            var manifest = JsonConvert.DeserializeObject<GroupLayerManifest>(json)
                           ?? throw new InvalidDataException($"无法解析分层地图清单: {manifestPath}");
            if (!manifest.MapType.Equals(baseMap.Type.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"分层地图清单 mapType 不匹配，期望 {baseMap.Type}，实际 {manifest.MapType}");
            }
            if (manifest.ReferenceWidth != baseMap.MapSize.Width || manifest.ReferenceHeight != baseMap.MapSize.Height)
            {
                throw new InvalidDataException(
                    $"分层地图清单尺寸不匹配，期望 {baseMap.MapSize.Width}x{baseMap.MapSize.Height}，实际 {manifest.ReferenceWidth}x{manifest.ReferenceHeight}");
            }

            var layers = new List<BaseMapLayer>(manifest.Fragments.Count);
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fragment in manifest.Fragments)
            {
                if (string.IsNullOrWhiteSpace(fragment.Id) ||
                    string.IsNullOrWhiteSpace(fragment.KeyPointFile) ||
                    string.IsNullOrWhiteSpace(fragment.DescriptorFile) ||
                    fragment.CropWidth <= 0 || fragment.CropHeight <= 0 ||
                    fragment.HighlightWidth <= 0 || fragment.HighlightHeight <= 0 ||
                    fragment.FeatureCount <= 0 || !ids.Add(fragment.Id))
                {
                    TaskControl.Logger.LogWarning("[SIFT] 跳过无效或重复的分层地图片段：{Name}（id={LayerId}, floor={Floor}）",
                        fragment.Name, fragment.Id, fragment.Floor);
                    continue;
                }

                var cropRect = new Rect(fragment.CropX, fragment.CropY, fragment.CropWidth, fragment.CropHeight);
                var highlightRect = new Rect(fragment.HighlightX, fragment.HighlightY, fragment.HighlightWidth, fragment.HighlightHeight);
                var mapRect = new Rect(0, 0, baseMap.MapSize.Width, baseMap.MapSize.Height);
                if (cropRect.Intersect(mapRect) != cropRect || highlightRect.Intersect(cropRect) != highlightRect)
                {
                    TaskControl.Logger.LogWarning("[SIFT] 跳过范围无效的分层地图片段：{Name}（id={LayerId}, floor={Floor}）",
                        fragment.Name, fragment.Id, fragment.Floor);
                    continue;
                }

                layers.Add(new BaseMapLayer(baseMap)
                {
                    Name = string.IsNullOrWhiteSpace(fragment.Name) ? fragment.Id : fragment.Name,
                    LayerId = fragment.Id,
                    LayerGroupId = fragment.GroupId,
                    Floor = fragment.Floor,
                    CropRect = cropRect,
                    HighlightRect = highlightRect,
                    FeatureCount = fragment.FeatureCount,
                    KeyPointFilePath = Path.Combine(groupLayerDirectory, fragment.KeyPointFile),
                    DescriptorFilePath = Path.Combine(groupLayerDirectory, fragment.DescriptorFile),
                    IsGroupLayer = true
                });
            }

            TaskControl.Logger.LogInformation("[SIFT] {MapType} 分层地图清单已加载，共 {Count} 个片段", baseMap.Type, layers.Count);
            return layers;
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogWarning(e, "[SIFT] 分层地图清单加载失败：{ManifestPath}", manifestPath);
            return [];
        }
    }

    /// <summary>
    /// 首次进入分组分层片段范围时加载当前特征匹配资源，加载成功后在进程生命周期内持续复用
    /// </summary>
    /// <returns>资源已经可用时返回 <see langword="true"/>；加载失败时返回 <see langword="false"/>。</returns>
    public bool EnsureFeatureLoaded()
    {
        if (!IsGroupLayer || _isFeatureLoaded)
        {
            return true;
        }
        if (_featureLoadFailed)
        {
            return false;
        }

        lock (_featureLoadLock)
        {
            if (_isFeatureLoaded)
            {
                return true;
            }
            if (_featureLoadFailed)
            {
                return false;
            }

            Mat? descriptors = null;
            try
            {
                TaskControl.Logger.LogInformation("[SIFT] 正在动态加载分层地图：{Name}（group={GroupId}, floor={Floor}）",
                    Name, LayerGroupId, Floor);
                var keyPoints = FeatureStorageHelper.LoadKeyPointArray(KeyPointFilePath)
                                ?? throw new FileNotFoundException("分层地图关键点文件不存在", KeyPointFilePath);
                descriptors = FeatureStorageHelper.LoadDescriptorMat(DescriptorFilePath)
                              ?? throw new FileNotFoundException("分层地图特征描述文件不存在", DescriptorFilePath);
                if (descriptors.Empty() || keyPoints.Length != descriptors.Rows || keyPoints.Length != FeatureCount)
                {
                    throw new InvalidDataException(
                        $"分层地图特征数量不一致，清单 {FeatureCount}，关键点 {keyPoints.Length}，描述 {descriptors.Rows}");
                }

                // 文件中的关键点以裁剪图左上角为原点，加载后统一平移到完整参考底图坐标系。
                for (var i = 0; i < keyPoints.Length; i++)
                {
                    var keyPoint = keyPoints[i];
                    keyPoint.Pt = new Point2f(keyPoint.Pt.X + CropRect.X, keyPoint.Pt.Y + CropRect.Y);
                    keyPoints[i] = keyPoint;
                }

                TrainDescriptors.Dispose();
                TrainKeyPoints = keyPoints;
                TrainDescriptors = descriptors;
                descriptors = null;
                _isFeatureLoaded = true;
                TaskControl.Logger.LogInformation("[SIFT] 分层地图加载完成：{Name}，共 {FeatureCount} 个特征点", Name, FeatureCount);
                return true;
            }
            catch (Exception e)
            {
                descriptors?.Dispose();
                _featureLoadFailed = true;
                TaskControl.Logger.LogWarning(e,
                    "[SIFT] 分层地图加载失败：{Name}（floor={Floor}，kp={KeyPointPath}，descriptor={DescriptorPath}）",
                    Name, Floor, KeyPointFilePath, DescriptorFilePath);
                return false;
            }
        }
    }
    
    public static BaseMapLayer LoadLayer(SceneBaseMap baseMap, string kpFilePath, string matFilePath)
    {
        var layer = new BaseMapLayer(baseMap)
        {
            Floor = 0,
            Name = $"{baseMap.Type} 主地图",
            LayerId = $"{baseMap.Type}_0",
            LayerGroupId = baseMap.Type.ToString(),
            TrainKeyPoints = FeatureStorageHelper.LoadKeyPointArray(kpFilePath) ?? throw new Exception($"地图数据加载失败，文件: {kpFilePath}"),
            TrainDescriptors = FeatureStorageHelper.LoadDescriptorMat(matFilePath) ?? throw new Exception($"地图数据加载失败，文件: {matFilePath}")
        };
        layer.FeatureCount = layer.TrainKeyPoints.Length;
        layer._isFeatureLoaded = true;
        return layer;
    }

    /// <summary>
    /// 选择切分后的特征块合并
    /// </summary>
    /// <param name="prevX"></param>
    /// <param name="prevY"></param>
    /// <returns></returns>
    public (KeyPoint[], Mat) ChooseBlocks(float prevX, float prevY)
    {
        if (baseMap.SplitRow <= 0 || baseMap.SplitCol <= 0 || SplitBlocks.Length == 0)
        {
            return (TrainKeyPoints, TrainDescriptors);
        }

        var (cellRow, cellCol) = KeyPointFeatureBlockHelper.GetCellIndex(baseMap.MapSize, baseMap.SplitRow, baseMap.SplitCol, prevX, prevY);
        Debug.WriteLine($"当前坐标({prevX},{prevY})在特征块({cellRow},{cellCol})中");
        if (_lastMergedBlock == null || _lastMergedBlock.MergedCenterCellRow != cellRow || _lastMergedBlock.MergedCenterCellCol != cellCol)
        {
            Debug.WriteLine($"---------切换到新的特征块({cellRow},{cellCol})，合并特征点--------");
            _lastMergedBlock = KeyPointFeatureBlockHelper.MergeNeighboringFeatures(SplitBlocks, TrainDescriptors, cellRow, cellCol);
        }

        return (_lastMergedBlock.KeyPointArray, _lastMergedBlock.Descriptor!);
    }
}

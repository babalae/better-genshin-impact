using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;

namespace BetterGenshinImpact.Core.Recognition.Ncnn;

/// <summary>单个 ncnn 模型产物的就绪状态。</summary>
public sealed record NcnnAssetItem(string DisplayName, string Usage, bool Ready, string Detail);

/// <summary>按用途分组的 ncnn 产物就绪状态。</summary>
public sealed record NcnnAssetGroup(string Name, int ReadyCount, int TotalCount, IReadOnlyList<NcnnAssetItem> Items)
{
    public bool IsReady => ReadyCount == TotalCount;
    public string Summary => $"{Name} {ReadyCount}/{TotalCount}";
}

/// <summary>
///     ncnn 后端的可用性探测：只做文件系统检查，不加载 <c>ncnn_shim.dll</c>
///     （该库静态链接 Vulkan 且有意不卸载，探测不应触发加载与管线初始化）。
/// </summary>
public sealed class NcnnAvailability
{
    private const string OcrModelRelativeDir = @"Assets\Model\PaddleOCR\Ncnn";

    private static readonly (BgiOnnxModel Model, string Display, string Usage)[] YoloModels =
    [
        (BgiOnnxModel.BgiWorld, "世界物体检测", "拾取 · 战斗结束检测"),
        (BgiOnnxModel.BgiFish, "鱼类检测", "自动钓鱼"),
        (BgiOnnxModel.BgiMine, "矿石检测", "挖矿"),
        (BgiOnnxModel.BgiTree, "古树检测", "自动秘境"),
        (BgiOnnxModel.BgiQClassify, "Q 技能分类", "自动战斗"),
        (BgiOnnxModel.BgiAvatarSide, "队伍头像分类", "自动战斗"),
        (BgiOnnxModel.BgiEClassify, "E 技能分类", "自动战斗")
    ];

    /// <summary>OCR 模型目录（绝对路径），诊断探测与装配路径共用。</summary>
    public static string OcrModelDirectory => Global.Absolute(OcrModelRelativeDir);

    private NcnnAvailability(bool shimReady, string shimPath, IReadOnlyList<NcnnAssetGroup> groups)
    {
        ShimReady = shimReady;
        ShimPath = shimPath;
        Groups = groups;
    }

    public bool ShimReady { get; }
    public string ShimPath { get; }
    public IReadOnlyList<NcnnAssetGroup> Groups { get; }
    public int ReadyCount => Groups.Sum(group => group.ReadyCount);
    public int TotalCount => Groups.Sum(group => group.TotalCount);
    public bool IsReady => ShimReady && ReadyCount == TotalCount;

    public string Summary
    {
        get
        {
            if (!ShimReady) return "ncnn 运行库缺失 · 必须补齐 ncnn_shim.dll（需含 create_ex / infer_ex 的新版本）";
            var ocrReady = Groups.Where(group => group.Name.StartsWith("OCR", StringComparison.Ordinal)).Sum(group => group.ReadyCount);
            var ocrTotal = Groups.Where(group => group.Name.StartsWith("OCR", StringComparison.Ordinal)).Sum(group => group.TotalCount);
            var yoloReady = ReadyCount - ocrReady;
            var yoloTotal = TotalCount - ocrTotal;
            return IsReady
                ? $"ncnn 已就绪 · OCR {ocrReady}/{ocrTotal} · YOLO {yoloReady}/{yoloTotal}"
                : $"ncnn 部分产物缺失 · OCR {ocrReady}/{ocrTotal} · YOLO {yoloReady}/{yoloTotal}";
        }
    }

    public static NcnnAvailability Detect()
    {
        // shim 走 NuGet 原生包标准解析（裸名加载），可用性以导出探测为准
        var shimReady = NcnnShim.IsSupported();
        var shimText = "NuGet 包 BetterGI.NcnnShim（runtimes/win-x64/native/ncnn_shim.dll）";
        var ocrDir = Global.Absolute(OcrModelRelativeDir);

        return new NcnnAvailability(shimReady, shimText,
        [
            BuildGroup("OCR 文本检测", [BuildItem(
                ocrDir, "det_dyn", "文本检测 · 动态形状", "全部 OCR 文本定位")]),
            BuildGroup("OCR 文字识别", [BuildItem(
                ocrDir, "rec_dyn", "文字识别 · 动态形状", "OCR 结果解码")]),
            BuildGroup("YOLO 检测与分类", YoloModels.Select(def => BuildItem(
                Path.Combine(Path.GetDirectoryName(def.Model.ModalPath) ?? ".", "Ncnn"),
                Path.GetFileNameWithoutExtension(def.Model.ModalPath), def.Display, def.Usage)))
        ]);
    }

    private static NcnnAssetGroup BuildGroup(string name, IEnumerable<NcnnAssetItem> items)
    {
        var list = items.ToArray();
        return new NcnnAssetGroup(name, list.Count(item => item.Ready), list.Length, list);
    }

    private static NcnnAssetItem BuildItem(string directory, string stem, string display, string usage)
    {
        var paramPath = Path.Combine(directory, stem + ".param");
        var modelPath = Path.Combine(directory, stem + ".bin");
        var hasParam = File.Exists(paramPath);
        var hasModel = File.Exists(modelPath);
        var ready = hasParam && hasModel;
        return new NcnnAssetItem(display, usage, ready, ready
            ? "ncnn 已就绪"
            : !hasParam && !hasModel ? "产物缺失" : "产物不完整");
    }
}

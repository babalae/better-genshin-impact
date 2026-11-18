using System;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;

namespace BetterGenshinImpact.Core.Recognition.Ncnn;

/// <summary>
///     ncnn 模型（.param/.bin）的统一路径解析。
///     模型由「ncnn 模型资源插件」（id = ncnn_models）分发，安装后位于
///     <c>Plugins/ncnn_models/Models/&lt;类别&gt;/Ncnn/</c>；插件未安装时回退到旧的
///     <c>Assets/Model/&lt;类别&gt;/Ncnn/</c>（兼容开发期/旧布局）。
/// </summary>
/// <remarks>
///     插件启用与否不影响模型读取——本类只看文件是否存在，与插件系统的可插拔机制无关。
///     所有 ncnn 后端与诊断代码都从这里取目录，避免路径常量散落。
/// </remarks>
public static class NcnnModelPaths
{
    /// <summary>ncnn 模型资源插件的标识（同时是安装目录名）。</summary>
    public const string PluginId = "ncnn_models";

    /// <summary>插件内模型根目录（相对启动目录）。</summary>
    private static readonly string PluginModelsRoot = Path.Combine("Plugins", PluginId, "Models");

    /// <summary>OCR 模型在插件内的类别目录名。</summary>
    private const string OcrCategory = "PaddleOCR";

    /// <summary>
    ///     OCR 模型目录（det_dyn / rec_dyn）。优先插件目录，回退旧资产目录。
    /// </summary>
    public static string OcrModelDirectory => Resolve(OcrCategory, @"Assets\Model\PaddleOCR\Ncnn");

    /// <summary>
    ///     按 ONNX 模型取对应的 ncnn 目录。从 ONNX 相对路径
    ///     <c>Assets\Model\&lt;类别&gt;\&lt;stem&gt;.onnx</c> 提取类别，优先插件目录，回退同目录 Ncnn 子目录。
    /// </summary>
    public static string YoloModelDirectory(BgiOnnxModel model)
    {
        var category = ExtractCategory(model.ModelRelativePath);
        var legacy = Path.Combine(Path.GetDirectoryName(model.ModalPath) ?? ".", "Ncnn");
        return category is null ? legacy : Resolve(category, legacy);
    }

    /// <summary>
    ///     解析某类别的 ncnn 目录：插件目录含 .param/.bin 时用插件，否则回退旧目录。
    ///     用「目录存在且有任意 .param」判断插件是否已安装该类别，避免空目录误判。
    /// </summary>
    private static string Resolve(string category, string legacyRelative)
    {
        var pluginDir = Global.Absolute(Path.Combine(PluginModelsRoot, category, "Ncnn"));
        if (HasParamFiles(pluginDir)) return pluginDir;
        return Global.Absolute(legacyRelative);
    }

    private static bool HasParamFiles(string directory)
    {
        if (!Directory.Exists(directory)) return false;
        try
        {
            return Directory.EnumerateFiles(directory, "*.param", SearchOption.TopDirectoryOnly).Any();
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    ///     从相对路径 <c>Assets\Model\&lt;类别&gt;\...</c> 提取类别段（Model 后的第一段）。
    ///     兼容 / 与 \ 分隔符；不符合约定时返回 null（调用方回退旧逻辑）。
    /// </summary>
    private static string? ExtractCategory(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var segments = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (string.Equals(segments[i], "Model", StringComparison.OrdinalIgnoreCase))
                return segments[i + 1];
        }

        return null;
    }
}

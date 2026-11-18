using System;
using System.IO;
using BetterGenshinImpact.Core.Plugins.Hosting;
using BetterGenshinImpact.Core.Recognition.Ncnn;
using BetterGenshinImpact.Core.Recognition.ONNX;
using Xunit;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests;

/// <summary>
///     ncnn 模型资源插件（id=ncnn_models）的验证：
///     1) 打出的 zip 能被主程序插件宿主识别为兼容插件（空壳 entry dll 方案）；
///     2) NcnnModelPaths 的类别提取与插件优先/回退路径解析正确。
///     插件包不在仓库内时相关用例跳过，不阻塞常规测试。
/// </summary>
[Collection("Sequential")]
public class NcnnModelPluginTests
{
    /// <summary>插件包在工作区的固定产出位置；CI/其他机器没有时跳过。</summary>
    /// <remarks>从测试 bin 目录（…/better-genshin-impact/Test/…/bin/x64/Debug/net10.0…）上溯 7 级到工作区根。</remarks>
    private static string PluginZip =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "..",
            "bettergi-plugins", "artifacts", "ncnn_models-1.0.0.zip"));

    [Fact]
    public void NcnnModelZip_IsRecognizedAsCompatiblePlugin()
    {
        var zip = PluginZip;
        if (!File.Exists(zip)) return; // 插件包未构建，跳过

        var package = PluginPackageReader.Read(zip);
        Assert.Null(package.CompatibilityError);
        Assert.Equal("ncnn_models", package.Manifest.Id);
        Assert.Equal("BetterGI.Plugin.NcnnModels.dll", package.Manifest.Entry);
        Assert.True(package.FileCount >= 19, $"插件文件数不足：{package.FileCount}");
        Assert.True(package.TotalBytes > 40L * 1024 * 1024, "插件模型文件体积异常偏小");
    }

    [Fact]
    public void YoloModelDirectory_MapsCategoryFromOnnxRelativePath()
    {
        // 未安装插件时回退到 ONNX 同目录的 Ncnn 子目录
        var world = NcnnModelPaths.YoloModelDirectory(BgiOnnxModel.BgiWorld);
        var expectedFallback = Path.Combine(Path.GetDirectoryName(BgiOnnxModel.BgiWorld.ModalPath) ?? ".", "Ncnn");
        Assert.Equal(Path.GetFullPath(expectedFallback), Path.GetFullPath(world));

        var fish = NcnnModelPaths.YoloModelDirectory(BgiOnnxModel.BgiFish);
        var expectedFish = Path.Combine(Path.GetDirectoryName(BgiOnnxModel.BgiFish.ModalPath) ?? ".", "Ncnn");
        Assert.Equal(Path.GetFullPath(expectedFish), Path.GetFullPath(fish));
    }

    [Fact]
    public void OcrModelDirectory_IsAbsolutePath()
    {
        var dir = NcnnModelPaths.OcrModelDirectory;
        Assert.True(Path.IsPathRooted(dir), $"OCR 模型目录不是绝对路径：{dir}");
    }
}

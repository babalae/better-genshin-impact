using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using Compunet.YoloSharp;
using Compunet.YoloSharp.Data;
using OpenCvSharp;
using Xunit.Abstractions;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFightTests;

/// <summary>
/// e_classify_sim 模型类别名回归测试。
/// 目的：跑通模型推理链路，断言模型产出的类别名各段（前缀/角色英文名/编号/状态）与实测样本一致，
/// 防止 <see cref="Avatar.IsESkillReadyByClassify"/> 依赖的类别名格式悄然变化。
/// </summary>
public class EClassifyTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

#pragma warning disable CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
    private static BgiYoloPredictor predictor;
#pragma warning restore CS8618

    private static BgiYoloPredictor Predictor => LazyInitializer.EnsureInitialized(ref predictor,
        () => new BgiOnnxFactory(new FakeLogger<BgiOnnxFactory>())
            .CreateYoloPredictor(BgiOnnxModel.BgiEClassify));

    /// <summary>
    /// 验证 e_classify_sim 模型输出的类别名各段与期望值一致（格式："前缀 角色英文名 编号 状态"）。
    /// </summary>
    /// <param name="screenshot1080P">1920x1080 战斗截图相对 Assets 目录的路径，例如 <c>AutoFight\E\ready.png</c></param>
    /// <param name="expectedPrefix">期望类别名第 1 段：前缀（实测 "S"）</param>
    /// <param name="expectedNameEn">期望类别名第 2 段：角色英文名（实测 "Arlecchino"）</param>
    /// <param name="expectedCode">期望类别名第 3 段：编号（实测 "01"）</param>
    /// <param name="expectedStatus">期望类别名第 4 段：状态（实测 "nocd" / "cd"）</param>
    [Theory]
    [InlineData(@"AutoFight\联机满编\别人进我世界_2人.png", "S", "Arlecchino", "01", "nocd")]
    [InlineData(@"AutoFight\联机满编\别人进我世界_3人.png", "S", "Arlecchino", "01", "nocd")]
    [InlineData(@"AutoFight\联机满编\别人进我世界_4人.png", "S", "Arlecchino", "01", "nocd")]
    [InlineData(@"AutoFight\联机满编\别人进我世界_4人_2.png", "S", "Arlecchino", "01", "cd")]
    public void ClassifyResult_ShouldMatchExpectedSegments(string screenshot1080P, string expectedPrefix, string expectedNameEn, string expectedCode, string expectedStatus)
    {
        var path = @$"..\..\..\Assets\{screenshot1080P}";
        if (!File.Exists(path))
        {
            _output.WriteLine($"截图不存在，跳过：{path}");
            return;
        }

        var mat = new Mat(path);
        Assert.True(mat.Width == 1920 && mat.Height == 1080,
            $"截图必须是 1920x1080，实际：{mat.Width}x{mat.Height}");

        var systemInfo = new FakeSystemInfo(new Vanara.PInvoke.RECT(0, 0, mat.Width, mat.Height), 1);
        // 桌面 -> 游戏捕获区域 -> 1080P 区域
        var gameCaptureRegion = systemInfo.DesktopRectArea.Derive(mat, systemInfo.CaptureAreaRect.X, systemInfo.CaptureAreaRect.Y);
        var imageRegion = gameCaptureRegion.DeriveTo1080P();

        using var eRa = imageRegion.DeriveCrop(GameTask.AutoFight.Assets.AutoFightAssets.Get(imageRegion).ERectForClassify);
        var result = Predictor.Predictor.Classify(eRa.CacheImage);
        var top = result.GetTopClass();

        _output.WriteLine($"截图: {screenshot1080P}");
        _output.WriteLine($"top class: {top.Name.Name}, confidence: {top.Confidence:F4}");
        // result 的 ToString 已包含所有类别分布（参考 CombatScenes.ClassifyAvatarName 的用法）
        _output.WriteLine($"full result: {result}");

        Assert.True(top.Confidence > 0f, "top class 置信度应当 > 0");

        // 类别名格式: "<前缀> <角色英文名> <编号> <状态>"，实测样本 "S Arlecchino 01 nocd" / "S Arlecchino 01 cd"
        var parts = top.Name.Name.Split(' ');
        Assert.True(parts.Length == 4, $"类别名应为 4 段，实际：{top.Name.Name}");
        Assert.Equal(expectedPrefix, parts[0]);
        Assert.Equal(expectedNameEn, parts[1], ignoreCase: true);
        Assert.Equal(expectedCode, parts[2]);
        Assert.Equal(expectedStatus, parts[3], ignoreCase: true);
    }
}

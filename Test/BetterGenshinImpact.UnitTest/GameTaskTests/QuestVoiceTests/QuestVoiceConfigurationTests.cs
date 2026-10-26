using System;
using System.IO;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.QuestVoice;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>真正运行配置校验，外置资源只写隔离测试目录。</summary>
public sealed class QuestVoiceConfigurationTests : IDisposable
{
    /// <summary>本例资源目录。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "QuestVoiceMigration", Guid.NewGuid().ToString("N"));

    /// <summary>移植的生产默认值必须与来源测试使用的参数一致。</summary>
    [Fact]
    public void ContinuousDefaultsPreserveValidatedSourceTimingAndInkThreshold()
        => Assert.Equal(ContinuousFrameGateTests.Settings, QuestVoiceSessionFactory.ContinuousDefaults);

    /// <summary>缺失模型和声线不能被报告为可运行。</summary>
    [Fact]
    public async Task MissingAssetsAreActionableAndNeverReportedReady()
    {
        var factory = new QuestVoiceSessionFactory(new QuestVoiceConfig(), _root);
        var result = await factory.ValidateAsync(default);
        Assert.False(result.IsValid);
        Assert.Contains("OCR", result.Detail);
        Assert.Contains("GPT", result.Detail);
    }

    /// <summary>外置配置不得被重复检查覆盖。</summary>
    [Fact]
    public async Task SeedPreservesUserRoutingAndSnapshotUsesProgramRelativePaths()
    {
        Directory.CreateDirectory(_root);
        foreach (var name in new[] { "ocr.onnx", "voice.ckpt", "voice.pth", "ref.wav" })
            await File.WriteAllBytesAsync(Path.Combine(_root, name), [1]);
        var config = new QuestVoiceConfig { OcrModelPath = "ocr.onnx", GptWeightsPath = "voice.ckpt",
            SovitsWeightsPath = "voice.pth", ReferenceAudioPath = "ref.wav", PromptText = "参考原文" };
        var factory = new QuestVoiceSessionFactory(config, _root);
        var first = await factory.ValidateAsync(default);
        Assert.True(first.IsValid, first.Detail);
        Assert.Equal(Path.Combine(_root, "ocr.onnx"), first.Settings!.Ocr!.ModelPath);
        var routing = Path.Combine(_root, "User", "QuestVoice", "routing.json");
        await File.WriteAllTextAsync(routing, "user-owned");
        var second = await factory.ValidateAsync(default);
        Assert.True(second.IsValid, second.Detail);
        Assert.Equal("user-owned", await File.ReadAllTextAsync(routing));
    }

    /// <summary>错误地址和采样时间在启动设备前被拦截。</summary>
    [Theory]
    [InlineData("file:///tmp", 150)]
    [InlineData("http://localhost:9880", 0)]
    public async Task RejectsUnsupportedEndpointOrSampling(string endpoint, int interval)
    {
        var config = new QuestVoiceConfig { ServiceUrl = endpoint, SampleIntervalMilliseconds = interval };
        var result = await new QuestVoiceSessionFactory(config, _root).ValidateAsync(default);
        Assert.False(result.IsValid);
    }

    /// <summary>仅删除自己建立的独立测试目录。</summary>
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}

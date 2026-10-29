using System;
using System.IO;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.QuestVoice;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>覆盖旧配置升级、独立引擎配置和服务器路径快照。</summary>
public sealed class TtsProviderConfigurationTests : IDisposable
{
    /// <summary>每例只写自己的隔离目录。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MultiTts", Guid.NewGuid().ToString("N"));

    /// <summary>升级不得改变旧用户选中的引擎或本地资产。</summary>
    [Fact]
    public void LegacyDefaultsToGptWithoutChangingExistingVoice()
    {
        var config = JsonConvert.DeserializeObject<QuestVoiceConfig>("{\"GptWeightsPath\":\"old.ckpt\"}")!;
        var json = JObject.FromObject(config);
        Assert.Equal("gpt-sovits", (string?)json["ProviderId"]);
        Assert.Equal("old.ckpt", config.GptWeightsPath);
        Assert.False(json["UseRemoteService"]!.Value<bool>());
    }

    /// <summary>第二引擎不读取 GPT 资产，运行中不受配置编辑影响。</summary>
    [Fact]
    public async Task PresetProviderNeedsNoGptAssetsAndFreezesSnapshot()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(Path.Combine(_root, "ocr.onnx"), [1]);
        var config = JsonConvert.DeserializeObject<QuestVoiceConfig>(
            "{\"ProviderId\":\"huoshan\",\"HuoshanVoiceId\":\"zh_female_qingxin\",\"RoutingEnabled\":true,\"StreamingEnabled\":true,\"ServiceUrl\":\"invalid\"}")!;
        config.OcrModelPath = "ocr.onnx";
        var result = await new QuestVoiceSessionFactory(config, _root).ValidateAsync(default);
        Assert.True(result.IsValid, result.Detail);
        var snapshot = JObject.FromObject(result.Settings!);
        Assert.Equal("huoshan", (string?)snapshot["Provider"]?["ProviderId"]);
        Assert.Null(result.Settings!.Routing);
        Assert.False(result.Settings.Protocol!.StreamPlayback);
        JsonConvert.PopulateObject("{\"ProviderId\":\"gpt-sovits\"}", config);
        Assert.Equal("huoshan", (string?)JObject.FromObject(result.Settings)["Provider"]?["ProviderId"]);
    }

    /// <summary>远程资源保持服务器原文，不能要求客户端存在这些文件。</summary>
    [Fact]
    public async Task RemoteSnapshotPreservesLinuxPathsAndLocalConfiguration()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(Path.Combine(_root, "ocr.onnx"), [1]);
        var config = JsonConvert.DeserializeObject<QuestVoiceConfig>(
            "{\"UseRemoteService\":true,\"RemoteServiceUrl\":\"https://tts.example.com/api/\",\"RemoteGptWeightsPath\":\"/models/ganyu.ckpt\",\"RemoteSovitsWeightsPath\":\"/models/ganyu.pth\",\"RemoteReferenceAudioPath\":\"/audio/ref.wav\",\"RemotePromptText\":\"你好\",\"GptWeightsPath\":\"local.ckpt\"}")!;
        config.OcrModelPath = "ocr.onnx";
        var result = await new QuestVoiceSessionFactory(config, _root).ValidateAsync(default);
        Assert.True(result.IsValid, result.Detail);
        Assert.Equal("/models/ganyu.ckpt", result.Settings!.Voice.GptWeightsPath);
        Assert.Equal("https://tts.example.com/api/", result.Settings.TtsServiceUrl.AbsoluteUri);
        Assert.Equal("local.ckpt", config.GptWeightsPath);
    }

    /// <summary>清理本例拥有的目录。</summary>
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}

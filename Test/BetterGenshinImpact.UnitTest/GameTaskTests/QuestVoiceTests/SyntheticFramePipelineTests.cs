using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Audio;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Ocr;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>真实场景判定→预处理→接受→路由→HTTP 协议→WAV→播放边界的贯通验证。</summary>
public sealed class SyntheticFramePipelineTests
{
    /// <summary>只替换外部 OCR 推理、HTTP 服务器和物理声卡，其他业务路径全部运行生产实现。</summary>
    [Fact]
    public async Task StableImageFlowsThroughRoutingRealTransportAndValidatedAudioExactlyOnce()
    {
        using var fixture = new RoutingFixture();
        await fixture.WriteAsync();
        var settings = new AppSettings(fixture.Root, new("http://localhost:19001/"), new(1, 2, 2), fixture.LegacyVoice,
            new(fixture.Root, Path.Combine(fixture.Root, "audio")), new(0, 0),
            Ocr: new("controlled.onnx", "unused.xml"), Scene: new(SceneSelectionTests.CatalogPath), Routing: new(fixture.CatalogPath));
        var requests = new List<string>();
        using var engine = new GptSovitsEngine(_ => new HttpClient(new RoutingTransportTests.Handler((request, _) =>
        {
            requests.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(request.Method == HttpMethod.Get ? RoutingTransportTests.Success() : RoutingTransportTests.Audio());
        })), new ReferenceAudioSelector(), new WavProcessor());
        var player = new ControlledAudio();
        var voice = new ContinuousVoiceSession(settings, engine, new WavProcessor(), new AudioArtifactStore(), player,
            new VoiceRoutingService(new SpeakerCatalogLoader(), new SpeakerRouter()));
        var catalog = new SceneCatalog();
        var selector = new GenshinSceneSelector(catalog);
        var ocr = new ControlledOcr();
        var images = new ImageOcrService(new SceneProfileLoader(), new ImagePreprocessor(), ocr, selector);
        await using var frames = new ContinuousFrameSession(QuestVoiceSessionFactory.ContinuousDefaults,
            new ContinuousFrameInspector(QuestVoiceSessionFactory.ContinuousDefaults, settings.Scene!, catalog, selector),
            new ContinuousFrameRecognizer(images, settings), voice);
        var routed = new TaskCompletionSource<ContinuousVoiceEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        voice.Event += value => { if (value.Kind == ContinuousVoiceEventKind.Routed) routed.TrySetResult(value); };
        await frames.StartAsync();
        var bytes = SceneSelectionTests.Draw();
        await frames.ObserveAsync(1, TimeSpan.Zero, bytes);
        await frames.ObserveAsync(2, TimeSpan.FromMilliseconds(200), bytes);
        await frames.ObserveAsync(3, TimeSpan.FromMilliseconds(400), bytes);
        var audio = await player.Played.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await frames.CompleteAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("voice.ganyu", (await routed.Task).Work!.Routing!.VoiceId);
        Assert.Equal(new[] { "/set_gpt_weights", "/set_sovits_weights", "/tts" }, requests);
        Assert.Equal(1, ocr.Calls);
        Assert.True(new WavProcessor().ValidateCompletedResponse(audio).DurationSeconds > 0);
        Assert.Single(Directory.GetFiles(settings.Output.AudioDirectory, "*.wav"));
    }

    /// <summary>明确标记的外部推理替身，检查生产预处理确实提供了两块张量。</summary>
    private sealed class ControlledOcr : IOcrEngine
    {
        /// <summary>实际推理调用次数。</summary>
        public int Calls { get; private set; }
        /// <summary>返回可接受的中文角色与正文，不宣称运行过真实模型。</summary>
        public Task<(IReadOnlyList<OcrText> Texts, OcrModelInfo Model)> RecognizeAsync(IReadOnlyList<OcrTensor> tensors,
            OcrSettings settings, string directory, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Calls++;
            Assert.Equal(2, tensors.Count);
            Assert.All(tensors, tensor => Assert.True(tensor.Data.Length > 0));
            return Task.FromResult<(IReadOnlyList<OcrText>, OcrModelInfo)>((new[] { new OcrText("甘雨", .99),
                new OcrText("你好，旅行者。", .99) }, new("controlled", "controlled", "TestDouble", 0, null)));
        }
    }
    /// <summary>接收真实校验后的 WAV，避免打开物理设备。</summary>
    private sealed class ControlledAudio : IAudioPlayer
    {
        /// <summary>实际到达播放边界的音频。</summary>
        public TaskCompletionSource<byte[]> Played { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>验证停止令牌后报告到达播放边界。</summary>
        public Task PlayAsync(byte[] wav, CancellationToken token) { token.ThrowIfCancellationRequested(); Played.TrySetResult(wav); return Task.CompletedTask; }
        /// <summary>本替身立即完成播放，不拥有设备。</summary>
        public void Stop() { }
    }
}

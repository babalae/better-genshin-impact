using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Ocr;
using SkiaSharp;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>移植回归验证：AutoSceneProcessingTests。</summary>
public sealed class AutoSceneProcessingTests : IDisposable
{
    /// <summary>移植回归验证： directory。</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "QuestVoiceAutoProcessing", Guid.NewGuid().ToString("N"));
    /// <summary>移植回归验证：ImagePath。</summary>
    private string ImagePath => Path.Combine(_directory, "image.png");
    /// <summary>移植回归验证：Profile。</summary>
    internal static readonly SceneProfile Profile = new(20, 10,
        [new("name", OcrRegionKind.Speaker, 0, new(0, 0, 10, 5)), new("body", OcrRegionKind.Dialogue, 0, new(0, 5, 20, 10))]);
    /// <summary>移植回归验证：AutoSceneProcessingTests。</summary>
    public AutoSceneProcessingTests() { Directory.CreateDirectory(_directory); WriteImage(SKColors.Red); }
    /// <summary>移植回归验证：Request。</summary>
    private ImageProcessingRequest Request => new(ImagePath, SceneSelectionMode.Automatic, null,
        new("missing.onnx", "missing.xml"), new("catalog.json"), _directory);

    /// <summary>移植回归验证：Selected uses selected profile once and not xml。</summary>
    [Fact]
    public async Task Selected_uses_selected_profile_once_and_not_xml()
    {
        var engine = new Engine();
        var layouts = new Layouts();
        var result = await new ImageOcrService(layouts, new(), engine, new Selector()).ProcessAsync(Request, null, default);
        Assert.Equal(ImageProcessingOutcome.Recognized, result.Outcome);
        Assert.Equal(Profile, result.Ocr!.Layout);
        Assert.Equal(1, engine.Calls);
        Assert.Equal(0, layouts.Calls);
        Assert.Equal("测试正文", result.Ocr.Dialogue);
    }

    /// <summary>移植回归验证：Refusal never calls engine or xml even without ocr configuration。</summary>
    [Theory]
    [InlineData(SceneDecisionKind.Rejected, ImageProcessingOutcome.Rejected)]
    [InlineData(SceneDecisionKind.Unknown, ImageProcessingOutcome.Unknown)]
    public async Task Refusal_never_calls_engine_or_xml_even_without_ocr_configuration(SceneDecisionKind kind, ImageProcessingOutcome outcome)
    {
        var engine = new Engine(); var layouts = new Layouts();
        var result = await new ImageOcrService(layouts, new(), engine, new Selector(kind)).ProcessAsync(Request with { Ocr = null }, null, default);
        Assert.Equal(outcome, result.Outcome);
        Assert.Null(result.Ocr);
        Assert.NotNull(result.Source);
        Assert.Equal(0, engine.Calls);
        Assert.Equal(0, layouts.Calls);
    }

    /// <summary>移植回归验证：Non speakable text retains raw ocr。</summary>
    [Theory]
    [InlineData("……？！")]
    [InlineData(" \t\n")]
    public async Task Non_speakable_text_retains_raw_ocr(string text)
    {
        var engine = new Engine { Body = text };
        var result = await new ImageOcrService(new Layouts(), new(), engine, new Selector()).ProcessAsync(Request, null, default);
        Assert.Equal(ImageProcessingOutcome.NoSpeakableText, result.Outcome);
        Assert.Equal(text, result.Ocr!.Regions[1].RawText);
        Assert.Equal(1, engine.Calls);
    }

    /// <summary>移植回归验证：Scene notification can overwrite source but hash dimensions and tensors use original bytes。</summary>
    [Fact]
    public async Task Scene_notification_can_overwrite_source_but_hash_dimensions_and_tensors_use_original_bytes()
    {
        var original = await File.ReadAllBytesAsync(ImagePath);
        var engine = new Engine();
        var selector = new Selector();
        ImageSceneResult? notification = null;
        var result = await new ImageOcrService(new Layouts(), new(), engine, selector).ProcessAsync(Request, scene =>
        {
            notification = scene;
            Assert.Equal(0, engine.Calls);
            WriteImage(SKColors.Blue, 8, 8);
        }, default);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant(), result.Source!.Sha256);
        Assert.Equal(result.Source, notification!.Source);
        Assert.Equal(original, selector.Bytes);
        Assert.Equal(20, result.Source.Width);
        Assert.Equal(10, result.Source.Height);
        Assert.Equal(20, result.Ocr!.ImageWidth);
        Assert.Equal(-1, engine.Tensors![0].Data[0]);
        Assert.Equal(1, engine.Tensors[0].Data[96 * engine.Tensors[0].Width]);
    }

    /// <summary>移植回归验证：Stop from scene callback prevents ocr and keeps snapshot。</summary>
    [Fact]
    public async Task Stop_from_scene_callback_prevents_ocr_and_keeps_snapshot()
    {
        using var cts = new CancellationTokenSource();
        var engine = new Engine();
        var result = await new ImageOcrService(new Layouts(), new(), engine, new Selector()).ProcessAsync(Request, _ => cts.Cancel(), cts.Token);
        Assert.Equal(ImageProcessingOutcome.Cancelled, result.Outcome);
        Assert.NotNull(result.Source);
        Assert.Null(result.Ocr);
        Assert.Equal(0, engine.Calls);
    }

    /// <summary>移植回归验证：Explicit mode preserves punctuation and skips selector。</summary>
    [Fact]
    public async Task Explicit_mode_preserves_punctuation_and_skips_selector()
    {
        var engine = new Engine { Body = "……" }; var layouts = new Layouts(); var selector = new Selector();
        var service = new ImageOcrService(layouts, new(), engine, selector);
        var result = await service.ProcessAsync(Request with { Mode = SceneSelectionMode.Explicit }, null, default);
        Assert.Equal(ImageProcessingOutcome.Recognized, result.Outcome);
        Assert.Equal(SceneDecisionKind.Explicit, result.Scene!.Kind);
        Assert.Equal(0, selector.Calls);
        Assert.Equal("missing.xml", layouts.LastPath);
        var legacy = await service.RecognizeAsync(new(ImagePath, "chosen.xml", Request.Ocr!, _directory), default);
        Assert.Equal(result.Ocr!.Dialogue, legacy.Dialogue);
        Assert.Equal("chosen.xml", layouts.LastPath);
    }

    /// <summary>移植回归验证：Explicit template error still precedes missing image。</summary>
    [Fact]
    public async Task Explicit_template_error_still_precedes_missing_image()
    {
        var service = new ImageOcrService(new SceneProfileLoader(), new(), new Engine(), new Selector());
        var result = await service.ProcessAsync(Request with
        {
            Mode = SceneSelectionMode.Explicit, ImagePath = Path.Combine(_directory, "missing.png"),
            TemplatePath = Path.Combine(_directory, "missing.xml")
        }, null, default);
        Assert.Equal(ImageProcessingOutcome.Failed, result.Outcome);
        Assert.Equal(OcrErrorCode.Template, result.Error);
    }

    /// <summary>移植回归验证：Selected missing model preserves scene and snapshot without model fiction。</summary>
    [Fact]
    public async Task Selected_missing_model_preserves_scene_and_snapshot_without_model_fiction()
    {
        using var engine = new DirectMlOcrEngine();
        var result = await new ImageOcrService(new Layouts(), new(), engine, new Selector()).ProcessAsync(Request, null, default);
        Assert.Equal(ImageProcessingOutcome.Failed, result.Outcome);
        Assert.Equal(OcrErrorCode.ModelMissing, result.Error);
        Assert.NotNull(result.Source);
        Assert.Equal(SceneDecisionKind.Selected, result.Scene!.Kind);
        Assert.Null(result.Ocr);
        Assert.Single(Directory.GetFiles(_directory));
    }

    /// <summary>移植回归验证：WriteImage。</summary>
    private void WriteImage(SKColor color, int width = 20, int height = 10)
    {
        using var bitmap = new SKBitmap(width, height); bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(ImagePath, encoded.ToArray());
    }
    /// <summary>移植回归验证：Selector。</summary>
    internal sealed class Selector(SceneDecisionKind kind = SceneDecisionKind.Selected) : ISceneSelector
    {
        /// <summary>移植回归验证：Calls。</summary>
        public int Calls { get; private set; }
        /// <summary>移植回归验证：Bytes。</summary>
        public byte[]? Bytes { get; private set; }
        /// <summary>在后台判断当前画面是否满足受支持的原神对白布局。</summary>
        public Task<SceneSelection> SelectAsync(ReadOnlyMemory<byte> image, SceneSettings settings, CancellationToken cancellationToken)
        {
            Calls++; Bytes = image.ToArray();
            return Task.FromResult(new SceneSelection(new(kind, kind == SceneDecisionKind.Selected ? "test.layout" : null,
                "TestReason", "test-v1", "catalog-hash", new Dictionary<string, double>()), kind == SceneDecisionKind.Selected ? Profile : null));
        }
    }
    /// <summary>移植回归验证：Layouts。</summary>
    internal sealed class Layouts : ISceneProfileLoader
    {
        /// <summary>移植回归验证：Calls。</summary>
        public int Calls { get; private set; }
        /// <summary>移植回归验证：LastPath。</summary>
        public string? LastPath { get; private set; }
        /// <summary>异步读取并严格校验外置配置或布局。</summary>
        public Task<SceneProfile> LoadAsync(string path, CancellationToken cancellationToken)
        { Calls++; LastPath = path; return Task.FromResult(Profile); }
    }
    /// <summary>移植回归验证：Engine。</summary>
    internal sealed class Engine : IOcrEngine
    {
        /// <summary>移植回归验证：Calls。</summary>
        public int Calls { get; private set; }
        /// <summary>移植回归验证：Body。</summary>
        public string Body { get; init; } = "测试正文";
        /// <summary>移植回归验证：Tensors。</summary>
        public IReadOnlyList<OcrTensor>? Tensors { get; private set; }
        /// <summary>在取消检查和配置约束下执行识别。</summary>
        public Task<(IReadOnlyList<OcrText> Texts, OcrModelInfo Model)> RecognizeAsync(IReadOnlyList<OcrTensor> tensors,
            OcrSettings settings, string profileDirectory, CancellationToken cancellationToken)
        {
            Calls++; Tensors = tensors;
            return Task.FromResult<(IReadOnlyList<OcrText>, OcrModelInfo)>(([new("姓名", 1), new(Body, 1)], new("test.onnx", "model-hash", "controlled", 0, null)));
        }
    }
    /// <summary>释放本实例拥有的本地资源。</summary>
    public void Dispose() { Directory.Delete(_directory, true); }
}

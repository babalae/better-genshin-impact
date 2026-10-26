using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Audio;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Ocr;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;
using NAudio.Wave;
using Microsoft.ML.OnnxRuntime;
using Serilog;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Diagnostics;

namespace BetterGenshinImpact.GameTask.QuestVoice;

/// <summary>把 BetterGI 持久化配置转换为原业务核心快照，不依赖 WinUI 或源仓库。</summary>
public sealed class QuestVoiceSessionFactory(QuestVoiceConfig config, string programDirectory) : IQuestVoiceSessionFactory
{
    /// <summary>沿用来源项目经过验证的稳定窗口和差分参数。</summary>
    public static ContinuousSettings ContinuousDefaults { get; } = new(350, 3, 1500, 600, 2000, 256, 32, .01);
    /// <summary>每次Create生成独立计数器，Service仅观察当前运行会话。</summary>
    public QuestVoiceMetrics Metrics { get; private set; } = new();
    /// <summary>部署与诊断资源所在目录。</summary>
    public string ConfigurationDirectory => Path.Combine(programDirectory, "User", "QuestVoice");
    /// <summary>序列化首次资源部署，避免并发检查读取半份示例。</summary>
    private readonly SemaphoreSlim _resourceGate = new(1, 1);
    /// <summary>本次设置的规范化路径，不依赖进程工作目录。</summary>
    private string Resolve(string path) => Path.GetFullPath(path, programDirectory);

    /// <summary>只创建缺失的示例，不覆盖用户编辑的外置映射或布局。</summary>
    public async Task EnsureResourcesAsync(CancellationToken token)
    {
        await _resourceGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(ConfigurationDirectory);
            foreach (var (resource, name) in new[] { ("auto-scene.json", "auto-scene.json"),
                         ("dialogue-one-line.xml", "dialogue-one-line.xml"), ("routing.example.json", "routing.json") })
            {
                var path = Path.Combine(ConfigurationDirectory, name);
                if (File.Exists(path)) continue;
                await using var input = typeof(QuestVoiceSessionFactory).Assembly.GetManifestResourceStream(
                    "BetterGenshinImpact.Resources.QuestVoice." + resource)
                    ?? throw new InvalidOperationException("缺少内嵌的 QuestVoice 布局资源：" + resource);
                var temporary = path + ".seed-" + Guid.NewGuid().ToString("N");
                try
                {
                    await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                        4096, FileOptions.Asynchronous))
                        await input.CopyToAsync(output, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    try { File.Move(temporary, path, overwrite: false); }
                    catch (IOException) when (File.Exists(path)) { /* 另一实例已完成部署，不覆盖。 */ }
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
        finally { _resourceGate.Release(); }
    }

    /// <summary>校验地址、采样、文件和路由引用；不声称已经连接 TTS 或运行 OCR。</summary>
    public async Task<QuestVoiceValidation> ValidateAsync(CancellationToken token)
    {
        var errors = new List<string>();
        var routingSummary = "固定声线；所有角色使用默认 NPC 声线。";
        try
        {
            token.ThrowIfCancellationRequested();
            await EnsureResourcesAsync(token).ConfigureAwait(false);
            var settings = CreateTtsSettings();
            var capabilities = TtsProviderRegistry.GetInfo(settings.Provider!.ProviderId).Capabilities;
            if (config.SampleIntervalMilliseconds is < 50 or > 500)
                errors.Add("捕获采样间隔必须为 50～500 毫秒。");
            if (config.DeviceId < 0) errors.Add("OCR DirectML 设备序号必须为非负整数。");

            // 启动时创建不可变快照，运行中编辑配置不会改变某一轮的声线或布局。
            var voice = settings.Voice;
            var ocr = new OcrSettings(ResolveOptional(config.OcrModelPath),
                Path.Combine(ConfigurationDirectory, "dialogue-one-line.xml"), config.DeviceId);
            CheckFile(ocr.ModelPath, "OCR PP-OCRv6 模型", errors);
            var scene = new SceneSettings(Path.Combine(ConfigurationDirectory, "auto-scene.json"));
            await new SceneCatalog().LoadAsync(scene.CatalogPath, token).ConfigureAwait(false);
            RoutingSettings? routing = null;
            if (capabilities.VoiceAssets && config.RoutingEnabled)
            {
                routing = new(Resolve(config.RoutingCatalogPath), voice.ServerSidePaths);
                var catalog = (await new SpeakerCatalogLoader().LoadAsync(routing.CatalogPath, token, voice.ServerSidePaths).ConfigureAwait(false)).Catalog;
                // 主角和昵称直接由原路由器解释；这里提供只读摘要。
                routingSummary = $"角色 {catalog.Speakers.Count} 项，声线 {catalog.Voices.Count} 项；旅行者：{catalog.ProtagonistId ?? "未指定"}；"
                    + $"昵称：{string.Join("、", catalog.PlayerNames)}；NPC：{catalog.FallbackVoiceId ?? voice.Name}";
                if (catalog.FallbackVoiceId is null) CheckVoice(voice, errors);
                foreach (var binding in catalog.Voices)
                {
                    var reason = await RoutingVoiceAssets.UnavailableAsync(binding, token, voice.ServerSidePaths).ConfigureAwait(false);
                    if (reason is not null && (catalog.MissingVoicePolicy == MissingVoicePolicy.Fail
                        || string.Equals(binding.Id, catalog.FallbackVoiceId, StringComparison.OrdinalIgnoreCase)))
                        errors.Add($"声线 {binding.Id}：{reason}");
                }
            }
            else if (capabilities.VoiceAssets) CheckVoice(voice, errors);
            else routingSummary = "当前引擎使用默认预设音色；原 GPT 角色映射已保留，切回后生效。";
            settings = settings with { Ocr = ocr, Scene = scene, Routing = routing };
            return new(errors.Count == 0, errors.Count == 0
                ? "文件与路由校验通过；OCR 推理、后端合成和音频输出待实际运行验证。"
                : string.Join(Environment.NewLine, errors), routingSummary, settings);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            errors.Add(exception.Message);
            return new(false, string.Join(Environment.NewLine, errors), routingSummary, null);
        }
    }

    /// <summary>仅构造 TTS 快照，测试语音不要求 OCR 模型或截图环境。</summary>
    public AppSettings CreateTtsSettings(bool validateProvider = true)
    {
        var info = TtsProviderRegistry.GetInfo(config.ProviderId);
        var remote = info.Capabilities.ServerConnection && config.UseRemoteService;
        var address = remote ? config.RemoteServiceUrl : config.ServiceUrl;
        if (!info.Capabilities.ServerConnection) address = "https://translate.volcengine.com/";
        if (!Uri.TryCreate(address?.Trim(), UriKind.Absolute, out var endpoint))
            throw new VoiceException(VoiceErrorCode.Configuration, "请填写有效的 HTTP/HTTPS 服务地址。");
        endpoint = new Uri(endpoint.AbsoluteUri.TrimEnd('/') + "/");
        var voice = !info.Capabilities.VoiceAssets ? new VoiceSettings(info.DisplayName, "", "", "", "") : new VoiceSettings(config.VoiceName,
            remote ? config.RemoteGptWeightsPath.Trim() : ResolveOptional(config.GptWeightsPath),
            remote ? config.RemoteSovitsWeightsPath.Trim() : ResolveOptional(config.SovitsWeightsPath),
            remote ? config.RemoteReferenceAudioPath.Trim() : ResolveOptional(config.ReferenceAudioPath),
            remote ? config.RemotePromptText : config.PromptText, ServerSidePaths: remote);
        // 无资产的 Provider 完全不解析旧版路径（包括客户端平台不支持的旧值）。
        var streaming = info.Capabilities.Streaming && config.StreamingEnabled;
        var settings = new AppSettings(programDirectory, endpoint,
            new(config.ConnectTimeoutSeconds, config.RequestTimeoutSeconds, config.SynthesisTimeoutSeconds), voice,
            new(ConfigurationDirectory, ConfigurationDirectory), new(0, 0),
            Protocol: new(StreamingMode: streaming, StreamPlayback: streaming),
            Provider: new(info.Id, remote ? "remote" : "local", config.HuoshanVoiceId));
        using var provider = TtsProviderRegistry.Create(info.Id);
        if (validateProvider) provider.Validate(settings);
        return settings;
    }

    /// <summary>空路径保留为缺失，避免把程序目录当成文件。</summary>
    private string ResolveOptional(string? path) => string.IsNullOrWhiteSpace(path) ? "" : Resolve(path.Trim());
    /// <summary>可读、非空是静态校验，模型格式在实际推理时进一步校验。</summary>
    private static void CheckFile(string path, string label, ICollection<string> errors)
    {
        try
        {
            using var file = File.OpenRead(path);
            if (file.Length == 0) errors.Add(label + "为空：" + path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { errors.Add($"{label}缺失或不可读：{path}（{exception.Message}）"); }
    }
    /// <summary>兜底必须有可用的成套本机资产。</summary>
    private static void CheckVoice(VoiceSettings voice, ICollection<string> errors)
    {
        foreach (var (path, label) in new[] { (voice.GptWeightsPath, "GPT 权重"),
                     (voice.SovitsWeightsPath, "SoVITS 权重"), (voice.ReferenceAudioPath, "参考音频") })
        {
            if (!voice.ServerSidePaths) CheckFile(path, label, errors);
            else if (string.IsNullOrWhiteSpace(path)) errors.Add(label + "服务器路径不能为空。");
        }
        if (string.IsNullOrWhiteSpace(voice.Name) || string.IsNullOrWhiteSpace(voice.PromptText))
            errors.Add("请填写默认声线名称及参考音频的逐字原文。");
    }

    /// <summary>资源按会话拥有，发生部分创建失败时立刻释放已经创建的资源。</summary>
    public async Task<IQuestVoiceSession> CreateAsync(CancellationToken token)
    {
        var validation = await ValidateAsync(token).ConfigureAwait(false);
        if (!validation.IsValid || validation.Settings is null) throw new InvalidOperationException(validation.Detail);
        // 绝不为配音替换全进程 ONNX DLL；CUDA/OpenVINO 等可选运行库可能没有 DirectML。
        if (!OrtEnv.Instance().GetAvailableProviders().Contains("DmlExecutionProvider", StringComparer.Ordinal))
            throw new InvalidOperationException("当前 BetterGI 原生 ONNX 运行库不包含 DirectML。QuestVoice OCR 需要 DirectML；请在硬件加速设置选择内置 CPU / DirectML 并重启 BetterGI。不会为配音替换正在使用的运行库。");
        var scratch = Path.Combine(ConfigurationDirectory, "cache", Guid.NewGuid().ToString("N"));
        var settings = validation.Settings with { Output = new(scratch, Path.Combine(scratch, "audio")) };
        var engine = TtsProviderRegistry.Create(settings.Provider!.ProviderId);
        var metrics = Metrics = new QuestVoiceMetrics();
        var ocr = new DirectMlOcrEngine(metrics);
        var player = new WasapiAudioPlayer(() => new WasapiOut());
        try
        {
            var catalog = new SceneCatalog();
            var selector = new GenshinSceneSelector(catalog);
            var images = new ImageOcrService(new SceneProfileLoader(), new ImagePreprocessor(), ocr, selector, metrics);
            var logger = QuestVoiceLog.Diagnostic.ForContext("SourceContext", "QuestVoice");
            var routing = new VoiceRoutingService(new SpeakerCatalogLoader(), new SpeakerRouter(), logger);
            var voice = new ContinuousVoiceSession(settings, engine, new WavProcessor(), new AudioArtifactStore(), player, routing, logger);
            var frames = new ContinuousFrameSession(ContinuousDefaults,
                new ContinuousFrameInspector(ContinuousDefaults, settings.Scene!, catalog, selector),
                new ContinuousFrameRecognizer(images, settings), voice, logger, metrics);
            return new QuestVoiceSession(frames, voice, engine, ocr, player, scratch);
        }
        catch { player.Dispose(); ocr.Dispose(); engine.Dispose(); throw; }
    }
}

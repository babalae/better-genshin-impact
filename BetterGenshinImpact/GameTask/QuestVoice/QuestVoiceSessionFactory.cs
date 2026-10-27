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
using Newtonsoft.Json;

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
    /// <summary>管理器读取实际选择的目录，不依赖当前进程工作目录。</summary>
    public string RoutingCatalogPath => Resolve(config.RoutingCatalogPath);
    /// <summary>未指定实例的历史 GPT profile 按用户原来的本地/远程选择解释。</summary>
    public bool RoutingServerSidePaths => config.UseRemoteService;
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
            if (config.RoutingEnabled)
            {
                var selectedRouting = new RoutingSettings(RoutingCatalogPath, RoutingServerSidePaths);
                var catalog = (await new SpeakerCatalogLoader().LoadAsync(selectedRouting.CatalogPath, token, selectedRouting.ServerSidePaths).ConfigureAwait(false)).Catalog;
                // 旧目录在普通引擎下继续保持原来的固定声音语义；保存升级后显式启用通用映射。
                if (capabilities.VoiceAssets || catalog.Version >= 2) routing = selectedRouting;
                // 主角和昵称直接由原路由器解释；这里提供只读摘要。
                routingSummary = $"角色 {catalog.Speakers.Count} 项，声线 {catalog.Voices.Count} 项；旅行者：{catalog.ProtagonistId ?? "未指定"}；"
                    + $"昵称：{string.Join("、", catalog.PlayerNames)}；NPC：{catalog.FallbackVoiceId ?? voice.Name}";
                if (routing is not null && catalog.FallbackVoiceId is null && capabilities.VoiceAssets) CheckVoice(voice, errors);
                foreach (var binding in routing is null ? Array.Empty<RoutingVoice>() : catalog.Voices)
                {
                    // Provider 参数和资源分别校验；普通声音不读取 GPT 权重。
                    try { CreateVoiceSettings(binding); }
                    catch (Exception exception) { errors.Add($"声线 {binding.Id}：{exception.Message}"); }
                    var reason = await RoutingVoiceAssets.UnavailableAsync(binding, token, selectedRouting.ServerSidePaths).ConfigureAwait(false);
                    if (reason is not null && (catalog.MissingVoicePolicy == MissingVoicePolicy.Fail
                        || string.Equals(binding.Id, catalog.FallbackVoiceId, StringComparison.OrdinalIgnoreCase)))
                        errors.Add($"声线 {binding.Id}：{reason}");
                }
            }
            else if (capabilities.VoiceAssets) CheckVoice(voice, errors);
            else routingSummary = "角色路由已关闭，使用当前引擎默认声线。";
            settings = settings with { Ocr = ocr, Scene = scene, Routing = routing };
            if (routing is not null) settings = settings with { Protocol = settings.Protocol! with
                { StreamingMode = config.StreamingEnabled, StreamPlayback = config.StreamingEnabled } };
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
        => CreateTtsSettings(config.ProviderId,
            (ExtraConfig(config.ProviderId)?.UseRemote ?? config.UseRemoteService) && TtsProviderRegistry.GetInfo(config.ProviderId).Capabilities.ServerConnection ? "remote" : "local",
            ExtraConfig(config.ProviderId)?.VoiceId ?? config.HuoshanVoiceId, validateProvider);

    /// <summary>设置页指定引擎的旧默认声线快照，沿用生产路径与实例解析，不改变全局引擎。</summary>
    public AppSettings CreateProviderSettings(string providerId, bool validateProvider = true) => CreateTtsSettings(providerId,
        TtsProviderRegistry.GetInfo(providerId).Capabilities.ServerConnection && (ExtraConfig(providerId)?.UseRemote ?? config.UseRemoteService) ? "remote" : "local",
        ExtraConfig(providerId)?.VoiceId ?? config.HuoshanVoiceId, validateProvider);

    /// <summary>资源草稿验证使用与运行时一致的相对路径基准。</summary>
    public string ProgramDirectory => programDirectory;

    /// <summary>新增引擎独立配置；旧配置缺失字典条目时补齐缺省，不触碰旧引擎。</summary>
    public QuestVoiceEngineConfig? ExtraConfig(string providerId)
    {
        var defaults = QuestVoiceEngineConfig.Defaults();
        if (!defaults.TryGetValue(providerId, out var fallback)) return null;
        config.ExtraProviders ??= defaults;
        if (!config.ExtraProviders.TryGetValue(providerId, out var selected) || selected is null)
        {
            selected = fallback;
            config.ExtraProviders = new(config.ExtraProviders) { [providerId] = selected };
        }
        return selected;
    }

    /// <summary>为一个明确 Provider/实例构造快照，不改写用户配置或另一实例的路径。</summary>
    private AppSettings CreateTtsSettings(string providerId, string instanceId, string providerVoiceId, bool validateProvider,
        VoiceBinding? binding = null)
    {
        var info = TtsProviderRegistry.GetInfo(providerId);
        if (instanceId is not ("local" or "remote") || (!info.Capabilities.ServerConnection && instanceId == "remote"))
            throw new VoiceException(VoiceErrorCode.Configuration, "所选声线引用了未配置的服务实例：" + instanceId);
        var remote = info.Capabilities.ServerConnection && instanceId == "remote";
        var extra = ExtraConfig(providerId);
        var address = extra is null ? remote ? config.RemoteServiceUrl : config.ServiceUrl
            : remote ? extra.RemoteUrl : extra.LocalUrl;
        if (!info.Capabilities.ServerConnection) address = "https://translate.volcengine.com/";
        if (!Uri.TryCreate(address?.Trim(), UriKind.Absolute, out var endpoint))
            throw new VoiceException(VoiceErrorCode.Configuration, "请填写有效的 HTTP/HTTPS 服务地址。");
        endpoint = new Uri(endpoint.AbsoluteUri.TrimEnd('/') + "/");
        var voice = !info.Capabilities.VoiceAssets ? new VoiceSettings(info.DisplayName, "", "", "", "")
            : binding is not null ? new VoiceSettings(binding.Name, binding.GptWeightsPath, binding.SovitsWeightsPath,
                binding.References.FirstOrDefault()?.Path ?? "", binding.References.FirstOrDefault()?.PromptText ?? "", ServerSidePaths: remote)
            : new VoiceSettings(config.VoiceName,
            remote ? config.RemoteGptWeightsPath.Trim() : ResolveOptional(config.GptWeightsPath),
            remote ? config.RemoteSovitsWeightsPath.Trim() : ResolveOptional(config.SovitsWeightsPath),
            remote ? config.RemoteReferenceAudioPath.Trim() : ResolveOptional(config.ReferenceAudioPath),
            remote ? config.RemotePromptText : config.PromptText, ServerSidePaths: remote);
        // 无资产的 Provider 完全不解析旧版路径（包括客户端平台不支持的旧值）。
        var streaming = info.Capabilities.Streaming && config.StreamingEnabled;
        var providerReference = new TtsProviderSettings(info.Id, remote ? "remote" : "local", providerVoiceId);
        voice = voice with { Provider = providerReference };
        var settings = new AppSettings(programDirectory, endpoint,
            new(config.ConnectTimeoutSeconds, config.RequestTimeoutSeconds, config.SynthesisTimeoutSeconds), voice,
            new(ConfigurationDirectory, ConfigurationDirectory), new(0, 0),
            Protocol: new(StreamingMode: streaming, StreamPlayback: streaming),
            Provider: providerReference,
            Extra: extra is null ? null : new(extra.Language, extra.Model, extra.Format, extra.ApiKey, extra.Rate, extra.Pitch));
        using var provider = TtsProviderRegistry.Create(info.Id);
        if (validateProvider) provider.Validate(settings);
        return settings;
    }

    /// <summary>指定 profile 的实例与声音快照，供运行时和试听复用。</summary>
    public AppSettings CreateVoiceSettings(RoutingVoice profile, bool validateProvider = true)
        => ResolveVoiceSettings(RoutingVoiceAssets.Snapshot(profile), validateProvider);

    /// <summary>绑定只携带声音引用和资产；连接、超时、语言等继续来自实例设置。</summary>
    private AppSettings ResolveVoiceSettings(VoiceBinding binding, bool validateProvider = true)
    {
        var reference = binding.Provider;
        var providerId = reference?.ProviderId ?? config.ProviderId;
        var capabilities = TtsProviderRegistry.GetInfo(providerId).Capabilities;
        var instance = string.IsNullOrWhiteSpace(reference?.InstanceId)
            ? capabilities.ServerConnection && (ExtraConfig(providerId)?.UseRemote ?? config.UseRemoteService) ? "remote" : "local" : reference.InstanceId;
        var settings = CreateTtsSettings(providerId, instance!, reference?.VoiceId ?? ExtraConfig(providerId)?.VoiceId ?? config.HuoshanVoiceId, validateProvider, binding);
        if (!capabilities.VoiceAssets) return settings;
        var audio = binding.References.FirstOrDefault();
        return settings with { Voice = settings.Voice with
        {
            Name = binding.Name, GptWeightsPath = binding.GptWeightsPath, SovitsWeightsPath = binding.SovitsWeightsPath,
            ReferenceAudioPath = audio?.Path ?? "", PromptText = audio?.PromptText ?? "", ReferenceEmotion = audio?.Emotion
        } };
    }

    /// <summary>克隆启动配置后创建分派器，运行期间 UI 编辑不会改变地址或实例。</summary>
    public ITtsProvider CreateRoutingProvider()
    {
        var frozenConfig = JsonConvert.DeserializeObject<QuestVoiceConfig>(JsonConvert.SerializeObject(config))!;
        var frozen = new QuestVoiceSessionFactory(frozenConfig, programDirectory);
        return new RoutedTtsProvider(binding => frozen.ResolveVoiceSettings(binding), TtsProviderRegistry.GetInfo(frozenConfig.ProviderId));
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
        var engine = settings.Routing is not null ? CreateRoutingProvider() : TtsProviderRegistry.Create(settings.Provider!.ProviderId);
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

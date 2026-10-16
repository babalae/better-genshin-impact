using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

/// <summary>仅属于本次启动的提示，不从持久化的历史验证错误恢复。</summary>
public sealed record InferenceStartupStatus(string FallbackMessage, bool IsMainInstance = true);

/// <summary>设备配置先发布；主窗口加载后的友好验证不作为业务初始化的前置条件。</summary>
public sealed class InferenceRuntimeService(
    IConfigService configService, IOnnxRuntimeSelection loader, IInferenceDeviceDiscovery devices,
    IInferenceModelDiagnostics diagnostics, ILogger<BgiOnnxFactory> logger, InferenceStartupStatus? startupStatus = null) : IDisposable
{
    private sealed record PublishedRuntime(ResolvedInferenceRuntime Runtime, bool IsValidated);

    private PublishedRuntime? _published;
    private readonly object _initializationLock = new();
    private readonly object _settingsLock = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _initializationTask;
    private Task? _validationTask;
    private long _settingsRevision;
    private long _startupRevision;
    private PublishedRuntime Published => Volatile.Read(ref _published)
        ?? throw new InvalidOperationException("推理初始化尚未完成。");
    public ResolvedInferenceRuntime Current => Published.Runtime;
    public bool IsReady => Volatile.Read(ref _published) != null;
    public bool IsStopping => _lifetime.IsCancellationRequested;
    public bool IsCurrentValidated => Volatile.Read(ref _published)?.IsValidated ?? false;
    public bool HasPendingRestart => Interlocked.Read(ref _settingsRevision) != Interlocked.Read(ref _startupRevision);
    public string StartupMessage { get; private set; } = startupStatus?.FallbackMessage ?? "";
    public event EventHandler? SettingsChanged;
    private bool IsMainInstance => startupStatus?.IsMainInstance ?? true;
    private HardwareAccelerationConfig Config => configService.Get().HardwareAccelerationConfig;

    public static InferenceStartupStatus PrepareStartup(HardwareAccelerationConfig config, bool isMainInstance = true)
    {
        var fallbackMessage = "";
        config.LastValidationError ??= "";
        config.VerifiedSignature ??= "";
        config.ValidatedEngines ??= [];
        // 新增 OCR 参数单独修正，避免无效比例或换用核心数较少的处理器时丢失其他引擎设置。
        config.PaddleOcrCpu = PaddleOcrCpuConfig.FromOptions((config.PaddleOcrCpu ?? new()).ToOptions().Normalize());
        if (config.IsExpectedConfiguration && !string.IsNullOrWhiteSpace(config.VerifiedSignature))
        {
            // 兼容已有的单份验证签名：只恢复能与保存参数匹配的历史成功记录。
            foreach (var engine in Enum.GetValues<InferenceDeviceType>().Where(engine => engine != InferenceDeviceType.Cpu))
            {
                var settings = config.GetEngineSettings(engine);
                if (string.IsNullOrWhiteSpace(settings.DeviceId)) continue;
                if (InferenceRuntimeFingerprint.Compute(config, engine) == config.VerifiedSignature)
                    config.RecordValidatedEngine(engine);
            }
        }
        if (!config.IsExpectedConfiguration)
        {
            config.ResetToDefaults();
            config.LastFailedEngine = null;
            config.LastValidationError = "";
        }
        else if (!config.InferenceDevice.IsSelectable())
        {
            // TensorRT 已下线：只把生效引擎改回 CPU，其他引擎已保存的设备参数保持不变。
            var retiredEngine = config.InferenceDevice;
            config.SwitchToCpu();
            config.LastFailedEngine = null;
            config.LastValidationError = "";
            fallbackMessage = $"{retiredEngine} 推理引擎已停止提供（首次构建引擎耗时过长），本次已使用 CPU。\r\n\r\n" +
                              "请在首页的“硬件加速设置”中选择其他引擎后重试。";
        }
        else if (isMainInstance && config.ValidationState == InferenceValidationState.Validating)
        {
            var failedEngine = config.InferenceDevice;
            config.SwitchToCpu();
            config.LastFailedEngine = failedEngine;
            config.LastValidationError = "上一次推理验证未正常完成，已回退 CPU。";
            fallbackMessage = $"{failedEngine} 上一次推理验证未正常完成，本次已使用 CPU。";
        }
        // PendingRestart 在窗口加载前保持原样，真正开始验证时才记录 Validating。
        return new(fallbackMessage, isMainInstance);
    }

    /// <summary>只准备设备与生效配置，不创建模型、运行推理或弹出提示。</summary>
    public Task InitializeAsync(CancellationToken token = default)
    {
        lock (_initializationLock) return _initializationTask ??= InitializeCoreAsync(token);
    }

    private async Task InitializeCoreAsync(CancellationToken token)
    {
        if (IsReady) return;
        var config = Config;
        // LastValidationError 只用于设置页的历史详情，不能冒充本次启动的失败重新弹窗。
        if (loader.InferenceDevice != config.InferenceDevice)
        {
            FallBack(config.InferenceDevice, "运行库加载",
                string.IsNullOrWhiteSpace(loader.FailureReason)
                    ? "所选运行库无法加载或依赖不完整，请在硬件加速设置中重新检测并补齐依赖。"
                    : loader.FailureReason,
                loader.StatusText);
            return;
        }
        try
        {
            var selected = config.GetEngineSettings(config.InferenceDevice);
            var found = await Task.Run(() => devices.GetDevices(config.InferenceDevice, loader.RuntimeDirectory), token);
            var device = found.FirstOrDefault(d => d.Id == selected.DeviceId)
                ?? throw new InvalidOperationException("保存的推理设备不可用，请重新选择设备。");
            var runtime = ResolvedInferenceRuntime.Resolve(config, device, loader.RuntimeDirectory);
            var isValidated = runtime.Engine == InferenceDeviceType.Cpu
                || (config.ValidationState == InferenceValidationState.None && config.VerifiedSignature == runtime.Fingerprint);
            _startupRevision = Interlocked.Read(ref _settingsRevision);
            Volatile.Write(ref _published, new(runtime, isValidated));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e)
        {
            logger.LogWarning(e, "[ONNX]推理设备初始化失败，回退 CPU");
            var reason = e.Message.Contains("设备不可用", StringComparison.Ordinal)
                ? e.Message
                : "无法初始化保存的推理设备，请在硬件加速设置中重新选择设备。";
            FallBack(config.InferenceDevice, "设备初始化", reason, e.Message);
        }
    }

    /// <summary>仅由主实例的 MainWindowViewModel.OnLoaded 调用；重复调用共用同一任务。</summary>
    public Task ValidatePendingAsync(CancellationToken token = default)
    {
        if (!IsMainInstance) return Task.CompletedTask;
        lock (_initializationLock) return _validationTask ??= ValidatePendingCoreAsync(token);
    }

    private async Task ValidatePendingCoreAsync(CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await InitializeAsync(linked.Token);
        var published = Published;
        if (published.IsValidated)
        {
            if (!HasPendingRestart && Config.ValidationState == InferenceValidationState.PendingRestart)
            {
                Config.ValidationState = InferenceValidationState.None;
                configService.Save();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
            return;
        }
        var runtime = published.Runtime;
        bool ownsSavedSettings;
        lock (_settingsLock)
        {
            ownsSavedSettings = _settingsRevision == _startupRevision;
            if (ownsSavedSettings) Config.ValidationState = InferenceValidationState.Validating;
        }
        // 验证中的状态必须先落盘，原生调用异常中断后下一次启动才能安全回退。
        if (ownsSavedSettings) configService.Save();
        try
        {
            // 缺图不是引擎故障：古树场景仍待接入，先通过独立 OCR 会话验证引擎。
            var factory = new BgiOnnxSessionFactory(logger, runtime);
            if (diagnostics.HasDomainTreeImage)
                await diagnostics.ValidateTreeAsync(factory, linked.Token).ConfigureAwait(false);
            else
                await diagnostics.ValidateOcrAsync(factory, linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            lock (_settingsLock)
            {
                Volatile.Write(ref _published, published with { IsValidated = true });
                ownsSavedSettings = _settingsRevision == _startupRevision;
                // 即使验证期间保存了其他引擎，也保留本次成功记录；不覆盖新的待重启设置。
                Config.RecordValidatedEngine(runtime.Engine);
                if (ownsSavedSettings)
                {
                    Config.VerifiedSignature = runtime.Fingerprint;
                    Config.ValidationState = InferenceValidationState.None;
                    Config.LastFailedEngine = null;
                    Config.LastValidationError = "";
                }
            }
            configService.Save();
        }
        catch (OperationCanceledException)
        {
            // 正常退出取消友好验证并不说明引擎不可用，保留待验证设置供下次主实例加载。
            lock (_settingsLock)
            {
                ownsSavedSettings = _settingsRevision == _startupRevision;
                if (ownsSavedSettings) Config.ValidationState = InferenceValidationState.PendingRestart;
            }
            if (ownsSavedSettings) configService.Save();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "[ONNX]主窗口推理验证失败，回退 CPU");
            var validationError = e as InferenceValidationException;
            FallBack(runtime.Engine,
                validationError?.Scene ?? "模型验证",
                validationError?.UserReason ?? "加载或运行验证模型时发生错误，详细错误已记录到硬件加速设置和日志中。",
                e.ToString(),
                _startupRevision);
        }
        finally { SettingsChanged?.Invoke(this, EventArgs.Empty); }
    }

    private void FallBack(InferenceDeviceType engine, string stage, string userReason, string technicalDetails,
        long? validationRevision = null)
    {
        lock (_settingsLock)
        {
            // 非主实例只能回退本进程的运行时快照，不能改写主实例共享的持久化设置。
            if (IsMainInstance)
            {
                // 验证期间用户可能已保存新的设置，旧验证结果不能覆盖下一次启动的选择。
                if (validationRevision == null || _settingsRevision == validationRevision)
                {
                    Config.SwitchToCpu();
                }
                Config.LastFailedEngine = engine;
            }
            if (IsMainInstance)
                Config.LastValidationError = $"失败环节：{stage}\r\n失败原因：{userReason}\r\n\r\n技术详情：{technicalDetails}";
            StartupMessage = $"{engine} 加速验证失败，已自动切换为 CPU。\r\n\r\n" +
                             $"失败环节：{stage}\r\n失败原因：{userReason}\r\n\r\n" +
                             "软件仍可正常使用。你可以继续使用 CPU，或在首页的“硬件加速设置”中选择其他设备后重试。";
            var cpu = ResolvedInferenceRuntime.Cpu(Config.Cpu.CpuThreads, Config.PaddleOcrCpu.ToOptions(),
                loader.RuntimeDirectory);
            Volatile.Write(ref _published, new(cpu, true));
        }
        if (IsMainInstance) configService.Save();
    }

    public void Apply(InferenceDeviceType engine, InferenceEngineSettings settings, bool cpuOcr, int cpuThreads,
        PaddleOcrCpuOptions? paddleOcrCpu = null)
    {
        if (!Enum.IsDefined(engine) || string.IsNullOrWhiteSpace(settings.DeviceId) || cpuThreads < 0 || cpuThreads > Environment.ProcessorCount)
            throw new ArgumentException("推理设置不符合预期。");
        if (paddleOcrCpu is { IsValid: false }) throw new ArgumentException("OCR CPU 线程策略参数不符合预期。");
        // TensorRT 已下线：任何调用方都不能再把生效引擎切回该引擎。
        if (!engine.IsSelectable()) throw new ArgumentException($"{engine} 推理引擎已停止提供，请选择其他引擎。");
        lock (_settingsLock)
        {
            _settingsRevision++;
            var config = Config;
            var target = config.GetEngineSettings(engine);
            target.DeviceId = settings.DeviceId;
            target.EnableModelCache = settings.EnableModelCache;
            config.Cpu.CpuThreads = cpuThreads;
            if (paddleOcrCpu != null) config.PaddleOcrCpu = PaddleOcrCpuConfig.FromOptions(paddleOcrCpu);
            config.CpuOcr = cpuOcr;
            config.InferenceDevice = engine;
            config.FormatVersion = 1;
            config.ValidationState = InferenceValidationState.PendingRestart;
        }
        configService.Save();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task StopAsync()
    {
        _lifetime.Cancel();
        Task? validation;
        lock (_initializationLock) validation = _validationTask;
        if (validation != null) await validation;
    }

    public void Dispose() => _lifetime.Dispose();
}

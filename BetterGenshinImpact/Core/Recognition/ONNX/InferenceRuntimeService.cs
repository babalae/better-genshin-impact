using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

/// <summary>生效状态只在启动验证结束时发布。设置应用只保存下次启动的配置。</summary>
public sealed class InferenceRuntimeService(
    IConfigService configService, IOnnxRuntimeSelection loader, IInferenceDeviceDiscovery devices,
    IInferenceModelDiagnostics diagnostics, ILogger<BgiOnnxFactory> logger)
{
    private InferenceRuntimeSnapshot? _current;
    private readonly object _initializationLock = new();
    private Task? _initializationTask;
    public InferenceRuntimeSnapshot Current => _current ?? throw new InvalidOperationException("推理初始化尚未完成。");
    public bool IsReady => _current != null;
    public string StartupMessage { get; private set; } = "";
    public event EventHandler? SettingsChanged;
    private HardwareAccelerationConfig Config => configService.Get().HardwareAccelerationConfig;

    public static void PrepareStartup(HardwareAccelerationConfig config)
    {
        config.LastValidationError ??= "";
        config.VerifiedSignature ??= "";
        if (!config.IsExpectedConfiguration)
        {
            config.ResetToCpu();
            config.LastFailedEngine = null;
            config.LastValidationError = "";
        }
        else if (config.ValidationState == InferenceValidationState.Validating)
        {
            var failedEngine = config.InferenceDevice;
            config.ResetToCpu();
            config.LastFailedEngine = failedEngine;
            config.LastValidationError = "上一次推理验证未正常完成，已回退 CPU。";
        }
        else if (config.ValidationState == InferenceValidationState.PendingRestart)
        {
            config.ValidationState = InferenceValidationState.Validating;
        }
    }

    public Task InitializeAsync(CancellationToken token = default)
    {
        lock (_initializationLock) return _initializationTask ??= InitializeCoreAsync(token);
    }

    private async Task InitializeCoreAsync(CancellationToken token)
    {
        if (IsReady) return;
        var config = Config;
        if (config.LastValidationError.Length > 0) StartupMessage = config.LastValidationError;
        if (loader.InferenceDevice != config.InferenceDevice)
        {
            FallBack(config.InferenceDevice, loader.StatusText);
            return;
        }
        try
        {
            var selected = config.GetEngineSettings(config.InferenceDevice);
            var found = await Task.Run(() => devices.GetDevices(config.InferenceDevice, loader.RuntimeDirectory), token);
            var device = found.FirstOrDefault(d => d.Id == selected.DeviceId)
                ?? throw new InvalidOperationException("保存的推理设备不可用，请重新选择设备。");
            var snapshot = InferenceRuntimeSnapshot.Create(config, device, loader.RuntimeDirectory);
            if (config.ValidationState == InferenceValidationState.Validating)
            {
                if (snapshot.Engine != InferenceDeviceType.Cpu)
                {
                    // 缺图不是引擎故障：古树场景仍待接入，先通过独立 OCR 会话验证引擎。
                    var factory = new BgiOnnxFactory(logger, snapshot);
                    if (diagnostics.HasDomainTreeImage) await diagnostics.ValidateTreeAsync(factory, token);
                    else await diagnostics.ValidateOcrAsync(factory, token);
                }
                config.VerifiedSignature = snapshot.Signature;
                config.ValidationState = InferenceValidationState.None;
                config.LastFailedEngine = null;
                config.LastValidationError = "";
                configService.Save();
            }
            else if (snapshot.Engine != InferenceDeviceType.Cpu && config.VerifiedSignature != snapshot.Signature)
            {
                throw new InvalidOperationException("推理设置尚未通过验证，请在设置页重新应用。");
            }
            _current = snapshot;
        }
        catch (OperationCanceledException)
        {
            config.ResetToCpu();
            config.LastFailedEngine = null;
            config.LastValidationError = "";
            configService.Save();
            throw;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "[ONNX]推理验证失败，回退 CPU");
            FallBack(config.InferenceDevice, e.Message);
        }
    }

    private void FallBack(InferenceDeviceType engine, string message)
    {
        Config.ResetToCpu();
        Config.LastFailedEngine = engine;
        Config.LastValidationError = message;
        StartupMessage = $"{engine} 验证失败，已回退 CPU：{message}";
        _current = InferenceRuntimeSnapshot.CpuDefault();
        configService.Save();
    }

    public void Apply(InferenceDeviceType engine, InferenceEngineSettings settings, bool cpuOcr, int cpuThreads)
    {
        if (!Enum.IsDefined(engine) || string.IsNullOrWhiteSpace(settings.DeviceId) || cpuThreads < 0 || cpuThreads > Environment.ProcessorCount)
            throw new ArgumentException("推理设置不符合预期。");
        var config = Config;
        var target = config.GetEngineSettings(engine);
        target.DeviceId = settings.DeviceId;
        target.EnableModelCache = settings.EnableModelCache;
        config.Cpu.CpuThreads = cpuThreads;
        config.CpuOcr = cpuOcr;
        config.InferenceDevice = engine;
        config.FormatVersion = 1;
        config.ValidationState = InferenceValidationState.PendingRestart;
        configService.Save();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
}

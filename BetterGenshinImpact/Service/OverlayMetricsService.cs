using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Platform.Wine;
using BetterGenshinImpact.Service.Interface;
using LibreHardwareMonitor.Hardware;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using BetterGenshinImpact.Service.Model;
using BetterGenshinImpact.Service.Model.OverlayMetric;

namespace BetterGenshinImpact.Service;

public sealed class OverlayMetricsService : IDisposable
{
    // 调度器可能按几十毫秒上报一次，遮罩文本和硬件传感器分别节流，避免 UI 与 LibreHardwareMonitor 高频刷新。
    private static readonly TimeSpan PublishInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan HardwareRefreshInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan GpuQueryResetInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PeakProcessingCostWindow = TimeSpan.FromSeconds(5);

    private readonly ILogger<OverlayMetricsService> _logger = App.GetLogger<OverlayMetricsService>();
    private readonly IConfigService? _configService = App.GetService<IConfigService>();
    private readonly object _locker = new();
    private readonly object _hardwareLocker = new();
    private readonly MaskWindowConfig? _maskWindowConfig;

    private Computer? _computer;
    private bool _hardwareInitialized;
    private bool _hardwareWarningLogged;
    private GameGpuUsageProvider? _gameGpuUsageProvider;
    private bool _gpuMetricsUnavailable;
    private bool _gpuWarningLogged;

    private double? _gameFps;
    private double? _processingCostMs;
    private double? _peakProcessingCostMs;
    private double? _captureCostMs;
    private double? _triggerCostMs;
    private double? _cpuUsage;
    private double? _gpuUsage;
    private double? _memoryUsage;
    private double? _bgiCpuPercent;
    private TimeSpan _lastBgiCpuTime;
    private DateTime _lastBgiCpuSampleTime = DateTime.MinValue;

    private long _skippedTicks;
    private long _lastPublishedSkippedTicks;
    private readonly Queue<(DateTime Time, double Value)> _processingCostSamples = new();
    private DateTime _lastPublishTime = DateTime.MinValue;
    private DateTime _lastHardwareRefreshTime = DateTime.MinValue;

    public event EventHandler<OverlayMetricsSnapshot>? MetricsUpdated;

    public OverlayMetricsSnapshot CurrentSnapshot { get; private set; } = OverlayMetricsSnapshot.Empty;

    public OverlayMetricsService()
    {
        _maskWindowConfig = _configService?.Get().MaskWindowConfig;
        _maskWindowConfig?.EnsureOverlayMetricItems();
        if (_maskWindowConfig != null)
        {
            _maskWindowConfig.PropertyChanged += MaskWindowConfigOnPropertyChanged;
        }
    }

    public void UpdateGameFps(double fps)
    {
        lock (_locker)
        {
            _gameFps = fps;
        }

        TryPublish();
    }

    public void ClearGameFps()
    {
        // 仅清缓存值不主动发布：调用方停止帧率采样后会 Refresh(force) 统一发布，避免双发。
        lock (_locker)
        {
            _gameFps = null;
        }
    }

    public void RecordSkippedTick()
    {
        lock (_locker)
        {
            _skippedTicks++;
        }

        TryPublish();
    }

    public void RecordDispatcherTick(double processingCostMs, double captureCostMs, double triggerCostMs)
    {
        lock (_locker)
        {
            RecordPeakProcessingCost(processingCostMs, DateTime.UtcNow);
            _processingCostMs = Smooth(_processingCostMs, processingCostMs);
            _captureCostMs = Smooth(_captureCostMs, captureCostMs);
            _triggerCostMs = Smooth(_triggerCostMs, triggerCostMs);
        }

        TryPublish();
    }

    public void Refresh()
    {
        TryPublish(force: true);
    }

    private void MaskWindowConfigOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MaskWindowConfig.ShowOverlayMetrics)
            or nameof(MaskWindowConfig.OverlayMetricItems))
        {
            TryPublish(force: true, refreshHardware: false);
        }
    }

    private void TryPublish(bool force = false, bool refreshHardware = true)
    {
        var allConfig = _configService?.Get();
        var config = allConfig?.MaskWindowConfig;
        if (allConfig == null || config == null)
        {
            return;
        }

        config.EnsureOverlayMetricItems();
        MaintainGpuMetricsLifecycle(config);
        var now = DateTime.UtcNow;
        // 即使实时触发器高频运行，遮罩只需要半秒级更新；手动刷新和配置变更用 force 立即同步。
        if (!force && now - _lastPublishTime < PublishInterval)
        {
            return;
        }

        // 硬件采样相对更重，只有用户开启对应指标时才按秒级刷新。
        if (refreshHardware && ShouldRefreshHardware(config, now))
        {
            RefreshHardwareMetrics(config, now);
        }

        OverlayMetricsSnapshot snapshot;
        lock (_locker)
        {
            var elapsedSeconds = _lastPublishTime == DateTime.MinValue
                ? PublishInterval.TotalSeconds
                : Math.Max(0.001, (now - _lastPublishTime).TotalSeconds);
            var skippedDelta = _skippedTicks - _lastPublishedSkippedTicks;
            var skippedPerSecond = skippedDelta / elapsedSeconds;

            snapshot = BuildSnapshot(config, skippedPerSecond);
            CurrentSnapshot = snapshot;
            _lastPublishedSkippedTicks = _skippedTicks;
            _lastPublishTime = now;
        }

        MetricsUpdated?.Invoke(this, snapshot);
    }

    private bool ShouldRefreshHardware(MaskWindowConfig config, DateTime now)
    {
        if (!config.ShowOverlayMetrics || !HardwareMetricsEnabled(config))
        {
            return false;
        }

        return now - _lastHardwareRefreshTime >= HardwareRefreshInterval;
    }

    private static bool HardwareMetricsEnabled(MaskWindowConfig config)
    {
        return config.IsOverlayMetricEnabled(OverlayMetricItem.CpuUsage)
               || config.IsOverlayMetricEnabled(OverlayMetricItem.GpuUsage)
               || config.IsOverlayMetricEnabled(OverlayMetricItem.MemoryUsage);
    }

    private void MaintainGpuMetricsLifecycle(MaskWindowConfig config)
    {
        if (config.ShowOverlayMetrics && config.IsOverlayMetricEnabled(OverlayMetricItem.GpuUsage))
        {
            return;
        }

        if (_gameGpuUsageProvider == null && !_gpuMetricsUnavailable)
        {
            return;
        }

        lock (_hardwareLocker)
        {
            ReleaseGpuUsageProvider(resetAvailability: true);
        }
    }

    private OverlayMetricsSnapshot BuildSnapshot(MaskWindowConfig config, double skippedPerSecond)
    {
        if (!config.ShowOverlayMetrics)
        {
            return OverlayMetricsSnapshot.Empty;
        }

        var items = new List<OverlayMetricDisplayItem>();
        AddMetric(items, config, OverlayMetricItem.GameFps, _gameFps, value => $"{value:0}");
        AddMetric(items, config, OverlayMetricItem.ProcessingCost, _processingCostMs, value => $"{value:0}ms");
        AddMetric(items, config, OverlayMetricItem.PeakProcessingCost, _peakProcessingCostMs, value => $"{value:0}ms");
        AddMetric(items, config, OverlayMetricItem.CaptureCost, _captureCostMs, value => $"{value:0}ms");
        AddMetric(items, config, OverlayMetricItem.TriggerCost, _triggerCostMs, value => $"{value:0}ms");
        AddMetric(items, config, OverlayMetricItem.SkippedTicks, skippedPerSecond, value => $"{value:0}次/秒");
        AddMetric(items, config, OverlayMetricItem.GpuUsage, _gpuUsage, value => $"{value:0}%");
        AddMetric(items, config, OverlayMetricItem.CpuUsage, _cpuUsage, value => $"{value:0}%");
        AddMetric(items, config, OverlayMetricItem.MemoryUsage, _memoryUsage, value => $"{value:0}%");
        AddMetric(items, config, OverlayMetricItem.BgiMemoryUsage, GetBgiMemoryMb, FormatMemory);
        AddMetric(items, config, OverlayMetricItem.BgiCpuUsage, GetBgiCpuPercent, value => $"{value:F1}%");

        return items.Count == 0 ? OverlayMetricsSnapshot.Empty : new OverlayMetricsSnapshot(items);
    }

    private static void AddMetric(List<OverlayMetricDisplayItem> items, MaskWindowConfig config, OverlayMetricItem item, double? value, Func<double, string> formatter)
    {
        // 传感器不可用或数值未产生时直接跳过，遮罩不要显示 N/A 或空占位。
        if (!config.IsOverlayMetricEnabled(item) || value == null || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
        {
            return;
        }

        items.Add(new OverlayMetricDisplayItem(OverlayMetricItemDefaults.GetDisplayName(item), formatter(value.Value)));
    }

    // 进程级查询等昂贵采样必须走本惰性重载：未勾选对应指标时不执行取值，与硬件指标的门控语义保持一致。
    private static void AddMetric(List<OverlayMetricDisplayItem> items, MaskWindowConfig config, OverlayMetricItem item, Func<double?> valueProvider, Func<double, string> formatter)
    {
        if (!config.IsOverlayMetricEnabled(item))
        {
            return;
        }

        AddMetric(items, config, item, valueProvider(), formatter);
    }

    private void RecordPeakProcessingCost(double processingCostMs, DateTime now)
    {
        _processingCostSamples.Enqueue((now, processingCostMs));

        var cutoff = now - PeakProcessingCostWindow;
        while (_processingCostSamples.Count > 0 && _processingCostSamples.Peek().Time < cutoff)
        {
            _processingCostSamples.Dequeue();
        }

        _peakProcessingCostMs = _processingCostSamples.Count == 0
            ? null
            : _processingCostSamples.Max(sample => sample.Value);
    }

    private void RefreshHardwareMetrics(MaskWindowConfig config, DateTime now)
    {
        // 多个 TP Worker 会并发进入本方法（Timer 触发器线程 + 脚本线程）。
        // LibreHardwareMonitor 传感器采样较慢，用 TryEnter 避免排队等锁；
        // 抢不到说明上一轮采样还没结束，直接跳过本轮（秒级节流，跳过无影响）。
        if (!Monitor.TryEnter(_hardwareLocker, 0))
        {
            return;
        }

        try
        {
            var previousRefreshTime = _lastHardwareRefreshTime;
            _lastHardwareRefreshTime = now;
            if (previousRefreshTime != DateTime.MinValue && now - previousRefreshTime > GpuQueryResetInterval)
            {
                // 休眠或长时间暂停后重新建立 PDH 基线，避免展示跨越长时间窗口的平均值。
                ReleaseGpuUsageProvider(resetAvailability: true);
            }

            var cpuEnabled = config.IsOverlayMetricEnabled(OverlayMetricItem.CpuUsage);
            var gpuEnabled = config.IsOverlayMetricEnabled(OverlayMetricItem.GpuUsage);
            var memoryEnabled = config.IsOverlayMetricEnabled(OverlayMetricItem.MemoryUsage);

            double? cpuUsage = null;
            double? memoryUsage = null;
            if (cpuEnabled || memoryEnabled)
            {
                (cpuUsage, memoryUsage) = ReadSystemHardwareMetrics(cpuEnabled, memoryEnabled);
            }

            var gpuUsage = gpuEnabled ? ReadGameGpuUsage() : null;

            lock (_locker)
            {
                _cpuUsage = cpuEnabled ? cpuUsage : null;
                _gpuUsage = gpuEnabled ? gpuUsage : null;
                _memoryUsage = memoryEnabled ? memoryUsage : null;
            }
        }
        finally
        {
            Monitor.Exit(_hardwareLocker);
        }
    }

    private (double? CpuUsage, double? MemoryUsage) ReadSystemHardwareMetrics(bool cpuEnabled, bool memoryEnabled)
    {
        // LibreHardwareMonitor 仅保留系统 CPU/内存采样；GPU 由按游戏进程归因的 PDH 数据提供。
        if (!EnsureHardwareInitialized())
        {
            return (null, null);
        }

        try
        {
            double? cpuUsage = null;
            double? memoryUsage = null;
            foreach (var hardware in _computer!.Hardware)
            {
                hardware.Update();
                foreach (var subHardware in hardware.SubHardware)
                {
                    subHardware.Update();
                }

                if (cpuEnabled && hardware.HardwareType == HardwareType.Cpu)
                {
                    cpuUsage = TryGetLoad(hardware, "CPU Total") ?? TryGetFirstSensorValue(hardware, SensorType.Load);
                }
                else if (memoryEnabled && hardware.HardwareType == HardwareType.Memory)
                {
                    memoryUsage = TryGetLoad(hardware, "Memory") ?? TryGetFirstSensorValue(hardware, SensorType.Load);
                }
            }

            return (cpuUsage, memoryUsage);
        }
        catch (Exception ex)
        {
            LogHardwareWarning(ex);
            return (null, null);
        }
    }

    private double? ReadGameGpuUsage()
    {
        if (WinePlatformAddon.IsRunningOnWine || _gpuMetricsUnavailable)
        {
            return null;
        }

        var gameProcessId = TryGetGameProcessId();
        if (gameProcessId == null)
        {
            ReleaseGpuUsageProvider(resetAvailability: true);
            return null;
        }

        try
        {
            _gameGpuUsageProvider ??= new GameGpuUsageProvider();
            return _gameGpuUsageProvider.Sample(gameProcessId.Value);
        }
        catch (Exception ex)
        {
            ReleaseGpuUsageProvider(resetAvailability: false);
            _gpuMetricsUnavailable = true;
            LogGpuWarning(ex);
            return null;
        }
    }

    private static int? TryGetGameProcessId()
    {
        try
        {
            var context = TaskContext.Instance();
            if (!context.IsInitialized || context.SystemInfo.GameProcess.HasExited)
            {
                return null;
            }

            return context.SystemInfo.GameProcessId;
        }
        catch
        {
            return null;
        }
    }

    private bool EnsureHardwareInitialized()
    {
        // 硬件指标是可选能力，初始化失败只隐藏硬件项并记录一次警告，不影响 BetterGI 自身指标。
        if (_hardwareInitialized)
        {
            return _computer != null;
        }

        _hardwareInitialized = true;
        try
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = false,
                IsMemoryEnabled = true
            };
            _computer.Open();
            return true;
        }
        catch (Exception ex)
        {
            _computer?.Close();
            _computer = null;
            LogHardwareWarning(ex);
            return false;
        }
    }

    private void LogHardwareWarning(Exception ex)
    {
        if (_hardwareWarningLogged)
        {
            return;
        }

        _hardwareWarningLogged = true;
        _logger.LogWarning(ex, "遮罩 CPU/内存指标初始化或读取失败，将自动隐藏不可用指标");
    }

    private void LogGpuWarning(Exception ex)
    {
        if (_gpuWarningLogged)
        {
            return;
        }

        _gpuWarningLogged = true;
        _logger.LogWarning(ex, "遮罩原神显卡占用率初始化或读取失败，将自动隐藏该指标");
    }

    private void ReleaseGpuUsageProvider(bool resetAvailability)
    {
        _gameGpuUsageProvider?.Dispose();
        _gameGpuUsageProvider = null;
        if (resetAvailability)
        {
            _gpuMetricsUnavailable = false;
        }

        lock (_locker)
        {
            _gpuUsage = null;
        }
    }

    private static double? TryGetLoad(IHardware hardware, string preferredName)
    {
        return hardware.Sensors
            .Where(sensor => sensor.SensorType == SensorType.Load && sensor.Value != null)
            .OrderByDescending(sensor => sensor.Name.Contains(preferredName, StringComparison.OrdinalIgnoreCase))
            .Select(sensor => (double?)sensor.Value!.Value)
            .FirstOrDefault();
    }

    private static double? TryGetFirstSensorValue(IHardware hardware, SensorType sensorType)
    {
        return hardware.Sensors
            .Where(sensor => sensor.SensorType == sensorType && sensor.Value != null)
            .Select(sensor => (double?)sensor.Value!.Value)
            .FirstOrDefault();
    }

    private static double Smooth(double? currentValue, double newValue)
    {
        // 简单平滑可减少单帧截图或触发器尖峰导致的遮罩数值跳动。
        return currentValue == null ? newValue : currentValue.Value * 0.7 + newValue * 0.3;
    }

    private static double? GetBgiMemoryMb()
    {
        try
        {
            // PrivateMemorySize64 反映进程实际私有内存，比 WorkingSet 更接近真实占用。
            using var process = Process.GetCurrentProcess();
            return process.PrivateMemorySize64 / 1024.0 / 1024.0;
        }
        catch
        {
            return null;
        }
    }

    private static string FormatMemory(double mb)
    {
        return $"{mb:F0}MB";
    }

    private double? GetBgiCpuPercent()
    {
        try
        {
            var now = DateTime.UtcNow;
            using var process = Process.GetCurrentProcess();
            var currentCpuTime = process.TotalProcessorTime;

            if (_lastBgiCpuSampleTime == DateTime.MinValue)
            {
                _lastBgiCpuTime = currentCpuTime;
                _lastBgiCpuSampleTime = now;
                return null;
            }

            var cpuDelta = (currentCpuTime - _lastBgiCpuTime).TotalSeconds;
            var timeDelta = (now - _lastBgiCpuSampleTime).TotalSeconds;

            _lastBgiCpuTime = currentCpuTime;
            _lastBgiCpuSampleTime = now;

            if (timeDelta <= 0) return null;

            // TotalProcessorTime 是所有核心累计时间，除以核心数得实际占用率。
            var raw = cpuDelta / timeDelta / Environment.ProcessorCount * 100;
            _bgiCpuPercent = Smooth(_bgiCpuPercent, raw);
            return Math.Round(_bgiCpuPercent.Value, 1);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_maskWindowConfig != null)
        {
            _maskWindowConfig.PropertyChanged -= MaskWindowConfigOnPropertyChanged;
        }

        lock (_hardwareLocker)
        {
            _gameGpuUsageProvider?.Dispose();
            _gameGpuUsageProvider = null;
            _computer?.Close();
            _computer = null;
        }
    }
}

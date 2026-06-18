using System.Diagnostics;

namespace BetterGenshinImpact.Service.Model.OverlayMetric;

public class DispatcherTickMetrics
{
    private long _tickStart;
    private long _captureStart;

    public bool IsEnabled { get; private set; }

    public double ProcessingCostMs { get; private set; }

    public double CaptureCostMs { get; private set; }

    public double TriggerCostMs { get; private set; }

    public void Begin()
    {
        _tickStart = Stopwatch.GetTimestamp();
        _captureStart = _tickStart;
        IsEnabled = true;
    }

    public void EndCapture()
    {
        if (!IsEnabled)
        {
            return;
        }

        CaptureCostMs = Stopwatch.GetElapsedTime(_captureStart).TotalMilliseconds;
    }

    public void AddTriggerCost(long triggerStart)
    {
        if (!IsEnabled)
        {
            return;
        }

        TriggerCostMs += Stopwatch.GetElapsedTime(triggerStart).TotalMilliseconds;
    }

    public void EndProcessing()
    {
        if (!IsEnabled)
        {
            return;
        }

        ProcessingCostMs = Stopwatch.GetElapsedTime(_tickStart).TotalMilliseconds;
    }

    public void Publish(OverlayMetricsService? metricsService)
    {
        if (!IsEnabled)
        {
            return;
        }

        metricsService?.RecordDispatcherTick(
            ProcessingCostMs,
            CaptureCostMs,
            TriggerCostMs);
    }
}
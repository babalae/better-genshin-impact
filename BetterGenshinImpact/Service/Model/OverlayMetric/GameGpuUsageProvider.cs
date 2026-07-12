using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace BetterGenshinImpact.Service.Model.OverlayMetric;

internal sealed class GameGpuUsageProvider : IDisposable
{
    private readonly PdhGpuEngineCounterSource _counterSource = new();
    private readonly GameGpuUsageTracker _tracker = new();

    public double? Sample(int gameProcessId)
    {
        var counterValues = _counterSource.Sample();
        var samples = new List<GpuEngineUsageSample>(counterValues.Count);
        foreach (var counterValue in counterValues)
        {
            if (GpuEngineInstanceParser.TryCreateSample(counterValue, out var sample))
            {
                samples.Add(sample);
            }
        }

        return _tracker.Update(gameProcessId, samples);
    }

    public void Dispose()
    {
        _counterSource.Dispose();
    }
}

internal sealed class GameGpuUsageTracker
{
    internal const int MissingSampleLimit = 3;

    internal int? ProcessId { get; private set; }

    internal GpuAdapterKey? SelectedAdapter { get; private set; }

    internal int MissingSampleCount { get; private set; }

    public double? Update(int processId, IReadOnlyList<GpuEngineUsageSample> samples)
    {
        if (processId <= 0)
        {
            Reset();
            return null;
        }

        if (ProcessId != processId)
        {
            ProcessId = processId;
            SelectedAdapter = null;
            MissingSampleCount = 0;
        }

        if (SelectedAdapter == null)
        {
            SelectedAdapter = GameGpuUsageCalculator.SelectAdapter(samples, processId);
            if (SelectedAdapter == null)
            {
                return null;
            }
        }
        else if (samples.Any(sample => sample.ProcessId == processId && sample.Adapter == SelectedAdapter.Value))
        {
            MissingSampleCount = 0;
        }
        else
        {
            MissingSampleCount++;
            if (MissingSampleCount >= MissingSampleLimit)
            {
                SelectedAdapter = GameGpuUsageCalculator.SelectAdapter(samples, processId);
                MissingSampleCount = 0;
                if (SelectedAdapter == null)
                {
                    return null;
                }
            }
        }

        return GameGpuUsageCalculator.CalculateAdapterUsage(samples, SelectedAdapter.Value);
    }

    public void Reset()
    {
        ProcessId = null;
        SelectedAdapter = null;
        MissingSampleCount = 0;
    }
}

internal static class GameGpuUsageCalculator
{
    internal const double MinimumAdapterActivity = 0.5;
    private const double UsageTieTolerance = 0.0001;

    public static GpuAdapterKey? SelectAdapter(IReadOnlyList<GpuEngineUsageSample> samples, int processId)
    {
        var candidates = samples
            .Where(sample => sample.ProcessId == processId && IsValidUtilization(sample.Utilization))
            .GroupBy(sample => sample.Adapter)
            .Select(group =>
            {
                var peakUsage = group.Max(sample => sample.Utilization);
                var peakIsThreeDimensional = group.Any(sample =>
                    IsThreeDimensional(sample.EngineType)
                    && Math.Abs(sample.Utilization - peakUsage) <= UsageTieTolerance);
                return new AdapterCandidate(group.Key, peakUsage, peakIsThreeDimensional);
            })
            .Where(candidate => candidate.PeakUsage >= MinimumAdapterActivity)
            .OrderByDescending(candidate => candidate.PeakUsage)
            .ThenByDescending(candidate => candidate.PeakIsThreeDimensional)
            .ThenBy(candidate => candidate.Adapter.LuidHigh)
            .ThenBy(candidate => candidate.Adapter.LuidLow)
            .ThenBy(candidate => candidate.Adapter.PhysicalAdapterIndex)
            .ToList();

        return candidates.Count == 0 ? null : candidates[0].Adapter;
    }

    public static double? CalculateAdapterUsage(IReadOnlyList<GpuEngineUsageSample> samples, GpuAdapterKey adapter)
    {
        var engineUsages = samples
            .Where(sample => sample.Adapter == adapter && IsValidUtilization(sample.Utilization))
            .GroupBy(sample => sample.EngineId)
            .Select(group => Math.Clamp(group.Sum(sample => sample.Utilization), 0d, 100d))
            .ToList();

        return engineUsages.Count == 0 ? null : engineUsages.Max();
    }

    private static bool IsValidUtilization(double utilization)
    {
        return double.IsFinite(utilization) && utilization >= 0;
    }

    private static bool IsThreeDimensional(string engineType)
    {
        return string.Equals(engineType, "3D", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record AdapterCandidate(GpuAdapterKey Adapter, double PeakUsage, bool PeakIsThreeDimensional);
}

internal static partial class GpuEngineInstanceParser
{
    public static bool TryCreateSample(GpuEngineCounterValue counterValue, out GpuEngineUsageSample sample)
    {
        if (counterValue.Status is not (PdhCounterStatus.ValidData or PdhCounterStatus.NewData)
            || !double.IsFinite(counterValue.Value)
            || counterValue.Value < 0)
        {
            sample = default;
            return false;
        }

        return TryParse(counterValue.InstanceName, counterValue.Value, out sample);
    }

    public static bool TryParse(string instanceName, double utilization, out GpuEngineUsageSample sample)
    {
        sample = default;
        if (string.IsNullOrWhiteSpace(instanceName) || !double.IsFinite(utilization) || utilization < 0)
        {
            return false;
        }

        var match = GpuEngineInstanceRegex().Match(instanceName);
        if (!match.Success
            || !int.TryParse(match.Groups["pid"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var processId)
            || !TryParseHexUInt32(match.Groups["luidHigh"].Value, out var luidHigh)
            || !TryParseHexUInt32(match.Groups["luidLow"].Value, out var luidLow)
            || !int.TryParse(match.Groups["phys"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var physicalAdapterIndex)
            || !int.TryParse(match.Groups["engine"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var engineId))
        {
            return false;
        }

        sample = new GpuEngineUsageSample(
            processId,
            new GpuAdapterKey(luidHigh, luidLow, physicalAdapterIndex),
            engineId,
            match.Groups["engineType"].Value,
            utilization);
        return true;
    }

    private static bool TryParseHexUInt32(string value, out uint result)
    {
        result = 0;
        return value.Length > 2
               && value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
               && uint.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
    }

    [GeneratedRegex(
        @"^pid_(?<pid>\d+)_luid_(?<luidHigh>0x[0-9a-f]+)_(?<luidLow>0x[0-9a-f]+)_phys_(?<phys>\d+)_eng_(?<engine>\d+)_engtype_(?<engineType>.+?)(?:#\d+)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GpuEngineInstanceRegex();
}

internal readonly record struct GpuAdapterKey(uint LuidHigh, uint LuidLow, int PhysicalAdapterIndex);

internal readonly record struct GpuEngineUsageSample(
    int ProcessId,
    GpuAdapterKey Adapter,
    int EngineId,
    string EngineType,
    double Utilization);

internal readonly record struct GpuEngineCounterValue(string InstanceName, uint Status, double Value);

internal static class PdhCounterStatus
{
    public const uint ValidData = 0x00000000;
    public const uint NewData = 0x00000001;
}

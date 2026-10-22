using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Serilog;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

/// <summary>
/// YOLO 检测耗时聚合。逐次打点会刷屏，这里按模型聚合：
/// 首次调用输出一行，此后每 <see cref="ReportEvery"/> 次输出一行到日志栏。
/// 只统计检测（Detect）路径，不含分类模型与 OCR。
/// </summary>
internal static class YoloLatencyMonitor
{
    private const int ReportEvery = 50;
    private const int MaxSamples = 200;

    /// <summary>超过该耗时的调用计入"慢调用"计数，用于观察 DML 尾延迟。</summary>
    private const double SlowThresholdMs = 50;

    private static readonly object Gate = new();
    private static readonly Dictionary<string, State> States = new();

    private sealed class State
    {
        public readonly List<double> Samples = new(MaxSamples);
        public int Total;
        public int SlowCount;
        public int LastBoxes;
    }

    public static long Start() => Stopwatch.GetTimestamp();

    /// <summary>记录一次检测耗时；累计到阈值时输出汇总。</summary>
    /// <param name="model">模型名（BgiWorld / BgiFish / BgiMine …）</param>
    /// <param name="startTimestamp">调用 <see cref="Start"/> 时的时间戳</param>
    /// <param name="boxes">本次检出框数</param>
    public static void Report(string model, long startTimestamp, int boxes)
    {
        var ms = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        if (ms < 0) return;

        string? line;
        lock (Gate)
        {
            if (!States.TryGetValue(model, out var state))
            {
                States[model] = state = new State();
            }

            state.Total++;
            state.LastBoxes = boxes;
            if (ms >= SlowThresholdMs) state.SlowCount++;
            state.Samples.Add(ms);
            if (state.Samples.Count > MaxSamples) state.Samples.RemoveAt(0);

            line = state.Total == 1 || state.Total % ReportEvery == 0 ? Format(model, state) : null;
        }

        if (line != null) Log.Information("{YoloStat}", line);
    }

    private static string Format(string model, State state)
    {
        var sorted = state.Samples.OrderBy(x => x).ToArray();
        static double Percentile(double[] data, double p) =>
            data[Math.Clamp((int)Math.Ceiling(data.Length * p) - 1, 0, data.Length - 1)];

        return $"[YOLO] {model} {state.Total} 次 | 均 {state.Samples.Average():F1}ms " +
               $"p50 {Percentile(sorted, 0.5):F1} p90 {Percentile(sorted, 0.9):F1} 最大 {sorted[^1]:F1} " +
               $"| 慢于 {SlowThresholdMs:F0}ms {state.SlowCount} 次 | 最近 {state.LastBoxes} 框";
    }
}

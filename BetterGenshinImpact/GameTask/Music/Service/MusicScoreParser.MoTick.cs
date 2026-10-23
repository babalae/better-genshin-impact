using BetterGenshinImpact.GameTask.Music.Model;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;

namespace BetterGenshinImpact.GameTask.Music.Service;

/// <summary>
/// mo-Tick 谱面（<see cref="MusicScoreFormat.MoTick"/>）的解析实现。
/// 谱面来自 YQ_moTools 录制导出，tick 文本的语法是
/// 「和弦字母串 + 相对毫秒间隔 + 可选 [延音毫秒]」：
/// <code>
/// A1000F          // 0ms 按 A；1000ms 时按 F
/// A[800]1000DF    // 0ms 按 A 并按住到 800ms；1000ms 时按 D+F
/// </code>
/// 数字永远是「上一个和弦的按下 → 这个和弦的按下」的间隔，[ms] 不推进时间轴。
/// 空白与其它字符（. , | + - / 等）按规则静默忽略。
/// </summary>
public sealed partial class MusicScoreParser
{
    /// <summary>
    /// YQ_moTools 录制文件的 format 标识，用于在没有 type 字段时识别 mo-Tick 谱面。
    /// </summary>
    private const string MoTickFileFormat = "yqrec.recording";

    /// <summary>
    /// 格式分派中表示 mo-Tick 的内部标识。
    /// </summary>
    private const string MoTickTypeName = "motick";

    /// <summary>
    /// 单个间隔允许的最大毫秒数（24 小时），用于拦截明显错误的时间数字。
    /// </summary>
    private const long MaxGapMilliseconds = 24 * 60 * 60 * 1000;

    /// <summary>
    /// 把 mo-Tick 谱面展开成绝对时间轴。
    /// </summary>
    /// <param name="json">整份谱面 JSON，tick.unit 与 capture 决定时间基准</param>
    /// <returns>按时间排序的按键事件时间轴</returns>
    private static PerformanceTimeline ParseMoTickTimeline(JObject json)
    {
        var tickNode = json["tick"] ?? throw new FormatException("mo-Tick 谱面缺少 tick 字段");
        string text;
        double unitScale;
        if (tickNode is JObject tickObject)
        {
            text = GetString(tickObject, "text", string.Empty);
            unitScale = ResolveTickUnitScale(json, tickObject);
        }
        else
        {
            // v1 兼容写法：tick 直接是文本，按毫秒处理
            text = tickNode.ToString();
            unitScale = 1d;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormatException("mo-Tick 谱面的 tick.text 为空");
        }

        var events = new List<PerformanceEvent>();
        var end = TimeSpan.Zero;
        var onset = 0d;
        foreach (var note in ParseMoTickText(text))
        {
            // 间隔是「上一个和弦的按下 → 这个和弦的按下」，延音不参与推进
            onset += note.GapMilliseconds * unitScale;
            // 没写 [ms] 时视为按下即抬起，仍保留一个可被游戏识别的最小时长
            var duration = Math.Max(note.SustainMilliseconds * unitScale, LowestLatencyMilliseconds);
            AddNote(events, TimeSpan.FromMilliseconds(onset), TimeSpan.FromMilliseconds(duration), note.Keys);
            if (onset + duration > end.TotalMilliseconds)
            {
                end = TimeSpan.FromMilliseconds(onset + duration);
            }
        }

        if (events.Count == 0)
        {
            throw new FormatException("mo-Tick 谱面没有解析到任何音符");
        }

        return new PerformanceTimeline(ApplyRepeatedKeyGap(events), end);
    }

    /// <summary>
    /// 结构嗅探：YQ_moTools 录制文件不带 type 字段，
    /// 用 format 标记，或 score_format="tick" 且存在 tick 节点来识别。
    /// 与演奏无关的其它键值对不参与判断。
    /// </summary>
    /// <param name="json">整份谱面 JSON</param>
    /// <returns>是否为 mo-Tick 谱面</returns>
    private static bool IsMoTickJson(JObject json)
    {
        var fileFormat = GetString(json, "format", string.Empty).Trim();
        if (string.Equals(fileFormat, MoTickFileFormat, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var scoreFormat = GetString(json, "score_format", string.Empty).Trim();
        return string.Equals(scoreFormat, "tick", StringComparison.OrdinalIgnoreCase) && json["tick"] != null;
    }

    /// <summary>
    /// 解析 tick.unit：v3 起为毫秒；v2 旧文件的 "frame" 需要按采集时长换算成毫秒。
    /// 未知单位一律报错，避免把按帧/按拍写的谱面静默当成毫秒播放。
    /// </summary>
    /// <param name="json">整份谱面 JSON，用于读取 capture</param>
    /// <param name="tick">tick 节点</param>
    /// <returns>谱面数值乘以该系数即为毫秒</returns>
    private static double ResolveTickUnitScale(JObject json, JObject tick)
    {
        var unit = GetString(tick, "unit", "ms").Trim().ToLowerInvariant();
        if (unit is "ms" or "millisecond" or "milliseconds")
        {
            return 1d;
        }

        if (unit is not ("frame" or "frames"))
        {
            throw new FormatException($"不支持的 mo-Tick 时间单位：{unit}（仅支持 ms / frame）");
        }

        var capture = json["capture"] as JObject;
        var duration = capture == null ? 0 : GetDouble(capture, "duration_ms", 0);
        var frames = capture == null ? 0 : GetDouble(capture, "frames", 0);
        if (frames <= 1 || duration <= 0)
        {
            throw new FormatException(
                "mo-Tick 谱面声明 tick.unit=\"frame\"，但 capture.duration_ms / capture.frames 不可用，无法换算毫秒");
        }

        // 录制工具的时间轴：第 0 帧在 0ms，第 frames-1 帧在 duration_ms
        return duration / (frames - 1);
    }

    /// <summary>
    /// 把 tick 文本切成和弦序列。
    /// 规则：连续字母是一个和弦；数字累加为下一个和弦的间隔；
    /// 紧跟在和弦后的 [数字] 是该和弦的按住时长；空白保留延音可挂接状态，
    /// 其它字符静默丢弃并中断延音挂接。
    /// </summary>
    /// <param name="text">tick 文本</param>
    /// <returns>按文本顺序排列的和弦</returns>
    private static List<MoTickNote> ParseMoTickText(string text)
    {
        var notes = new List<MoTickNote>();
        var chord = new StringBuilder();
        long pendingGap = 0;
        var canSustain = false;
        var index = 0;

        void AddChord()
        {
            if (pendingGap > MaxGapMilliseconds)
            {
                throw new FormatException($"mo-Tick 谱面的间隔过大：{pendingGap}ms");
            }

            notes.Add(new MoTickNote(chord.ToString(), pendingGap, 0));
            chord.Clear();
            pendingGap = 0;
        }

        while (index < text.Length)
        {
            var current = text[index];

            // 空白只影响可读性，不产生时间，也不打断延音挂接
            if (char.IsWhiteSpace(current))
            {
                index++;
                if (chord.Length > 0)
                {
                    AddChord();
                    canSustain = true;
                }

                continue;
            }

            // 数字是下一个和弦的相对间隔
            if (char.IsAsciiDigit(current))
            {
                if (chord.Length > 0)
                {
                    AddChord();
                }

                canSustain = false;
                pendingGap = pendingGap * 10 + (current - '0');
                index++;
                continue;
            }

            // 连续字母是同一个和弦
            if (char.IsAsciiLetter(current))
            {
                chord.Append(char.ToUpperInvariant(current));
                index++;
                continue;
            }

            // 其它字符先收尾当前和弦
            if (chord.Length > 0)
            {
                AddChord();
                canSustain = true;
            }

            if (current == '[' && canSustain)
            {
                var bracketEnd = text.IndexOf(']', index + 1);
                if (bracketEnd < 0)
                {
                    // 未闭合：不当作延音，也不吞掉后面的内容
                    canSustain = false;
                    index++;
                    continue;
                }

                var content = text[(index + 1)..bracketEnd];
                if (TryReadDigits(content, out var sustain) && notes.Count > 0)
                {
                    if (sustain > MaxGapMilliseconds)
                    {
                        throw new FormatException($"mo-Tick 谱面的延音过大：{sustain}ms");
                    }

                    notes[^1] = notes[^1] with { SustainMilliseconds = sustain };
                }

                // 一个和弦最多一个延音，第二个方括号按杂字符处理
                canSustain = false;
                index = bracketEnd + 1;
                continue;
            }

            canSustain = false;
            index++;
        }

        if (chord.Length > 0)
        {
            AddChord();
        }

        return notes;
    }

    /// <summary>
    /// 读取方括号内的延音数值，只接受纯数字（允许首尾空白），其余一律视为无效。
    /// </summary>
    /// <param name="content">方括号内的文本</param>
    /// <param name="value">解析结果</param>
    /// <returns>是否为合法数值</returns>
    private static bool TryReadDigits(string content, out long value)
    {
        value = 0;
        var trimmed = content.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        foreach (var c in trimmed)
        {
            if (!char.IsAsciiDigit(c))
            {
                value = 0;
                return false;
            }

            value = value * 10 + (c - '0');
        }

        return true;
    }

    /// <summary>
    /// mo-Tick 文本中的一个和弦：按键、相对上一个和弦的间隔、按住时长。
    /// </summary>
    /// <param name="Keys">和弦内的按键字母（可能重复，展开时会去重）</param>
    /// <param name="GapMilliseconds">相对上一个和弦起点的间隔（毫秒或帧，按 tick.unit 换算）</param>
    /// <param name="SustainMilliseconds">按住时长（毫秒或帧），0 表示按下即抬起</param>
    private sealed record MoTickNote(string Keys, long GapMilliseconds, long SustainMilliseconds);
}

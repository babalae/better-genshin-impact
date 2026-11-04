using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.GameTask.Music.Model;

namespace BetterGenshinImpact.GameTask.Music.Service;

/// <summary>
/// 引导式跟弹里「一次按键」：同一时刻按下的所有键（和弦）合并成一次。
/// </summary>
public sealed record MusicGuidedPress
{
    /// <summary>它在原谱里的时间点（引导模式本身不限节奏，只用于让界面按原谱顺序排列）</summary>
    public TimeSpan Time { get; init; }

    /// <summary>这一次需要按下的键（和弦时多于一个）</summary>
    public char[] Keys { get; init; } = [];

    /// <summary>用于界面显示的键位文本，例如 "A" 或 "A + G"</summary>
    public string Display => string.Join(" + ", Keys);
}

/// <summary>
/// 把演奏时间轴切成「一次按键」的序列。
///
/// 依据 <see cref="MusicTimelineBuilder"/> 的输出约定：同一个时间点上的 KeyDown 就是一次和弦，
/// 而重触发补的 KeyUp 会提前 30ms，所以按时间聚在一起不会误合并。
/// </summary>
public static class MusicGuidedSegmentBuilder
{
    /// <summary>
    /// 允许出现在同一次按键里的时间误差。
    /// 取 25ms：MIDI 里同一个和弦的音符常常有 0～20ms 的先后（分轨导出、量化误差），
    /// 误差太小会把一个和弦拆成好几次按键，玩家就得一根一根手指慢慢按，跟弹体验很差；
    /// 而 25ms 远小于任何真实节奏间隔（120BPM 的十六分音符也有 125ms），不会把连续音误合并。
    /// </summary>
    private static readonly TimeSpan ChordTolerance = TimeSpan.FromMilliseconds(25);

    public static IReadOnlyList<MusicGuidedPress> Build(PerformanceTimeline timeline)
    {
        var presses = new List<MusicGuidedPress>();
        var currentKeys = new List<char>();
        var currentTime = TimeSpan.Zero;
        var hasCurrent = false;

        // 稳定排序（与 MusicTimelineBuilder.Normalize 一致：同时间的 KeyDown 在前，方便合并和弦）
        var events = timeline.Events
            .OrderBy(x => x.Time)
            .ThenBy(x => x.Type == PerformanceEventType.KeyDown ? 0 : 1);

        foreach (var item in events)
        {
            if (item.Type != PerformanceEventType.KeyDown)
            {
                continue;
            }

            if (!hasCurrent)
            {
                hasCurrent = true;
                currentTime = item.Time;
                currentKeys = [item.Key];
                continue;
            }

            if (item.Time - currentTime <= ChordTolerance)
            {
                if (!currentKeys.Contains(item.Key))
                {
                    currentKeys.Add(item.Key);
                }

                continue;
            }

            presses.Add(new MusicGuidedPress
            {
                Time = currentTime,
                Keys = currentKeys.ToArray()
            });
            currentTime = item.Time;
            currentKeys = [item.Key];
        }

        if (hasCurrent)
        {
            presses.Add(new MusicGuidedPress
            {
                Time = currentTime,
                Keys = currentKeys.ToArray()
            });
        }

        return presses;
    }
}

using BetterGenshinImpact.GameTask.Music.Model;
using BetterGenshinImpact.GameTask.Music.Service;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.MusicTests;

/// <summary>
/// mo-Tick 谱面（<see cref="MusicScoreFormat.MoTick"/>）解析行为测试。
/// 期望值取自 YQ_moTools 参考实现（yqrec.recording.parse_tick_text）对同一文本的计算结果。
/// 只依赖临时文件，不需要 Assets 子模块与游戏环境。
/// </summary>
public class MusicScoreParserMoTickTests
{
    /// <summary>
    /// 真实录制文件（v2，unit=frame）：应按 capture 换算成毫秒，和弦展开成同刻多键。
    /// </summary>
    [Fact]
    public async Task ParseAsync_FrameUnitRecording_ConvertsFramesToMilliseconds()
    {
        var score = await ParseAsync("""
            {
              "version": 2,
              "format": "yqrec.recording",
              "score_format": "tick",
              "name": "谱面_tick_20261020_174428",
              "created_at": "2026-10-20T17:44:34+08:00",
              "instrument": { "id": "fwzs", "name": "风物之诗", "key_count": 21 },
              "capture": { "backend": "gdi", "fps": 31.49, "frames": 1005, "duration_ms": 31882.2 },
              "tick": { "unit": "frame", "text": "D14G13Q13E9F2H2V1E12A14D13R11G2WV16M18S34C3WG12M12TS14J11D2QN13C14A22F2H2EX13RN13TA14Q9F2H2QB13S14WJ26QEZ13B15S18B16AD17G16QSB14E9F2H2V1E12A13D14R9G2WV14M13S27C1WG12M13TS13J11D3QN13C13A23F1H2EX14RN13TA13Q10F2H2B1Q13S17WJ34QZ17B13S13F14AD12D14F13G" },
              "extra": { "profile": "default", "delta": 40 }
            }
            """);

        Assert.True(score.IsValid, score.Error);
        Assert.Equal(MusicScoreFormat.MoTick, score.Format);
        Assert.Equal("mo-Tick", score.FormatName);
        Assert.Equal("谱面_tick_20261020_174428", score.Name);
        Assert.Equal("风物之诗", score.Instrument);

        // 83 个和弦、107 个按键（和弦内不重复计数）
        Assert.Equal(107, score.NoteCount);

        var downs = Downs(score);
        AssertTime(0, downs[0]);
        Assert.Equal('D', downs[0].Key);
        AssertTime(444.573, downs[1]);
        Assert.Equal('G', downs[1].Key);
        AssertTime(857.390, downs[2]);
        AssertTime(1270.207, downs[3]);

        // 第 13 个和弦 WV：两个键同刻按下
        var chord = downs.Where(x => Math.Abs(x.Time.TotalMilliseconds - 3366.049) < 1).ToList();
        Assert.Equal(2, chord.Count);
        Assert.Equal(['V', 'W'], chord.Select(x => x.Key).OrderBy(x => x).ToArray());

        // 末音 29659.337ms，按下即抬起时补最小可识别时长 30ms
        AssertTime(29659.337, downs[^1]);
        Assert.InRange(score.Duration.TotalMilliseconds - 29689.337, -1, 1);
    }

    /// <summary>
    /// v3/v4 的毫秒单位：延音只决定抬起时刻，不推进下一个和弦的起点。
    /// </summary>
    [Fact]
    public async Task ParseAsync_MillisecondUnit_SustainDoesNotDelayNextChord()
    {
        var score = await ParseAsync("""
            {
              "type": "mo_tick",
              "name": "延音示例",
              "tick": { "unit": "ms", "text": "A[800]1000DF" }
            }
            """);

        Assert.True(score.IsValid, score.Error);
        var downs = Downs(score);
        Assert.Equal(3, downs.Count);
        AssertTime(0, downs[0]);
        Assert.Equal('A', downs[0].Key);
        AssertTime(1000, downs[1]);
        AssertTime(1000, downs[2]);
        Assert.Equal(['D', 'F'], downs.Skip(1).Select(x => x.Key).OrderBy(x => x).ToArray());

        // A 按住到 800ms；D/F 没有延音，按下即抬起仍保留 30ms
        var ups = score.SourceTimeline.Events
            .Where(x => x.Type == PerformanceEventType.KeyUp)
            .OrderBy(x => x.Time)
            .ToList();
        AssertTime(800, ups[0]);
        Assert.Equal('A', ups[0].Key);
        AssertTime(1030, ups[1]);
        AssertTime(1030, ups[2]);
    }

    /// <summary>
    /// 语法边界：对齐 YQ_moTools 规则文档 §5 的实测解析表。
    /// </summary>
    [Theory]
    [MemberData(nameof(SyntaxCases))]
    public async Task ParseAsync_SyntaxEdgeCases_MatchReferenceImplementation(
        string text,
        double[] expectedDownTimes)
    {
        var score = await ParseAsync($$"""
            {
              "type": "motick",
              "tick": { "unit": "ms", "text": "{{text}}" }
            }
            """);

        Assert.True(score.IsValid, score.Error);
        var actual = Downs(score).Select(x => x.Time.TotalMilliseconds).ToArray();
        Assert.Equal(expectedDownTimes.Length, actual.Length);
        for (var i = 0; i < expectedDownTimes.Length; i++)
        {
            Assert.InRange(actual[i] - expectedDownTimes[i], -0.01, 0.01);
        }
    }

    /// <summary>
    /// 语法边界用例表：文本 → 期望的按下时刻（毫秒）。
    /// </summary>
    public static TheoryData<string, double[]> SyntaxCases => new()
    {
        // 基本形：数字是相对上一个和弦的间隔
        { "A1000F", [0, 1000] },
        // 首数字是前导静默，和弦内多键同刻
        { "1000AB200C", [1000, 1000, 1200] },
        // 大小写不敏感
        { "a100f", [0, 100] },
        // 和弦内重复键去重
        { "AA100F", [0, 100] },
        // 杂字符被忽略，800 变成间隔（. 不是延音）
        { "A.800F", [0, 800] },
        // 括号内非数字：延音记为 0，但整段被吃掉
        { "A[abc]F", [0, 0] },
        // 一个和弦只认第一个方括号，第二个 900 变成间隔
        { "A[800][900]F", [0, 900] },
        // 未闭合方括号：不当作延音，也不吞掉后面的内容
        { "A[800", [0] },
        // 空白保留延音挂接，方括号内允许首尾空白
        { "A [ 800 ]F", [0, 0] },
        // 竖线与空格只是可读性：AB 一个和弦、C 一个和弦、DE 一个和弦
        { "AB|C 200 DE", [0, 0, 0, 200, 200] }
    };

    /// <summary>
    /// 缺少 tick 节点应给出可读的解析错误，而不是让整个目录扫描中断。
    /// </summary>
    [Fact]
    public async Task ParseAsync_MissingTick_ReturnsInvalidScore()
    {
        var score = await ParseAsync("""
            { "version": 4, "format": "yqrec.recording", "score_format": "tick", "name": "坏谱面" }
            """);

        Assert.False(score.IsValid);
        Assert.Contains("tick", score.Error);
    }

    /// <summary>
    /// frame 单位缺少 capture 换算数据时应报错，而不是按毫秒静默播放错乱的时间轴。
    /// </summary>
    [Fact]
    public async Task ParseAsync_FrameUnitWithoutCapture_ReturnsInvalidScore()
    {
        var score = await ParseAsync("""
            { "type": "motick", "tick": { "unit": "frame", "text": "A1F" } }
            """);

        Assert.False(score.IsValid);
        Assert.Contains("frame", score.Error);
    }

    /// <summary>
    /// v1 兼容写法：tick 直接是字符串时按毫秒解析。
    /// </summary>
    [Fact]
    public async Task ParseAsync_TickAsPlainString_IsTreatedAsMilliseconds()
    {
        var score = await ParseAsync("""
            { "format": "yqrec.recording", "tick": "A100F" }
            """);

        Assert.True(score.IsValid, score.Error);
        Assert.Equal(MusicScoreFormat.MoTick, score.Format);
        Assert.Equal([0d, 100], Downs(score).Select(x => x.Time.TotalMilliseconds).ToArray());
    }

    /// <summary>
    /// 回归保护：没有 type 的原琴 JSON 仍然按 AutoYuanQin 解析，不受结构嗅探影响。
    /// </summary>
    [Fact]
    public async Task ParseAsync_YuanQinWithoutType_StillParsesAsYuanQin()
    {
        var score = await ParseAsync("""
            { "name": "原琴", "bpm": 120, "notes": "Q[4]E[4]" }
            """);

        Assert.True(score.IsValid, score.Error);
        Assert.Equal(MusicScoreFormat.YuanQin, score.Format);
        Assert.Equal(2, score.NoteCount);
    }

    /// <summary>
    /// 新增与演奏无关的键值对（任意层级、任意类型）不应影响解析结果。
    /// </summary>
    [Fact]
    public async Task ParseAsync_UnrelatedKeys_AreIgnored()
    {
        var score = await ParseAsync("""
            {
              "version": 4,
              "format": "yqrec.recording",
              "score_format": "tick",
              "name": "带扩展字段的谱面",
              "author": "YQ_moTools",
              "tags": ["风物之诗", "练习"],
              "notes": "这一层没有语义，不应被当作 AutoYuanQin 的 notes",
              "export": { "tool": "yqrec", "build": 123, "plugins": [{ "id": "x", "enabled": true }] },
              "ui": { "theme": "dark", "zoom": 1.5 },
              "instrument": { "id": "fwzs", "name": "风物之诗", "key_count": 21, "color": "#ffffff", "vendor": { "v": 2 } },
              "capture": { "backend": "gdi", "fps": 60, "frames": 10, "duration_ms": 150, "gpu": "n/a", "notes": "ignored" },
              "tick": {
                "unit": "ms",
                "text": "A[800]1000DF",
                "sustain": true,
                "manual_edits": [{ "at": 120, "by": "ray" }],
                "future_field": null
              },
              "checksum": "deadbeef"
            }
            """);

        Assert.True(score.IsValid, score.Error);
        Assert.Equal(MusicScoreFormat.MoTick, score.Format);
        Assert.Equal("mo-Tick", score.FormatName);
        Assert.Equal("带扩展字段的谱面", score.Name);
        Assert.Equal("风物之诗", score.Instrument);

        // 与不带扩展字段时的基准完全一致
        var downs = Downs(score);
        Assert.Equal([0d, 1000, 1000], downs.Select(x => x.Time.TotalMilliseconds).ToArray());
        var aUp = score.SourceTimeline.Events.Single(x => x.Key == 'A' && x.Type == PerformanceEventType.KeyUp);
        AssertTime(800, aUp);
    }

    /// <summary>
    /// 未来版本若给 mo-Tick 谱面补上 type 字段（哪怕是不认识的值），仍按结构标记识别，不判为不支持。
    /// </summary>
    [Theory]
    [InlineData("tick")]
    [InlineData("tick_v5")]
    [InlineData("yqrec.recording")]
    public async Task ParseAsync_UnknownTypeWithMoTickMarkers_StillParsesAsMoTick(string type)
    {
        var score = await ParseAsync($$"""
            {
              "type": "{{type}}",
              "format": "yqrec.recording",
              "score_format": "tick",
              "tick": { "unit": "ms", "text": "A100F" }
            }
            """);

        Assert.True(score.IsValid, score.Error);
        Assert.Equal(MusicScoreFormat.MoTick, score.Format);
        Assert.Equal([0d, 100], Downs(score).Select(x => x.Time.TotalMilliseconds).ToArray());
    }

    /// <summary>
    /// 未知时间单位必须报错：否则按帧/按拍写的谱面会被当成毫秒静默播放错乱的时间轴。
    /// </summary>
    [Fact]
    public async Task ParseAsync_UnknownTickUnit_ReturnsInvalidScore()
    {
        var score = await ParseAsync("""
            { "type": "motick", "tick": { "unit": "beat", "text": "A1F" } }
            """);

        Assert.False(score.IsValid);
        Assert.Contains("时间单位", score.Error);
    }

    /// <summary>
    /// rootFolder 为空时解析失败也只应返回无效谱面，不能抛出异常（否则调用方会直接崩溃）。
    /// </summary>
    [Fact]
    public async Task ParseAsync_InvalidSheetWithEmptyRootFolder_ReturnsInvalidScore()
    {
        var folder = Path.Combine(Path.GetTempPath(), "bgi-music-tests");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"mo-tick-broken-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, """{ "type": "motick" }""");
        try
        {
            var parser = new MusicScoreParser();
            var score = await parser.ParseAsync(path, string.Empty, CancellationToken.None);
            Assert.False(score.IsValid);
            Assert.Contains("tick", score.Error);
            Assert.False(string.IsNullOrWhiteSpace(score.RelativePath));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// 取出按时间排序的按下事件。
    /// </summary>
    /// <param name="score">解析结果</param>
    /// <returns>按下事件列表</returns>
    private static List<PerformanceEvent> Downs(PerformanceScore score)
    {
        return score.SourceTimeline.Events
            .Where(x => x.Type == PerformanceEventType.KeyDown)
            .OrderBy(x => x.Time)
            .ThenBy(x => x.Key)
            .ToList();
    }

    /// <summary>
    /// 断言按下时刻（毫秒，容差 0.01ms）。
    /// </summary>
    /// <param name="expectedMilliseconds">期望毫秒</param>
    /// <param name="evt">实际事件</param>
    private static void AssertTime(double expectedMilliseconds, PerformanceEvent evt)
    {
        Assert.InRange(evt.Time.TotalMilliseconds - expectedMilliseconds, -0.01, 0.01);
    }

    /// <summary>
    /// 把 JSON 文本写入临时文件后调用解析器。
    /// </summary>
    /// <param name="json">曲谱 JSON 文本</param>
    /// <returns>解析结果</returns>
    private static async Task<PerformanceScore> ParseAsync(string json)
    {
        var folder = Path.Combine(Path.GetTempPath(), "bgi-music-tests");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"mo-tick-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, json);
        try
        {
            var parser = new MusicScoreParser();
            Assert.True(parser.CanParse(path));
            return await parser.ParseAsync(path, folder, CancellationToken.None);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

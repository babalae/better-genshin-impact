using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.Pulonia.Services;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoPathingTests;

/// <summary>路线返回对象的统计隔离、未知状态和持久化回归，不启动游戏或截图器。</summary>
public sealed class PathingResultTests
{
    /// <summary>返回结果固定拾取明细，来源统计集合的后续变化不能污染已经完成的路线。</summary>
    [Fact]
    public void CollectedItems_AreCopiedAndReadOnly()
    {
        var items = new Dictionary<string, int> { ["薄荷"] = 2 };
        var first = new PathingResult(true, 3, reviveCount: 1, collectedItems: items);
        items["薄荷"] = 9;
        items["甜甜花"] = 1;
        var second = new PathingResult(false, 0, "识别失败", collectedItems: items);
        Assert.Equal(2, first.CollectedItems!["薄荷"]);
        Assert.Single(first.CollectedItems);
        Assert.Equal(9, second.CollectedItems!["薄荷"]);
        Assert.True(first.IsSuccess);
        Assert.Equal(3, first.SuccessFightCount);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, int>)first.CollectedItems).Add("其他", 1));
    }

    /// <summary>未统计不能被序列化为实际零收益或零复活，已统计为空时仍保留零值。</summary>
    [Fact]
    public void UnknownStatistics_DifferFromObservedZero()
    {
        var unknown = JObject.FromObject(new PathingResult(true));
        var observed = JObject.FromObject(new PathingResult(true, reviveCount: 0, collectedItems: new Dictionary<string, int>()));
        Assert.Equal(JTokenType.Null, unknown["revive_count"]!.Type);
        Assert.Equal(JTokenType.Null, unknown["collected_items"]!.Type);
        Assert.Equal(0, observed.Value<int>("revive_count"));
        Assert.Empty((JObject)observed["collected_items"]!);
    }

    /// <summary>结果对象通过项目 JSON 工具往返后，状态、失败原因及统计保持不变。</summary>
    [Fact]
    public void Result_RoundTripsWithStatisticsAndError()
    {
        var result = new PathingResult(false, 2, "重试耗尽", 1, new Dictionary<string, int> { ["落落莓"] = 4 });
        var restored = PuloniaTaskJson.Read<PathingResult>(PuloniaTaskJson.Write(result));
        Assert.False(restored.IsSuccess);
        Assert.Equal(2, restored.SuccessFightCount);
        Assert.Equal("重试耗尽", restored.ErrorMessage);
        Assert.Equal(1, restored.ReviveCount);
        Assert.Equal(4, restored.CollectedItems!["落落莓"]);
    }

    /// <summary>负统计值及成功与失败信息矛盾的对象不能进入执行历史。</summary>
    [Fact]
    public void InvalidResult_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PathingResult(true, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PathingResult(true, reviveCount: -1));
        Assert.Throws<ArgumentException>(() => new PathingResult(true, errorMessage: "失败"));
        Assert.Throws<ArgumentException>(() => new PathingResult(true, collectedItems: new Dictionary<string, int> { ["薄荷"] = -1 }));
    }
}

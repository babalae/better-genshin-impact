using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.GameTask.MapMask;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.MapMaskTests;

/// <summary>
/// 本地标记与 HoYoLAB 账号标记之间的转换测试
/// </summary>
public sealed class MapMarkSyncTests
{
    /// <summary>只解析 HoYoLab 数据源的合法点位键</summary>
    [Fact]
    public void ParseLocalPointIds_IgnoresOtherSourcesAndMalformed()
    {
        var ids = MapMarkSync.ParseLocalPointIds(
            ["HoYoLab:pt-pt:88512", "HoYoLab:en-us:323", "MihoyoMap:5", "HoYoLab:pt-pt:abc", "x"]);

        Assert.Equal(new long[] { 88512, 323 }, ids.ToArray());
    }

    /// <summary>待上传列表去掉账号已有与重复项，并排序</summary>
    [Fact]
    public void PendingUpload_RemovesAccountAndDuplicates()
    {
        var pending = MapMarkSync.PendingUpload([5, 3, 3, 9], new HashSet<long> { 9 });

        Assert.Equal(new long[] { 3, 5 }, pending);
    }

    /// <summary>生成与遮罩窗口一致的点位键</summary>
    [Fact]
    public void ToKey_Format()
    {
        Assert.Equal("HoYoLab:pt-pt:1", MapMarkSync.ToKey("pt-pt", 1));
    }
}

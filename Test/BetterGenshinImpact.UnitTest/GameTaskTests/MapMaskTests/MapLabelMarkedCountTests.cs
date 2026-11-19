using BetterGenshinImpact.Model.MaskMap;
using BetterGenshinImpact.ViewModel;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.MapMaskTests;

/// <summary>
/// 标点分类网格“已标记/总数”计数测试
/// </summary>
public sealed class MapLabelMarkedCountTests
{
    /// <summary>创建测试用分类项</summary>
    private static MapLabelItemVm Item(string id, int count, params string[] labelIds) =>
        new(new MaskMapPointLabel { LabelId = id, Name = id, PointCount = count, LabelIds = labelIds });

    /// <summary>创建测试用点位</summary>
    private static MaskMapPoint Point(string id, string labelId) => new() { Id = id, LabelId = labelId };

    /// <summary>按分类统计已标记点位；合并分类（多个 LabelIds）累加各子分类</summary>
    [Fact]
    public void Apply_CountsMarkedPointsPerItem()
    {
        var a = Item("1", 200);
        var merged = Item("2", 10, "21", "22");
        var none = Item("3", 5);
        MaskMapPoint[] points = [Point("p1", "1"), Point("p2", "1"), Point("p3", "21"), Point("p4", "22"), Point("p5", "3")];
        var marked = new HashSet<string> { "p1", "p3", "p4" };

        MapLabelMarkedCount.Apply([a, merged, none], points, p => marked.Contains(p.Id));

        Assert.Equal(1, a.MarkedCount);
        Assert.Equal(2, merged.MarkedCount);
        Assert.Equal(0, none.MarkedCount);
    }

    /// <summary>显示文字：没有标记时只显示总数，有标记时显示“已标记/总数”</summary>
    [Fact]
    public void PointCountText_ShowsMarkedOverTotalOnlyWhenMarked()
    {
        var item = Item("1", 200);
        Assert.Equal("200", item.PointCountText);

        item.MarkedCount = 1;
        Assert.Equal("1/200", item.PointCountText);
    }
}

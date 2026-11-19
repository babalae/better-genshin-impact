using BetterGenshinImpact.Model.MaskMap;
using BetterGenshinImpact.ViewModel;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.MapMaskTests;

/// <summary>
/// 标点分类网格的“已选中”高亮同步测试
/// </summary>
public sealed class MapLabelSelectionTests
{
    /// <summary>创建测试用分类项</summary>
    private static MapLabelItemVm Item(string id) => new(new MaskMapPointLabel { LabelId = id, Name = id });

    /// <summary>按 ID 匹配：已选列表中的实例与网格中的实例不同也能高亮，未选中的取消高亮</summary>
    [Fact]
    public void Apply_MarksItemsBySelectedId()
    {
        var a = Item("1");
        var b = Item("2");
        b.IsSelected = true;

        MapLabelSelection.Apply([a, b], [Item("1")]);

        Assert.True(a.IsSelected);
        Assert.False(b.IsSelected);
    }
}

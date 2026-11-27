using System.ComponentModel;
using BetterGenshinImpact.Service.AccountRecords.Model;
using BetterGenshinImpact.ViewModel.Controls;

namespace BetterGenshinImpact.UnitTest.ServiceTests;

/// <summary>需求按单项置顶、实际需求与列值排序，同族关系不改变位置。</summary>
public sealed class DemandFamilySortingTests
{
    /// <summary>置顶优先于有需求；有需求优先于普通库存，组内数量升降序逐项生效。</summary>
    [Theory]
    [InlineData(ListSortDirection.Ascending, "史莱姆原浆", "史莱姆凝液")]
    [InlineData(ListSortDirection.Descending, "史莱姆凝液", "史莱姆原浆")]
    public void SortsIndividualItems(ListSortDirection direction, string first, string second)
    {
        var rows = new[] { Row("史莱姆凝液", 99, true), Row("史莱姆原浆", 1, true), Row("普通", 0, false), Row("置顶", 5, false) };
        rows[3].Item.Prior = true;
        var comparer = new AccountRecordsDemandComparer().WithSort("Item.Quantity", direction, false);
        Array.Sort(rows, (a, b) => comparer.Compare(a, b));
        Assert.Equal(new[] { "置顶", first, second, "普通" }, rows.Select(row => row.Item.Name));
    }

    /// <summary>构造独立排序行。</summary>
    private static AccountRecordsDemandRow Row(string name, long quantity, bool demand) => new()
    {
        Item = new AccountRecordsInventoryRow { Name = name, ItemNameSortKey = name, Quantity = quantity },
        Demand = demand ? new MaterialDemand { TargetQuantity = 1 } : null
    };
}

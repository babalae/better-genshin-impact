using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace BetterGenshinImpact.ViewModel.Controls;

/// <summary>需求逐项排序：手动置顶、存在需求优先，再应用用户列排序。</summary>
public sealed class AccountRecordsDemandComparer : IComparer
{
    /// <summary>玩家选择的列顺序；为空时采用最近库存记录优先。</summary>
    public IReadOnlyList<SortDescription> Sorts { get; }

    /// <summary>保存排序条件的只读副本，避免视图外修改比较规则。</summary>
    public AccountRecordsDemandComparer(IEnumerable<SortDescription>? sorts = null)
        => Sorts = Array.AsReadOnly(sorts?.ToArray() ?? []);

    /// <summary>列点击替换排序，Shift 点击保留其他列及既有优先级。</summary>
    public AccountRecordsDemandComparer WithSort(string member, ListSortDirection direction, bool append)
    {
        var sorts = append ? Sorts.ToList() : [];
        var index = sorts.FindIndex(sort => sort.PropertyName == member);
        if (index >= 0) sorts[index] = new SortDescription(member, direction);
        else sorts.Add(new SortDescription(member, direction));
        return new AccountRecordsDemandComparer(sorts);
    }

    /// <summary>先比较单项置顶及需求，再比较用户列。</summary>
    public int Compare(object? x, object? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is not AccountRecordsDemandRow left) return -1;
        if (y is not AccountRecordsDemandRow right) return 1;
        var result = right.Prior.CompareTo(left.Prior);
        if (result != 0) return result;
        result = right.HasDemand.CompareTo(left.HasDemand);
        if (result != 0) return result;
        var a = left;
        var b = right;
        foreach (var sort in Sorts)
        {
            result = CompareValue(Value(a, sort.PropertyName), Value(b, sort.PropertyName));
            if (result != 0) return sort.Direction == ListSortDirection.Ascending ? result : -result;
        }
        if (Sorts.Count == 0)
        {
            result = Nullable.Compare(b.Item.RecordedAt, a.Item.RecordedAt);
            if (result != 0) return result;
            result = CompareValue(a.Item.ItemNameSortKey, b.Item.ItemNameSortKey);
            if (result != 0) return result;
        }
        return StringComparer.Ordinal.Compare(left.Item.Name, right.Item.Name);
    }

    /// <summary>只映射表格允许排序的列，不通过反射访问任意属性。</summary>
    private static object? Value(AccountRecordsDemandRow row, string member) => member switch
    {
        "Item.ItemNameSortKey" => row.Item.ItemNameSortKey,
        "Item.RecordedAt" => row.Item.RecordedAt,
        "Item.Quantity" => row.Item.Quantity,
        nameof(AccountRecordsDemandRow.CategorySortKey) => row.CategorySortKey,
        nameof(AccountRecordsDemandRow.TargetQuantity) => row.TargetQuantity,
        nameof(AccountRecordsDemandRow.CreatedAt) => row.CreatedAt,
        nameof(AccountRecordsDemandRow.Source) => row.Source,
        nameof(AccountRecordsDemandRow.State) => row.StateRank,
        _ => null
    };

    /// <summary>名称沿用库存中文排序文化，数量与时间保留原生比较语义。</summary>
    private static int CompareValue(object? left, object? right)
    {
        if (left is string a && right is string b)
            return CultureInfo.GetCultureInfo("zh-CN").CompareInfo.Compare(a, b);
        return Comparer.DefaultInvariant.Compare(left, right);
    }
}

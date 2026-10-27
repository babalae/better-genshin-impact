using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace BetterGenshinImpact.ViewModel.Controls;

/// <summary>库存行集合；整表加载时只通知一次，避免逐行触发排序、布局和分隔线计算。</summary>
public sealed class AccountRecordsInventoryRows : ObservableCollection<AccountRecordsInventoryRow>
{
    /// <summary>在 UI 线程批量替换内容，保留集合及其排序、筛选视图的身份。</summary>
    public void ReplaceAll(IEnumerable<AccountRecordsInventoryRow> rows)
    {
        // 先完成枚举，兼容来源就是当前集合，并避免枚举失败后留下半张表。
        var snapshot = rows.ToArray();
        CheckReentrancy();
        Items.Clear();
        foreach (var row in snapshot) Items.Add(row);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

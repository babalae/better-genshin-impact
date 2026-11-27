using System.ComponentModel;
using System.Collections.Specialized;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using BetterGenshinImpact.View.Behavior;
using BetterGenshinImpact.ViewModel.Controls;
using Microsoft.Xaml.Behaviors;

namespace BetterGenshinImpact.UnitTest.ServiceTests;

/// <summary>在独立 STA 线程验证表格排序，不启动主程序或显示窗口。</summary>
public sealed class InventoryPriorSortingTests
{
    /// <summary>升序、降序以及切换排序列均保持置顶优先，状态列保留组内有灯优先。</summary>
    [Fact]
    public Task Prior_StaysFirstAcrossColumnSorts()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var rows = new AccountRecordsInventoryRows
                {
                    new() { Name = "普通少", Quantity = 1 },
                    new() { Name = "置顶多", Quantity = 100, Prior = true },
                    new() { Name = "普通多", Quantity = 200, Event = new() { State = "Warning" } },
                    new() { Name = "置顶少", Quantity = 2, Prior = true, Event = new() { State = "Warning" } }
                };
                var view = new ListCollectionView(rows);
                var grid = new SortableGrid { ItemsSource = view, AutoGenerateColumns = false, CanUserAddRows = false, IsReadOnly = true };
                var quantity = new DataGridTextColumn { SortMemberPath = nameof(AccountRecordsInventoryRow.Quantity) };
                var state = new DataGridTextColumn { SortMemberPath = nameof(AccountRecordsInventoryRow.StateRank) };
                grid.Columns.Add(quantity);
                grid.Columns.Add(state);
                Interaction.GetBehaviors(grid).Add(new AccountRecordsRowSelectionBehavior());

                grid.Sort(quantity);
                Assert.Equal(new[] { "置顶少", "置顶多", "普通少", "普通多" }, view.Cast<AccountRecordsInventoryRow>().Select(row => row.Name));
                grid.Sort(quantity);
                Assert.Equal(ListSortDirection.Descending, quantity.SortDirection);
                Assert.Equal(new[] { "置顶多", "置顶少", "普通多", "普通少" }, view.Cast<AccountRecordsInventoryRow>().Select(row => row.Name));
                grid.Sort(state);
                Assert.Equal(new[] { "置顶少", "置顶多", "普通多", "普通少" }, view.Cast<AccountRecordsInventoryRow>().Select(row => row.Name));
                view.Filter = item => ((AccountRecordsInventoryRow)item).Quantity != 2;
                Assert.Equal(new[] { "置顶多", "普通多", "普通少" }, view.Cast<AccountRecordsInventoryRow>().Select(row => row.Name));

                // 批量重建只刷新一次；既有 DataGrid 排序和筛选仍作用于新行。
                var resets = 0;
                ((INotifyCollectionChanged)view).CollectionChanged += (_, _) => resets++;
                rows.ReplaceAll(Enumerable.Range(0, 2000).Select(index => new AccountRecordsInventoryRow
                {
                    Name = index.ToString(), Quantity = index, Prior = index == 1999
                }));
                Assert.Equal(1, resets);
                Assert.Equal(1999, view.Cast<AccountRecordsInventoryRow>().Count());
                Assert.Equal("1999", view.Cast<AccountRecordsInventoryRow>().First().Name);
                Assert.DoesNotContain(view.Cast<AccountRecordsInventoryRow>(), row => row.Quantity == 2);
                Assert.Equal(1999, grid.Items.Count);

                // 到期排序使用最早批次的绝对时间；反向仍保持无到期项在组内末尾。
                var expiry = new DataGridTextColumn { SortMemberPath = nameof(AccountRecordsInventoryRow.EarliestExpiration) };
                grid.Columns.Add(expiry);
                rows.ReplaceAll([
                    new() { Name = "无日期", Quantity = 1 },
                    new() { Name = "较晚", Quantity = 1, Expirations = [new() { Quantity = 1, ExpiresAt = "2026-11-03T00:00:00Z" }] },
                    new() { Name = "较早", Quantity = 1, Expirations = [new() { Quantity = 1, ExpiresAt = "2026-11-05T00:00:00Z" }, new() { Quantity = 1, ExpiresAt = "2026-11-01T08:00:00+08:00" }] },
                    new() { Name = "置顶", Quantity = 1, Prior = true }
                ]);
                grid.Sort(expiry);
                Assert.Equal(new[] { "置顶", "较早", "较晚", "无日期" }, view.Cast<AccountRecordsInventoryRow>().Select(row => row.Name));
                grid.Sort(expiry);
                Assert.Equal(new[] { "置顶", "较晚", "较早", "无日期" }, view.Cast<AccountRecordsInventoryRow>().Select(row => row.Name));

                // 原位编辑数量、时间和状态后，即便筛选触发视图刷新，也使用上次排序键。
                view.Filter = null;
                var first = new AccountRecordsInventoryRow { Name = "甲", Quantity = 1, RecordedAt = DateTimeOffset.UtcNow };
                var second = new AccountRecordsInventoryRow { Name = "乙", Quantity = 2 };
                rows.ReplaceAll([first, second]);
                quantity.SortDirection = null;
                grid.Sort(quantity);
                first.ApplySnapshot(new() { Name = "甲", Quantity = 10, RecordedAt = DateTimeOffset.UtcNow.AddDays(1), Event = new() { State = "Error" } });
                view.Refresh();
                Assert.Same(first, view.GetItemAt(0));
                Assert.Equal(10, first.Quantity);
                Assert.Equal(0, first.StateRank);
                quantity.SortDirection = null;
                grid.Sort(quantity);
                Assert.Same(second, view.GetItemAt(0));
                grid.Sort(state);
                Assert.Same(first, view.GetItemAt(0));
                first.ApplySnapshot(new() { Name = "甲", Quantity = 10 });
                second.ApplySnapshot(new() { Name = "乙", Quantity = 2, Event = new() { State = "Error" } });
                view.Refresh();
                Assert.Same(first, view.GetItemAt(0));
                state.SortDirection = null;
                grid.Sort(state);
                Assert.Same(second, view.GetItemAt(0));
                Interaction.GetBehaviors(grid).Clear();
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    /// <summary>向测试开放表头排序事件入口。</summary>
    private sealed class SortableGrid : DataGrid
    {
        /// <summary>模拟用户点击指定列的表头。</summary>
        public void Sort(DataGridColumn column) => OnSorting(new DataGridSortingEventArgs(column));
    }
}

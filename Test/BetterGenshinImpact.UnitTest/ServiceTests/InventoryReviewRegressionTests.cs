using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.Service.AccountRecords.Model;
using BetterGenshinImpact.ViewModel.Controls;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.ServiceTests;

/// <summary>库存审查发现问题的隔离回归，不访问用户数据或启动窗口。</summary>
public sealed class InventoryReviewRegressionTests
{
    /// <summary>历史顺序变化不影响当前快照，且来源观测统一进入历史。</summary>
    [Fact]
    public async Task History_KeepsSnapshotAndDisplaysOnlyCurrentStock()
    {
        var entries = Enumerable.Range(0, 51).Select(i => new InventoryBatchIndexEntry
            { Reference = new() { BatchId = "batch", RecordId = i.ToString() } }).ToList();
        var batch = new InventoryBatch { BatchId = "batch", Records = entries.Select(entry => new InventoryBatchRecord
            { RecordId = entry.Reference.RecordId, Name = "甜甜花", Kind = "Report", Outcome = "Rejected" }).ToList() };
        var loads = 0;
        var vm = new AccountRecordsItemDetailsViewModel(new()
        {
            Name = "甜甜花", Recent = [new() { Quantity = 10, Source = "a" }, new() { Quantity = 9, Source = "b" }]
        }, new Dictionary<string, AccountRecordSource>(), true,
            () => { loads++; return Task.FromResult(entries.ToArray()); }, refs => Task.FromResult(refs.Select(reference => batch.Records.FirstOrDefault(record => record.RecordId == reference.RecordId)).ToArray()));
        Assert.Single(vm.Records);
        await vm.LoadMoreCommand.ExecuteAsync(null);
        var retried = entries[50];
        entries.RemoveAt(50);
        entries.Insert(0, retried);
        await vm.LoadMoreCommand.ExecuteAsync(null);
        Assert.Equal(51, vm.History.Count);
        Assert.Equal(1, loads);
        Assert.False(vm.HasMore);
    }

    /// <summary>快照引用的记录被清理时明确说明，不宣称全部展示。</summary>
    [Fact]
    public async Task History_ReportsMissingEvidence()
    {
        var vm = new AccountRecordsItemDetailsViewModel(new() { Name = "甜甜花" }, new Dictionary<string, AccountRecordSource>(), true,
            () => Task.FromResult(new[] { new InventoryBatchIndexEntry { Reference = new() { BatchId = "gone", RecordId = "1" } } }),
            _ => Task.FromResult<InventoryBatchRecord?[]>([null]));
        await vm.LoadMoreCommand.ExecuteAsync(null);
        Assert.Contains("1 条记录无法读取", vm.HistoryStatus);
        Assert.False(vm.HasMore);
    }

    /// <summary>分类与背包页分别补缺，并保留非空或显式空的人工值。</summary>
    [Theory]
    [InlineData(null, "材料", false)]
    [InlineData("食物", "食物", true)]
    [InlineData("", "", true)]
    [InlineData(null, "", false, "")]
    public async Task Import_MergesPageIndependently(string? localPage, string expected, bool warning, string incomingPage = "材料")
    {
        var root = Path.Combine(Path.GetTempPath(), "InventoryReviewRegression", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var csv = Path.Combine(root, "items.csv");
            File.WriteAllText(csv, "item_name,material_type,page\n甜甜花,植物,\n");
            var store = new InventoryStore(new InventoryCatalog(csv), Path.Combine(root, "records"));
            const string uid = "100000001";
            await store.GetSettingsAsync();
            if (localPage != null)
                File.WriteAllText(Path.Combine(root, "records", "item-overrides.json"),
                    new JObject { ["甜甜花"] = new JObject { ["Category"] = "植物", ["Page"] = localPage } }.ToString());
            var package = JObject.Parse("""
                {"format":"BetterGI.AccountRecords","version":1,
                 "sources":{"scan":{"reference":"scan","displayName":"scan"}},
                 "itemDefinitions":{"甜甜花":{"category":"植物","page":"材料"}},
                 "accounts":{"100000001":{"inventory":{"甜甜花":[{"quantity":10,"recordedAt":"placeholder","source":"scan"}]}}}}
                """);
            package["accounts"]![uid]!["inventory"]!["甜甜花"]![0]!["recordedAt"] = "2020-01-01T00:00:00Z";
            package["itemDefinitions"]!["甜甜花"]!["page"] = incomingPage;
            var result = await store.ImportInventoryAsync(package, uid, true);
            Assert.Equal(expected, (await store.ReadAsync(uid)).Pages["甜甜花"]);
            Assert.Equal(warning, result.Warnings.ContainsKey("甜甜花"));
            Assert.Single(result.Accepted);
            // 同样的补充重复导入，不重复改写字典或追加备注。
            var path = Path.Combine(root, "records", "item-overrides.json");
            var before = File.ReadAllText(path);
            await store.ImportInventoryAsync(package, uid, true);
            Assert.Equal(before, File.ReadAllText(path));
            if (incomingPage == "")
            {
                // 显式未分类在 CSV 后续补齐背包页并重启后仍然生效。
                Assert.Equal("", (string?)JObject.Parse(before)["甜甜花"]!["Page"]);
                File.WriteAllText(csv, "item_name,material_type,page\n甜甜花,植物,材料\n");
                var reopened = new InventoryStore(new InventoryCatalog(csv), Path.Combine(root, "records"));
                Assert.Equal("", (await reopened.ReadAsync(uid)).Pages["甜甜花"]);
            }
        }
        finally { Directory.Delete(root, true); }
    }

    /// <summary>存在性查询在路径不可写时也不尝试创建目录或接续旧缓存。</summary>
    [Fact]
    public void IconExists_DoesNotWriteOrMigrate()
    {
        var root = Path.Combine(Path.GetTempPath(), "InventoryReviewRegression", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "icons"), "blocked");
            var cache = new InventoryIconCache(root);
            Assert.False(cache.Exists("摩拉"));
            Assert.Null(cache.Load("摩拉"));
            Assert.Equal("blocked", File.ReadAllText(Path.Combine(root, "icons")));
        }
        finally { Directory.Delete(root, true); }
    }
}

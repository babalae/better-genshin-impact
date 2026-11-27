using System.Security.Cryptography;
using System.Text;
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.Service.AccountRecords.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit.Abstractions;

namespace BetterGenshinImpact.UnitTest.ServiceTests;

/// <summary>PR 4001 四项主要意见的隔离复现；断言期望行为，不修改真实账号或业务实现。</summary>
public sealed class AccountRecordsPr4001ReproductionTests : IDisposable
{
    /// <summary>每个用例独享的文件目录。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BetterGI.Pr4001", Guid.NewGuid().ToString("N"));
    /// <summary>虚构账号。</summary>
    private const string Uid = "100000001";
    /// <summary>统一测试来源。</summary>
    private readonly AccountRecordSource _source = new() { Reference = "pr4001-test", DisplayName = "隔离测试" };
    /// <summary>过去的合法业务时间。</summary>
    private readonly DateTimeOffset _time = DateTimeOffset.UtcNow.AddHours(-1);
    /// <summary>输出故障前后的持久化证据。</summary>
    private readonly ITestOutputHelper _output;
    /// <summary>不依赖资源包的测试服务。</summary>
    private readonly InventoryStore _store;
    /// <summary>账号目录。</summary>
    private string AccountDirectory => Path.Combine(_root, "records", Uid);
    /// <summary>当前库存文件。</summary>
    private string InventoryPath => Path.Combine(AccountDirectory, "inventory.json");

    /// <summary>生成足够覆盖容量边界的静态字典。</summary>
    public AccountRecordsPr4001ReproductionTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_root);
        var csv = Path.Combine(_root, "items.csv");
        File.WriteAllText(csv, "item_name,material_type,page\n摩拉,货币,材料\n" +
            string.Join("\n", Enumerable.Range(0, 5001).Select(i => $"材料{i},材料,材料")), new UTF8Encoding(false));
        _store = new(new InventoryCatalog(csv), Path.Combine(_root, "records"));
    }

    /// <summary>仅清理本用例创建的目录。</summary>
    public void Dispose() => Directory.Delete(_root, true);

    /// <summary>构造真实上报接口的输入。</summary>
    private InventoryReport Report(string name, long quantity = 10, int minute = 0) => new()
    { Name = name, Quantity = quantity, RecordedAt = _time.AddMinutes(minute).ToString("O") };

    /// <summary>仅把保存日期移至昨天，模拟跨日；不改变业务数据。</summary>
    private string SetPreviousRecordDay()
    {
        var document = JsonConvert.DeserializeObject<InventoryDocument>(File.ReadAllText(InventoryPath))!;
        document.ModifiedAt = DateTimeOffset.UtcNow.AddDays(-1);
        File.WriteAllText(InventoryPath, JsonConvert.SerializeObject(document), new UTF8Encoding(false));
        return InventoryStore.RecordDay(document.ModifiedAt.Value).ToString("yyyy-MM-dd");
    }

    /// <summary>缺失记录标识的索引不能遮蔽完好的批次文件。</summary>
    [Fact]
    public async Task InvalidCachedIdentity_RebuildsFromBatchFiles()
    {
        await _store.ReportAsync(Uid, _source, [Report("摩拉")], batchId: "valid");
        File.WriteAllText(Path.Combine(AccountDirectory, "batch-index.json"), "[{\"Name\":\"摩拉\"}]");
        var entry = Assert.Single(await _store.GetItemHistorySnapshotAsync(Uid, "摩拉"));
        Assert.Equal("valid", entry.Reference.BatchId);
        Assert.NotNull(Assert.Single(await _store.GetItemHistoryPageAsync(Uid, [entry.Reference])));
    }

    /// <summary>单个结构损坏批次显示为不可读，同页其他记录仍返回。</summary>
    [Fact]
    public async Task InvalidBatch_DoesNotBlockOtherHistoryRows()
    {
        await _store.ReportAsync(Uid, _source, [Report("摩拉")], batchId: "good");
        await _store.ReportAsync(Uid, _source, [Report("摩拉", 15, 1)], batchId: "bad");
        var entries = await _store.GetItemHistorySnapshotAsync(Uid, "摩拉");
        var file = Path.Combine(AccountDirectory, "batches", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("bad"))) + ".json");
        var batch = JObject.Parse(File.ReadAllText(file));
        batch["BatchId"] = "wrong";
        File.WriteAllText(file, batch.ToString());
        var page = await _store.GetItemHistoryPageAsync(Uid, entries.Select(item => item.Reference).ToArray());
        Assert.Equal(2, page.Length);
        Assert.Single(page.Where(item => item == null));
        Assert.Equal("Accepted", Assert.Single(page.OfType<InventoryBatchRecord>()).Outcome);
    }

    /// <summary>快照自带的来源不受当前来源字典损坏影响。</summary>
    [Fact]
    public async Task Snapshot_UsesItsOwnSources()
    {
        await _store.ReportAsync(Uid, _source, [Report("摩拉")]);
        var day = SetPreviousRecordDay();
        await _store.ReportAsync(Uid, _source, [Report("摩拉", 15, 1)]);
        File.WriteAllText(Path.Combine(_root, "records", "sources.json"), "{broken");
        var snapshot = await _store.ReadSnapshotAsync(Uid, day);
        Assert.Equal(10, snapshot.Inventory.Items["摩拉"][0].Quantity);
        Assert.Equal(_source.DisplayName, snapshot.Sources[_source.Reference].DisplayName);
    }

    /// <summary>首次导入显式空页面必须覆盖 CSV，省略、null 和冲突非空值仍保留现有页。</summary>
    [Theory]
    [InlineData("missing", "贵重道具")]
    [InlineData("null", "贵重道具")]
    [InlineData("", "")]
    [InlineData("食物", "贵重道具")]
    public async Task ImportedPage_RespectsExplicitEmpty(string page, string expected)
    {
        await _store.ReportAsync(Uid, _source, [Report("摩拉")]);
        var package = await _store.ExportInventoryAsync(Uid);
        var definition = new JObject { ["category"] = "货币" };
        if (page != "missing") definition["page"] = page == "null" ? JValue.CreateNull() : new JValue(page);
        package["itemDefinitions"]!["摩拉"] = definition;
        await _store.ImportInventoryAsync(package, Uid, true);
        Assert.Equal(expected, (await _store.GetItemClassificationsAsync()).Pages["摩拉"]);
        if (page == "")
        {
            var exported = await _store.ExportInventoryAsync(Uid);
            var restored = new InventoryStore(new InventoryCatalog(Path.Combine(_root, "items.csv")), Path.Combine(_root, "page-restored"));
            await restored.ImportInventoryAsync(exported, Uid, true);
            Assert.Equal("", (await restored.GetItemClassificationsAsync()).Pages["摩拉"]);
        }
    }

    /// <summary>删除快照库存时清除本项及同族估算，但保留需求和无关物品的估算。</summary>
    [Fact]
    public async Task DeleteStock_RemovesDependentSnapshotEstimatesOnly()
    {
        await _store.DefineAsync("史莱姆凝液", "材料");
        await _store.DefineAsync("史莱姆清", "材料");
        await _store.ReportAsync(Uid, _source, [Report("史莱姆凝液", 12), Report("摩拉")]);
        await _store.SetDemandAsync(Uid, "史莱姆凝液", 1, null);
        await _store.SetDemandAsync(Uid, "史莱姆清", 3, null);
        await _store.SetDemandAsync(Uid, "摩拉", 20, null);
        var day = SetPreviousRecordDay();
        await _store.ReportAsync(Uid, _source, [Report("摩拉", 15, 1)]);
        await _store.DeleteItemAsync(Uid, "史莱姆凝液");
        var package = await InventoryStore.ReadExchangeAsync(Path.Combine(AccountDirectory, "snapshots", day + ".json"));
        var body = package["accounts"]![Uid]!;
        Assert.Null(body["demandStates"]!["史莱姆凝液"]);
        Assert.Null(body["demandStates"]!["史莱姆清"]);
        Assert.NotNull(body["demandStates"]!["摩拉"]);
        Assert.NotNull(body["demands"]!["items"]!["史莱姆凝液"]);
        Assert.NotNull(body["demands"]!["items"]!["史莱姆清"]);
    }

    /// <summary>失败后原样重试成功的历史，不得因主文件损坏静默退回拒绝状态。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptedRetry_HistoryMustNotSilentlyRestoreRejectedBackup(bool corruptMain)
    {
        const string batchId = "retry-after-definition";
        var delta = new InventoryDeltaReport { Name = "新材料", RecordId = "one", Delta = 2, RecordedAt = _time.ToString("O") };
        Assert.Equal("Rejected", Assert.Single((await _store.ReportDeltaAsync(Uid, _source, [delta], batchId)).Records).Outcome);
        await _store.DefineAsync("新材料", "材料");
        Assert.Equal("Accepted", Assert.Single((await _store.ReportDeltaAsync(Uid, _source, [delta], batchId)).Records).Outcome);
        var path = Path.Combine(AccountDirectory, "batches", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(batchId))) + ".json");
        Assert.Equal("Accepted", (string?)JObject.Parse(File.ReadAllText(path))["Records"]![0]!["Outcome"]);
        Assert.Equal("Rejected", (string?)JObject.Parse(File.ReadAllText(path + ".bak"))["Records"]![0]!["Outcome"]);
        if (corruptMain) File.WriteAllText(path, "{broken", new UTF8Encoding(false));
        if (corruptMain)
        {
            // 无法从旧备份还原最新结果时应明确失败，而不是伪造 Accepted 或静默退回 Rejected。
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => _store.GetBatchAsync(Uid, batchId));
            Assert.Contains("历史不完整", error.Message);
            Assert.Equal("{broken", File.ReadAllText(path));
            Assert.Equal(1, (await _store.ReportDeltaAsync(Uid, _source, [delta], batchId)).DuplicateCount);
            return;
        }
        var history = await _store.GetBatchAsync(Uid, batchId);
        var replay = await _store.ReportDeltaAsync(Uid, _source, [delta], batchId);
        Assert.Equal(1, replay.DuplicateCount);
        var after = await _store.GetBatchAsync(Uid, batchId);
        _output.WriteLine($"corruptMain={corruptMain}; firstHistory={history!.Records[0].Outcome}; replay=Duplicate; finalHistory={after!.Records[0].Outcome}");
        Assert.Equal("Accepted", after.Records[0].Outcome);
    }

    /// <summary>当前格式需求文件结构损坏时，自动快照不应阻断有效库存上报。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CrossDayReport_ShouldSurviveInvalidDemandDocument(bool corruptDemands)
    {
        await _store.ReportAsync(Uid, _source, [Report("摩拉")]);
        await _store.SetDemandAsync(Uid, "摩拉", 20, null);
        var day = SetPreviousRecordDay();
        var path = Path.Combine(AccountDirectory, "demands.json");
        var demand = JObject.Parse(File.ReadAllText(path));
        // 保持当前版本，只破坏必需结构，避免将旧格式兼容混入复现。
        if (corruptDemands) { demand["Items"] = JValue.CreateNull(); File.WriteAllText(path, demand.ToString()); }
        var error = await Record.ExceptionAsync(() => _store.ReportAsync(Uid, _source, [Report("摩拉", 15, 1)]));
        var persisted = JsonConvert.DeserializeObject<InventoryDocument>(File.ReadAllText(InventoryPath))!;
        _output.WriteLine($"corruptDemands={corruptDemands}; persistedQuantity={persisted.Items["摩拉"][0].Quantity}; error={error?.Message}");
        Assert.Null(error);
        Assert.Equal(15, persisted.Items["摩拉"][0].Quantity);
        Assert.True(File.Exists(Path.Combine(AccountDirectory, "snapshots", day + ".json")));
        if (corruptDemands)
        {
            var snapshot = await InventoryStore.ReadExchangeAsync(Path.Combine(AccountDirectory, "snapshots", day + ".json"));
            Assert.Null(snapshot["accounts"]![Uid]!["demands"]);
            Assert.NotEmpty((await _store.ReadSnapshotAsync(Uid, day)).BackupWarnings);
            Assert.True(JToken.DeepEquals(demand, JObject.Parse(File.ReadAllText(path))));
            await Assert.ThrowsAsync<InvalidDataException>(() => _store.ExportInventoryAsync(Uid));
        }
    }

    /// <summary>上报支持保存的容量，导出、快照查看及重新导入也应支持。</summary>
    [Theory]
    [InlineData(5000)]
    [InlineData(5001)]
    public async Task StoredInventory_ExportSnapshotAndImportMustRoundTrip(int count)
    {
        foreach (var chunk in Enumerable.Range(0, count).Chunk(500))
        {
            var report = await _store.ReportAsync(Uid, _source, chunk.Select(i => Report($"材料{i}")));
            Assert.Equal(chunk.Length, report.Accepted.Count);
        }
        var package = await _store.ExportInventoryAsync(Uid);
        Assert.Equal(count, ((JObject)package["accounts"]![Uid]!["inventory"]!).Count);
        var exportPath = Path.Combine(_root, "export.json");
        File.WriteAllText(exportPath, package.ToString(), new UTF8Encoding(false));
        var day = SetPreviousRecordDay();
        await _store.ReportAsync(Uid, _source, [Report("材料0", 11, 1)]);
        Assert.True(File.Exists(Path.Combine(AccountDirectory, "snapshots", day + ".json")));
        // 三个入口分别调用，避免第一个失败掩盖其他入口的实际结果。
        var readError = await Record.ExceptionAsync(() => InventoryStore.ReadExchangeAsync(exportPath));
        var snapshotError = await Record.ExceptionAsync(() => _store.ReadSnapshotAsync(Uid, day));
        var restored = new InventoryStore(new InventoryCatalog(Path.Combine(_root, "items.csv")), Path.Combine(_root, "restored"));
        var importError = await Record.ExceptionAsync(() => restored.ImportInventoryAsync(package, Uid, true));
        _output.WriteLine($"count={count}; exportBytes={new FileInfo(exportPath).Length}; read={readError?.Message}; snapshot={snapshotError?.Message}; import={importError?.Message}");
        Assert.Null(readError);
        Assert.Null(snapshotError);
        Assert.Null(importError);
        Assert.Equal(count, (await restored.ReadAsync(Uid)).Inventory.Items.Count);
    }

    /// <summary>已提交待发布记录遇到持续索引占用，不应阻断无关旧历史查询。</summary>
    [Theory]
    [InlineData("batch", false)]
    [InlineData("index", false)]
    [InlineData("page", false)]
    [InlineData("batch", true)]
    [InlineData("index", true)]
    [InlineData("page", true)]
    public async Task HistoryQuery_ShouldReadCommittedDataWhileIndexIsLocked(string query, bool exclusive)
    {
        await _store.ReportAsync(Uid, _source, [Report("摩拉")], batchId: "old");
        var references = (await _store.GetItemHistorySnapshotAsync(Uid, "摩拉")).Select(item => item.Reference).ToArray();
        var indexPath = Path.Combine(AccountDirectory, "batch-index.json");
        Exception? queryError;
        using (var locked = new FileStream(indexPath, FileMode.Open, FileAccess.Read, exclusive ? FileShare.None : FileShare.Read))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => _store.ReportAsync(Uid, _source, [Report("摩拉", 15, 1)], batchId: "pending"));
            var committed = JsonConvert.DeserializeObject<InventoryDocument>(File.ReadAllText(InventoryPath))!;
            Assert.Equal(15, committed.Items["摩拉"][0].Quantity);
            Assert.NotEmpty(committed.PendingBatches);
            queryError = await Record.ExceptionAsync(async () =>
            {
                if (query == "batch") Assert.NotNull(await _store.GetBatchAsync(Uid, "old"));
                else if (query == "index") Assert.NotEmpty(await _store.GetItemHistorySnapshotAsync(Uid, "摩拉"));
                else Assert.NotNull(Assert.Single(await _store.GetItemHistoryPageAsync(Uid, references)));
                Assert.Equal("Accepted", Assert.Single((await _store.GetBatchAsync(Uid, "pending"))!.Records).Outcome);
                Assert.Equal(2, (await _store.GetItemHistorySnapshotAsync(Uid, "摩拉")).Length);
            });
        }
        // 解除文件占用后能读回且不丢已提交数量，证明触发点是查询强制发布。
        Assert.NotNull(await _store.GetBatchAsync(Uid, "old"));
        Assert.Equal(15, (await _store.ReadAsync(Uid)).Inventory.Items["摩拉"][0].Quantity);
        _output.WriteLine($"query={query}; lockedError={queryError?.Message}; unlockedQuery=success; committedQuantity=15");
        Assert.Null(queryError);
    }
}

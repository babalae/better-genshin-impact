using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Script.Dependence;
using BetterGenshinImpact.Service.AccountRecords;
using Microsoft.ClearScript.V8;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.ScriptTests;

/// <summary>隔离存储与真实 V8 验证库存桥接，不启动主程序或游戏。</summary>
[Collection("ScriptExecution")]
public sealed class ScriptInventoryTests : IDisposable
{
    /// <summary>测试独占目录。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BetterGI.ScriptInventory", Guid.NewGuid().ToString("N"));
    /// <summary>测试 UID。</summary>
    private const string Uid = "100000001";
    /// <summary>独立库存存储。</summary>
    private readonly InventoryStore _store;
    /// <summary>绑定的节点执行。</summary>
    private readonly ScriptInventoryContext _context = new("test/script", "测试脚本", Uid, "run", "/group/#1/js");
    /// <summary>库存宿主。</summary>
    private readonly ScriptInventory _host;
    /// <summary>避免业务时间处于未来。</summary>
    private readonly DateTimeOffset _time = DateTimeOffset.UtcNow.AddHours(-1);

    /// <summary>家族查询无需读取 UID，返回按低到高的独立 JSON；未知物品不猜关系。</summary>
    [Fact]
    public void MaterialFamilyQueryDoesNotReadAccount()
    {
        var host = new ScriptInventory(_store, _context, () => throw new Exception("不应读取 UID"), default);
        var family = JObject.Parse(host.GetMaterialFamily(" 涤净青金断片 "));
        Assert.Equal("涤净青金碎屑", (string?)family["members"]![0]);
        Assert.Equal("涤净青金断片", (string?)family["members"]![1]);
        Assert.False((bool)family["allowsCraftingBonus"]!);
        Assert.Equal(3, (int)family["inputCount"]!);
        Assert.Equal("null", host.GetMaterialFamily("摩拉"));
        Assert.Equal("null", host.GetMaterialFamily("不存在的材料"));
        Assert.Throws<ArgumentException>(() => host.GetMaterialFamily(" "));
    }

    /// <summary>真实 JS 查询返回独立数组，拒绝错误参数且不识别 UID。</summary>
    [Fact]
    public async Task NativeMaterialFamilyReturnsDetachedObject()
    {
        await ScriptExecution.ExecuteAsync(ct =>
        {
            var engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableTaskPromiseConversion);
            ScriptInventory.Register(engine, new ScriptInventory(_store,
                new ScriptInventoryContext("test/families", "家族测试"),
                () => throw new Exception("不应识别 UID"), ct));
            return engine;
        }, engine => engine.Evaluate("""
            (async () => {
                const family = await inventory.getMaterialFamily('涤净青金断片');
                if (!Array.isArray(family.members) || family.members[0] !== '涤净青金碎屑')
                    throw new Error('invalid family');
                family.members[0] = '修改副本';
                if ((await inventory.getMaterialFamily('涤净青金断片')).members[0] !== '涤净青金碎屑')
                    throw new Error('catalog mutated');
                if (await inventory.getMaterialFamily('摩拉') !== null) throw new Error('unknown family');
                for (const value of [null, [], {}, 1, ' ']) {
                    let rejected = false;
                    try { await inventory.getMaterialFamily(value); } catch (error) { rejected = error instanceof TypeError; }
                    if (!rejected) throw new Error('invalid name accepted');
                }
            })()
            """), default);
    }

    /// <summary>纯图标不读取 UID、不产生库存记录，失败逐项返回且混合数量仍可接受。</summary>
    [Fact]
    public async Task IconOnlyReportsDoNotRequireUidOrChangeQuantities()
    {
        var cache = new InventoryIconCache(Path.Combine(_root, "cache"));
        using var image = new OpenCvSharp.Mat(12, 12, OpenCvSharp.MatType.CV_8UC3, OpenCvSharp.Scalar.White);
        File.WriteAllBytes(Path.Combine(_root, "icon.png"), image.ToBytes(".png"));
        var host = new ScriptInventory(_store, new ScriptInventoryContext("test/icons", "图标包"),
            () => throw new Exception("不应读取 UID"), default, _root, cache);
        var result = JObject.Parse(await host.Report("""[{"name":"摩拉","iconPath":"icon.png"},{"name":"原石","iconPath":"missing.png"}]"""));
        Assert.Equal("摩拉", (string?)Assert.Single((JArray)result["accepted"]!));
        Assert.NotNull(result["rejected"]!["原石"]);
        Assert.Empty((JArray)result["records"]!);
        Assert.Empty((await _store.ReadAsync(Uid)).Inventory.Items);
        await Assert.ThrowsAsync<ArgumentException>(() => host.Report("""[{"name":"摩拉"}]"""));
        await Assert.ThrowsAsync<ArgumentException>(() => host.Report("""[{"name":" ","iconPath":"icon.png"}]"""));
        await _store.UpdateSettingsAsync(settings => settings.UseImageCache = false);
        result = JObject.Parse(await host.Report("""[{"name":"摩拉","iconPath":"icon.png"}]"""));
        Assert.Empty((JArray)result["accepted"]!);
        Assert.NotNull(result["rejected"]!["摩拉"]);
    }

    /// <summary>建立最小 CSV 和库存服务。</summary>
    public ScriptInventoryTests()
    {
        Directory.CreateDirectory(_root);
        var csv = Path.Combine(_root, "items.csv");
        File.WriteAllText(csv, "item_name,material_type,page\n摩拉,货币\n须臾树脂,消耗品\n");
        _store = new InventoryStore(new InventoryCatalog(csv), Path.Combine(_root, "records"));
        _host = new ScriptInventory(_store, _context, () => throw new Exception("已核验账号不应再次识别"), default);
    }

    /// <summary>仅删除测试自己的数据。</summary>
    public void Dispose() => Directory.Delete(_root, true);

    /// <summary>缺图查询限定当前库存，筛选取交集；关闭显示不把已有缓存误判为缺图。</summary>
    [Fact]
    public async Task MissingIcons_FilterNamesCategoriesAndPages()
    {
        var cache = new InventoryIconCache(Path.Combine(_root, "cache"));
        var host = new ScriptInventory(_store, _context, () => Uid, default, _root, cache);
        var records = JArray.Parse(Records(10));
        var resin = (JObject)records[0].DeepClone();
        resin["name"] = "须臾树脂";
        resin["quantity"] = 0;
        records.Add(resin);
        await host.Report(records.ToString());
        Assert.Equal(2, JArray.Parse(await host.GetMissingIconNames()).Count);
        var category = (await _store.ReadAsync(Uid)).Definitions["摩拉"];
        var filters = new JObject { ["names"] = new JArray("摩拉", "不存在"), ["categories"] = category };
        Assert.Equal("摩拉", (string?)Assert.Single(JArray.Parse(await host.GetMissingIconNames(filters.ToString()))));
        Assert.Empty(JArray.Parse(await host.GetMissingIconNames("""{"names":[]}""")));
        Assert.Empty(JArray.Parse(await host.GetMissingIconNames("""{"pages":"不存在"}""")));
        using var image = new OpenCvSharp.Mat(12, 12, OpenCvSharp.MatType.CV_8UC3, OpenCvSharp.Scalar.White);
        cache.Store("摩拉", image.ToBytes(".png"));
        await _store.UpdateSettingsAsync(settings => settings.UseImageCache = false);
        Assert.Equal("须臾树脂", (string?)Assert.Single(JArray.Parse(await host.GetMissingIconNames())));
        await Assert.ThrowsAsync<ArgumentException>(() => host.GetMissingIconNames("""{"names":1}"""));
        await Assert.ThrowsAsync<ArgumentException>(() => host.GetMissingIconNames("""{"unknown":[]}"""));
    }

    /// <summary>联合上报库存写入失败时，不更新已有需求。</summary>
    [Fact]
    public async Task CombinedReport_InventoryWriteFailureDoesNotUpdateDemand()
    {
        await _host.Report(Records(1));
        await _host.Report("""[{"name":"摩拉","targetQuantity":5}]""");
        var path = Path.Combine(_root, "records", Uid, "inventory.json");
        using (var occupied = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var input = JArray.Parse(Records(10, minute: 1));
            input[0]["targetQuantity"] = 20;
            await Assert.ThrowsAsync<IOException>(() => _host.Report(input.ToString()));
        }
        Assert.Equal(5, (await _store.ReadDemandsAsync(Uid)).Items["摩拉"].TargetQuantity);
    }

    /// <summary>需求保存失败不撤回已经完成的库存提交，明确验证先库存后需求。</summary>
    [Fact]
    public async Task CombinedReport_DemandWriteFailurePreservesNewInventory()
    {
        await _host.Report("""[{"name":"摩拉","targetQuantity":5}]""");
        var path = Path.Combine(_root, "records", Uid, "demands.json");
        using (var occupied = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var input = JArray.Parse(Records(10));
            input[0]["targetQuantity"] = 20;
            await Assert.ThrowsAsync<IOException>(() => _host.Report(input.ToString()));
        }
        Assert.Equal(10, (long)JObject.Parse(await _host.GetQuantities("\"摩拉\""))["摩拉"]!);
        Assert.Equal(5, (await _store.ReadDemandsAsync(Uid)).Items["摩拉"].TargetQuantity);
    }

    /// <summary>需求附件可独立缓存，图片失败只产生提醒，原有缓存不被删除。</summary>
    [Fact]
    public async Task Report_IconAttachmentIsOptionalAndFailureDoesNotRejectDemand()
    {
        var cache = new InventoryIconCache(Path.Combine(_root, "cache"));
        var host = new ScriptInventory(_store, _context, () => Uid, default, _root, cache);
        using var image = new OpenCvSharp.Mat(12, 12, OpenCvSharp.MatType.CV_8UC3, OpenCvSharp.Scalar.White);
        image.SaveImage(Path.Combine(_root, "icon.png"));
        var response = JObject.Parse(await host.Report("""[{"name":"摩拉","targetQuantity":10,"iconPath":"icon.png"}]"""));
        Assert.True(cache.Exists("摩拉"));
        Assert.Contains("摩拉", response["demandsAccepted"]!.Values<string>());
        File.WriteAllText(Path.Combine(_root, "bad.png"), "not an image");
        foreach (var path in new[] { "../outside.png", "missing.png", "bad.png" })
        {
            response = JObject.Parse(await host.Report(new JArray(new JObject
                { ["name"] = "摩拉", ["targetQuantity"] = 20, ["iconPath"] = path }).ToString()));
            Assert.Contains("摩拉", response["demandsAccepted"]!.Values<string>());
            Assert.NotNull(response["warnings"]!["摩拉"]);
            Assert.True(cache.Exists("摩拉"));
        }
        response = JObject.Parse(await host.Report("""[{"name":"摩拉","targetQuantity":0}]"""));
        Assert.Empty((JObject)response["warnings"]!);
        Assert.True(cache.Exists("摩拉"));
    }

    /// <summary>关闭缓存时仍接受库存，且不读取附件。</summary>
    [Fact]
    public async Task Report_DisabledIconCacheLeavesQuantityAccepted()
    {
        await _store.UpdateSettingsAsync(settings => settings.UseImageCache = false);
        var cache = new InventoryIconCache(Path.Combine(_root, "cache"));
        var host = new ScriptInventory(_store, _context, () => Uid, default, _root, cache);
        var records = JArray.Parse(Records(10));
        records[0]["iconPath"] = "missing.png";
        var response = JObject.Parse(await host.Report(records.ToString()));
        Assert.Contains("摩拉", response["accepted"]!.Values<string>());
        Assert.Empty((JObject)response["warnings"]!);
        Assert.False(cache.Exists("摩拉"));
    }

    /// <summary>同名库存或需求的成功不能让另一条被拒绝的记录覆盖已有图标。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Report_RejectedRecordCannotBorrowAnotherRecordsIconAcceptance(bool acceptedDemand)
    {
        var cache = new InventoryIconCache(Path.Combine(_root, "cache"));
        using var original = new OpenCvSharp.Mat(12, 12, OpenCvSharp.MatType.CV_8UC3, OpenCvSharp.Scalar.White);
        cache.Store("摩拉", original.ToBytes(".png"));
        var cachePath = Assert.Single(Directory.GetFiles(Path.Combine(_root, "cache", "icons")));
        var expected = File.ReadAllBytes(cachePath);
        using var rejected = new OpenCvSharp.Mat(12, 12, OpenCvSharp.MatType.CV_8UC3, OpenCvSharp.Scalar.Black);
        File.WriteAllBytes(Path.Combine(_root, "rejected.png"), rejected.ToBytes(".png"));
        var input = acceptedDemand ? JArray.Parse("""[{"name":"摩拉","targetQuantity":20}]""") : JArray.Parse(Records(10));
        var rejectedRecord = (JObject)JArray.Parse(Records(-1, minute: 1))[0];
        rejectedRecord["iconPath"] = "rejected.png";
        input.Add(rejectedRecord);
        var host = new ScriptInventory(_store, _context, () => Uid, default, _root, cache);

        var response = JObject.Parse(await host.Report(input.ToString()));

        Assert.NotNull(response["rejected"]!["摩拉"]);
        Assert.Equal(expected, File.ReadAllBytes(cachePath));
        Assert.Equal(1, cache.Revision);
    }

    /// <summary>数量和采集时间重复的有效记录仍能补齐图标，不要求伪造新的库存观测。</summary>
    [Fact]
    public async Task Report_DuplicateObservationCanFillMissingIcon()
    {
        var cache = new InventoryIconCache(Path.Combine(_root, "cache"));
        var host = new ScriptInventory(_store, _context, () => Uid, default, _root, cache);
        await host.Report(Records(10));
        using var image = new OpenCvSharp.Mat(12, 12, OpenCvSharp.MatType.CV_8UC3, OpenCvSharp.Scalar.White);
        File.WriteAllBytes(Path.Combine(_root, "icon.png"), image.ToBytes(".png"));
        var input = JArray.Parse(Records(10));
        input[0]["iconPath"] = "icon.png";

        var response = JObject.Parse(await host.Report(input.ToString()));

        Assert.Equal(1, (int)response["duplicateCount"]!);
        Assert.True(cache.Exists("摩拉"));
        Assert.Equal(10, (await _store.ReadAsync(Uid)).Inventory.Items["摩拉"][0].Quantity);
    }

    /// <summary>构造 JS 风格库存输入。</summary>
    private string Records(long quantity, string? type = null, int minute = 0)
    {
        var item = new JObject { ["name"] = "摩拉", ["quantity"] = quantity,
            ["recordedAt"] = _time.AddMinutes(minute).ToString("O") };
        if (type != null) item["type"] = type;
        return new JArray(item).ToString();
    }

    /// <summary>重复调用共用批次，增量不改变确认数，后续实测可以核实。</summary>
    [Fact]
    public async Task RepeatedCalls_ShareBatchAndKeepPendingDeltaOutOfQuery()
    {
        var first = JObject.Parse(await _host.Report(Records(10)));
        var second = JObject.Parse(await _host.Report(Records(5, "delta", 1)));
        Assert.Equal((string?)first["batchId"], (string?)second["batchId"]);
        Assert.Equal("1", (string?)first["records"]![0]!["recordId"]);
        Assert.Equal("2", (string?)second["records"]![0]!["recordId"]);
        Assert.Equal(10, (long)JObject.Parse(await _host.GetQuantities("\"摩拉\""))["摩拉"]!);
        await _host.Report(Records(15, minute: 2));
        var data = (await _store.ReadAsync(Uid)).Inventory;
        Assert.Equal(15, data.Items["摩拉"][0].Quantity);
        Assert.True(Assert.Single(data.Events.Where(e => e.Kind == "Delta")).VerifiedCorrect);
        Assert.Equal(3, (await _store.GetBatchAsync(Uid, _context.BatchId))!.OriginalCount);
    }

    /// <summary>同数组实测、增量、核实保持原顺序，而不是先汇总实测。</summary>
    [Fact]
    public async Task MixedArray_PreservesOrder()
    {
        var input = new JArray(JArray.Parse(Records(10))[0], JArray.Parse(Records(5, "delta", 1))[0],
            JArray.Parse(Records(15, minute: 2))[0]);
        await _host.Report(input.ToString());
        var data = (await _store.ReadAsync(Uid)).Inventory;
        Assert.True(Assert.Single(data.Events.Where(e => e.Kind == "Delta")).VerifiedCorrect);
    }

    /// <summary>结构错误预检整次调用；业务错误则返回逐条结果。</summary>
    [Fact]
    public async Task InvalidStructure_DoesNotPartiallyWrite_BusinessFailureIsRecorded()
    {
        var input = JArray.Parse(Records(10));
        input.Add(new JObject { ["name"] = "摩拉", ["quantity"] = 1, ["type"] = "dleta" });
        await Assert.ThrowsAsync<ArgumentException>(() => _host.Report(input.ToString()));
        Assert.False(await _store.HasUidDataAsync(Uid));
        input = JArray.Parse(Records(10));
        input.Add(JArray.Parse(Records(-1, minute: 1))[0]);
        var result = JObject.Parse(await _host.Report(input.ToString()));
        Assert.Equal(new[] { "Accepted", "Rejected" }, result["records"]!.Select(r => (string?)r["outcome"]));
    }

    /// <summary>数量零和未知不同，到期信息复用现有模型且允许部分未知。</summary>
    [Fact]
    public async Task QueryAndExpirations_KeepExistingSemantics()
    {
        await _host.Report(Records(0));
        var record = new JObject { ["name"] = "须臾树脂", ["quantity"] = 2,
            ["recordedAt"] = _time.ToString("O"), ["expirations"] = new JArray(new JObject
            { ["quantity"] = 1, ["expiresAt"] = DateTimeOffset.UtcNow.AddDays(2).ToString("O") }) };
        await _host.Report(new JArray(record).ToString());
        var quantities = JObject.Parse(await _host.GetQuantities("[\"摩拉\",\"须臾树脂\",\"未知物品\"]"));
        Assert.Equal(0, (long)quantities["摩拉"]!);
        Assert.Equal(2, (long)quantities["须臾树脂"]!);
        Assert.Equal(JTokenType.Null, quantities["未知物品"]!.Type);
        Assert.Single((await _store.ReadAsync(Uid)).Inventory.Items["须臾树脂"][0].Expirations!);
    }

    /// <summary>并发提交不复用记录编号。</summary>
    [Fact]
    public async Task ConcurrentCalls_AllocateDistinctRecordIds()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(i => _host.Report(Records(i, minute: i))));
        Assert.Equal(5, results.Select(json => (string?)JObject.Parse(json)["records"]![0]!["recordId"]).Distinct().Count());
    }

    /// <summary>轮次、节点和失败重试区分批次，直接运行也独立分配。</summary>
    [Fact]
    public void ExecutionIdentity_DistinguishesNodesIterationsAndRetries()
    {
        Assert.Equal(_context.BatchId, new ScriptInventoryContext("test/script", "测试", Uid, "run", "/group/#1/js").BatchId);
        Assert.NotEqual(_context.BatchId, new ScriptInventoryContext("test/script", "测试", Uid, "run", "/group/#2/js").BatchId);
        Assert.NotEqual(_context.BatchId, new ScriptInventoryContext("test/script", "测试", Uid, "run", "/group/#1/other").BatchId);
        Assert.NotEqual(_context.BatchId, new ScriptInventoryContext("test/script", "测试", Uid, "run", "/group/#1/js", 2).BatchId);
        Assert.NotEqual(new ScriptInventoryContext("x", "x").BatchId, new ScriptInventoryContext("x", "x").BatchId);
    }

    /// <summary>无账号不写入，取消后不接收新调用。</summary>
    [Fact]
    public async Task MissingUidAndCancellation_DoNotWrite()
    {
        var unknown = new ScriptInventory(_store, new ScriptInventoryContext("x", "x"), () => null, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => unknown.Report(Records(1)));
        using var ct = new CancellationTokenSource();
        ct.Cancel();
        var stopped = new ScriptInventory(_store, _context, () => Uid, ct.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stopped.Report(Records(1)));
        Assert.False(await _store.HasUidDataAsync(Uid));
    }

    /// <summary>前后扫描分批，回到同名分组继续追加；默认批次与所有命名分组独立。</summary>
    [Fact]
    public async Task NamedGroups_SeparateBatchesAndAllowInterleavedAppend()
    {
        var before = JObject.Parse(await _host.Report(Records(10), "{\"batch\":\"before\"}"));
        var after = JObject.Parse(await _host.Report(Records(20, minute: 1), "{\"batch\":\"after\"}"));
        var appended = JObject.Parse(await _host.Report(Records(5, "delta", 2), "{\"batch\":\"before\"}"));
        var normal = JObject.Parse(await _host.Report(Records(25, minute: 3)));
        Assert.Equal((string?)before["batchId"], (string?)appended["batchId"]);
        Assert.NotEqual((string?)before["batchId"], (string?)after["batchId"]);
        Assert.Equal(_context.BatchId, (string?)normal["batchId"]);
        Assert.NotEqual((string?)normal["batchId"], (string?)before["batchId"]);
        Assert.NotEqual((string?)normal["batchId"], (string?)after["batchId"]);
        Assert.Equal(2, (await _store.GetBatchAsync(Uid, (string)before["batchId"]!))!.OriginalCount);
        Assert.Equal(1, (await _store.GetBatchAsync(Uid, (string)after["batchId"]!))!.OriginalCount);
        Assert.NotEqual((string?)before["records"]![0]!["recordId"], (string?)appended["records"]![0]!["recordId"]);
        Assert.Equal(_context.GetBatchId("before"), new ScriptInventoryContext("test/script", "测试", Uid,
            "run", "/group/#1/js").GetBatchId("before"));
        Assert.NotEqual(_context.GetBatchId("before"), new ScriptInventoryContext("test/script", "测试", Uid,
            "run", "/group/#2/js").GetBatchId("before"));
        Assert.NotEqual(_context.GetBatchId("before"), new ScriptInventoryContext("test/script", "测试", Uid,
            "run", "/group/#1/js", 2).GetBatchId("before"));
    }

    /// <summary>错误分组参数不能悄悄写入默认批次。</summary>
    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"batch\":null}")]
    [InlineData("{\"batch\":1}")]
    [InlineData("{\"batch\":\"\"}")]
    [InlineData("{\"batch\":\"   \"}")]
    [InlineData("{\"batc\":\"before\"}")]
    public async Task InvalidGroupOptions_DoNotWrite(string options)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _host.Report(Records(10), options));
        Assert.False(await _store.HasUidDataAsync(Uid));
    }

    /// <summary>真实 JS 数组和 await 桥接返回原生对象及数组，且不要求 JSON 字符串参数。</summary>
    [Fact]
    public async Task NativeJavaScriptArrays_WorkThroughBothPublicEntrypoints()
    {
        string? result = null;
        await ScriptExecution.ExecuteAsync(ct =>
        {
            var engine = new V8ScriptEngine(V8ScriptEngineFlags.UseCaseInsensitiveMemberBinding | V8ScriptEngineFlags.EnableTaskPromiseConversion);
            ScriptInventory.Register(engine, new ScriptInventory(_store, _context, () => Uid, ct));
            engine.AddHostObject("capture", new Action<string>(value => result = value));
            return engine;
        }, engine => engine.Evaluate("""
            (async () => {
                const report = await inventory.report([{ name: '摩拉', quantity: 0, recordedAt: new Date(Date.now() - 60000).toISOString() }]);
                if (!Array.isArray(report.records) || report.records[0].outcome !== 'Accepted') throw new Error('invalid result');
                const one = await inventory.getQuantities('摩拉');
                const many = await inventory.getQuantities(['摩拉', '未知']);
                const first = await inventory.report([{ name: '摩拉', quantity: 1, recordedAt: new Date(Date.now() - 40000).toISOString() }], { batch: '扫描前' });
                const second = await inventory.report([{ name: '摩拉', quantity: 2, recordedAt: new Date(Date.now() - 30000).toISOString() }], { batch: '扫描后' });
                const third = await inventory.report([{ name: '摩拉', quantity: 3, recordedAt: new Date(Date.now() - 20000).toISOString() }], { batch: '扫描前' });
                if (first.batchId === second.batchId || first.batchId !== third.batchId || report.batchId === first.batchId)
                    throw new Error('invalid group identity');
                let rejected = false;
                try { await inventory.report([], 'before'); } catch { rejected = true; }
                if (!rejected) throw new Error('invalid options accepted');
                const dates = await inventory.getReportDates(['摩拉', '未知']);
                if (typeof dates['摩拉'] !== 'string' || !dates['摩拉'].endsWith('Z') || dates['未知'] !== null)
                    throw new Error('invalid report dates');
                const issues = await inventory.getIssueNames();
                if (!Array.isArray(issues.pending) || !Array.isArray(issues.errors)) throw new Error('invalid issue lists');
                capture(JSON.stringify({ one, many, keys: Object.keys(inventory) }));
            })()
            """), default);
        var data = JObject.Parse(result!);
        Assert.Equal(0, (long)data["one"]!["摩拉"]!);
        Assert.Equal(JTokenType.Null, data["many"]!["未知"]!.Type);
        Assert.Equal(new[] { "report", "getQuantities", "identifyIcon", "getMaterialFamily", "getDemandSettings", "getReportDates", "getIssueNames", "getMissingIconNames" }, data["keys"]!.Values<string>());
    }

    /// <summary>时间与确认库存对应，增量、目标、失败上报均不冒充新的实测。</summary>
    [Fact]
    public async Task ReportDates_TrackConfirmedStockOnly()
    {
        Assert.Null(JsonConvert.DeserializeObject<Dictionary<string, string?>>(await _host.GetReportDates("\"摩拉\""))!["摩拉"]);
        await _host.Report(Records(10));
        var first = await _host.GetReportDates("\"摩拉\"");
        Assert.Equal(_time.UtcDateTime.ToString("O"), JsonConvert.DeserializeObject<Dictionary<string, string?>>(first)!["摩拉"]);
        await _host.Report(Records(2, "delta", 1));
        await _host.Report("[{\"name\":\"摩拉\",\"targetQuantity\":20}]");
        await _host.Report(Records(-1, minute: 2));
        Assert.Equal(first, await _host.GetReportDates("\"摩拉\""));
        await _host.Report(Records(12, minute: 3));
        var latest = JsonConvert.DeserializeObject<Dictionary<string, string?>>(await _host.GetReportDates("[\" 摩拉 \",\"摩拉\",\"未知\"]"))!;
        Assert.Equal(2, latest.Count);
        Assert.Equal(_time.AddMinutes(3).UtcDateTime.ToString("O"), latest["摩拉"]);
        Assert.Null(latest["未知"]);
        await Assert.ThrowsAsync<ArgumentException>(() => _host.GetReportDates("[null]"));
    }

    /// <summary>同项待核实和错误分别列出，去重且按账号隔离，删除后不带出历史。</summary>
    [Fact]
    public async Task IssueNames_ReturnActiveDistinctNamesForBoundAccount()
    {
        await _host.Report(Records(10));
        await _host.Report(Records(2, "delta", 1));
        await _host.Report(Records(3, "delta", 2));
        await _host.Report(Records(-1, minute: 3));
        var issues = JObject.Parse(await _host.GetIssueNames());
        Assert.Equal(new[] { "摩拉" }, issues["pending"]!.Values<string>());
        Assert.Equal(new[] { "摩拉" }, issues["errors"]!.Values<string>());
        var other = new ScriptInventory(_store, new ScriptInventoryContext("test/other", "另一个账号", "100000002"), () => null, default);
        Assert.Empty((JArray)JObject.Parse(await other.GetIssueNames())["errors"]!);
        Assert.Empty((JArray)JObject.Parse(await other.GetIssueNames())["pending"]!);
        await _store.DeleteItemAsync(Uid, "摩拉");
        issues = JObject.Parse(await _host.GetIssueNames());
        Assert.Empty((JArray)issues["pending"]!);
        Assert.Empty((JArray)issues["errors"]!);
    }

    /// <summary>设置读取无需 UID、不创建数据文件，后续调用反映最新配置。</summary>
    [Fact]
    public async Task DemandSettings_AreReadOnlyGlobalAndFresh()
    {
        var host = new ScriptInventory(_store, new ScriptInventoryContext("test/settings", "设置测试"),
            () => throw new Exception("读取全局设置不应识别 UID"), default);
        var defaults = JObject.Parse(await host.GetDemandSettings());
        Assert.False((bool)defaults["useDeltaForDemands"]!);
        Assert.True((bool)defaults["useFamiliesForDemands"]!);
        Assert.Equal(7, (int)defaults["demandCraftingReservePercent"]!);
        Assert.Equal(3, defaults.Count);
        Assert.False(Directory.Exists(Path.Combine(_root, "records")));
        await _store.UpdateSettingsAsync(settings =>
        {
            settings.UseDeltaForDemands = true;
            settings.UseFamiliesForDemands = false;
            settings.DemandCraftingReservePercent = 12;
        });
        var updated = JObject.Parse(await host.GetDemandSettings());
        Assert.True((bool)updated["useDeltaForDemands"]!);
        Assert.False((bool)updated["useFamiliesForDemands"]!);
        Assert.Equal(12, (int)updated["demandCraftingReservePercent"]!);
    }

    /// <summary>V8 返回普通 JS 对象；调用方修改副本不会写回设置。</summary>
    [Fact]
    public async Task NativeDemandSettings_ReturnDetachedObject()
    {
        await ScriptExecution.ExecuteAsync(ct =>
        {
            var engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableTaskPromiseConversion);
            ScriptInventory.Register(engine, new ScriptInventory(_store, new ScriptInventoryContext("test/settings", "设置测试"),
                () => throw new Exception("不应识别 UID"), ct));
            return engine;
        }, engine => engine.Evaluate("""
            (async () => {
                const settings = await inventory.getDemandSettings();
                if (Object.getPrototypeOf(settings) !== Object.prototype || settings.demandCraftingReservePercent !== 7)
                    throw new Error('invalid settings');
                settings.useDeltaForDemands = true;
                if ((await inventory.getDemandSettings()).useDeltaForDemands !== false)
                    throw new Error('settings mutated');
            })()
            """), default);
    }

    /// <summary>需求沿用脚本来源，同值重报保留版本，改值重建需求且不修改库存。</summary>
    [Fact]
    public async Task DemandReports_PreserveProgressAndUseScriptSource()
    {
        var combined = JArray.Parse(Records(10));
        combined[0]["targetQuantity"] = 20;
        var result = JObject.Parse(await _host.Report(combined.ToString()));
        Assert.Equal("摩拉", (string?)result["demandsAccepted"]![0]);
        var first = (await _store.ReadDemandsAsync(Uid)).Items["摩拉"];
        Assert.Equal(_context.Source, first.Source);
        await _store.ConfirmDemandAsync(Uid, "摩拉", first.Revision);
        var confirmed = (await _store.ReadDemandsAsync(Uid)).Items["摩拉"];
        await _host.Report("[{\"name\":\"摩拉\",\"targetQuantity\":20}]");
        Assert.Equal(confirmed.Revision, (await _store.ReadDemandsAsync(Uid)).Items["摩拉"].Revision);
        await _host.Report("[{\"name\":\"摩拉\",\"targetQuantity\":0}]");
        var updated = (await _store.ReadDemandsAsync(Uid)).Items["摩拉"];
        Assert.Equal(0, updated.TargetQuantity);
        Assert.Null(updated.ConfirmedAt);
        Assert.NotEqual(confirmed.Revision, updated.Revision);
        var workspace = await _store.ReadAsync(Uid);
        Assert.Equal(10, workspace.Inventory.Items["摩拉"][0].Quantity);
        Assert.Equal(_context.DisplayName, workspace.Sources[updated.Source].DisplayName);
    }

    /// <summary>非法数量或字段拒绝整批，不能先写入合法的第一项。</summary>
    [Theory]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("9007199254740992")]
    [InlineData("null")]
    [InlineData("\"1\"")]
    public async Task InvalidDemandQuantity_RejectsWholeBatch(string quantity)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _host.Report(
            "[{\"name\":\"摩拉\",\"targetQuantity\":1},{\"name\":\"须臾树脂\",\"targetQuantity\":" + quantity + "}]"));
        Assert.Empty((await _store.ReadDemandsAsync(Uid)).Items);
    }

    /// <summary>未知需求沿用库存的字典准入，失败保留原目标，分类补齐不伪造库存。</summary>
    [Fact]
    public async Task DemandNames_RequireDictionaryOrValidCategory()
    {
        await _store.SetDemandAsync(Uid, "测试材料", 5, null);
        var original = (await _store.ReadDemandsAsync(Uid)).Items["测试材料"];
        var rejected = JObject.Parse(await _host.Report("[{\"name\":\"测试材料\",\"targetQuantity\":10}]"));
        Assert.Empty((JArray)rejected["demandsAccepted"]!);
        Assert.NotNull(rejected["demandsRejected"]!["测试材料"]);
        Assert.Equal(original.Revision, (await _store.ReadDemandsAsync(Uid)).Items["测试材料"].Revision);
        Assert.Equal("UnknownName", (await _store.ReadAsync(Uid)).Inventory.Errors["测试材料"].Code);
        var accepted = JObject.Parse(await _host.Report("[{\"name\":\"测试材料\",\"targetQuantity\":10,\"category\":\"角色培养素材\",\"comment\":\"测试备注\"}]"));
        Assert.Equal("测试材料", (string?)accepted["demandsAccepted"]![0]);
        var workspace = await _store.ReadAsync(Uid);
        Assert.Equal("角色培养素材", workspace.Definitions["测试材料"]);
        Assert.False(workspace.Inventory.Items.ContainsKey("测试材料"));
        Assert.Equal(10, (await _store.ReadDemandsAsync(Uid)).Items["测试材料"].TargetQuantity);
        var warning = JObject.Parse(await _host.Report("[{\"name\":\"测试材料\",\"targetQuantity\":20,\"category\":\"其他\"}]"));
        Assert.NotNull(warning["demandsWarnings"]!["测试材料"]);
        Assert.Equal("角色培养素材", (await _store.ReadAsync(Uid)).Definitions["测试材料"]);
    }

    /// <summary>同时上传未知库存和目标时，缺分类两者均拒绝，有分类两者均采纳。</summary>
    [Fact]
    public async Task CombinedUnknownRecord_UsesCategoryForBothQuantityAndTarget()
    {
        var input = new JArray(new JObject { ["name"] = "新材料", ["quantity"] = 2,
            ["targetQuantity"] = 8, ["recordedAt"] = _time.ToString("O") });
        var rejected = JObject.Parse(await _host.Report(input.ToString()));
        Assert.NotNull(rejected["rejected"]!["新材料"]);
        Assert.NotNull(rejected["demandsRejected"]!["新材料"]);
        Assert.False((await _store.ReadDemandsAsync(Uid)).Items.ContainsKey("新材料"));
        input[0]["category"] = "素材";
        input[0]["recordedAt"] = _time.AddMinutes(1).ToString("O");
        var accepted = JObject.Parse(await _host.Report(input.ToString()));
        Assert.Equal("新材料", (string?)accepted["demandsAccepted"]![0]);
        Assert.Equal(2, (await _store.ReadAsync(Uid)).Inventory.Items["新材料"][0].Quantity);
        Assert.Equal(8, (await _store.ReadDemandsAsync(Uid)).Items["新材料"].TargetQuantity);
    }

    /// <summary>真实 V8 校验非有限值和安全整数边界，并返回原生数组。</summary>
    [Fact]
    public async Task NativeDemandReports_ValidateNumbersAndReturnAcceptedNames()
    {
        await ScriptExecution.ExecuteAsync(ct =>
        {
            var engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableTaskPromiseConversion);
            ScriptInventory.Register(engine, new ScriptInventory(_store, _context, () => Uid, ct));
            return engine;
        }, engine => engine.Evaluate("""
            (async () => {
                for (const targetQuantity of [-1, 1.5, NaN, Infinity, 9007199254740992, '1', null, undefined]) {
                    let rejected = false;
                    try { await inventory.report([{name: '摩拉', targetQuantity}]); } catch { rejected = true; }
                    if (!rejected) throw new Error('invalid target accepted');
                }
                const result = await inventory.report([{name: '摩拉', targetQuantity: Number.MAX_SAFE_INTEGER},
                    {name: '须臾树脂', targetQuantity: 0}]);
                if (!Array.isArray(result.demandsAccepted) || result.demandsAccepted.length !== 2) throw new Error('invalid result');
            })()
            """), default);
        var demands = await _store.ReadDemandsAsync(Uid);
        Assert.Equal(9007199254740991L, demands.Items["摩拉"].TargetQuantity);
        Assert.Equal(0, demands.Items["须臾树脂"].TargetQuantity);
    }
}

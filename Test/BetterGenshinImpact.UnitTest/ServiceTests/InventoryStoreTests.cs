using System.Globalization;
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.Service.AccountRecords.Model;
using BetterGenshinImpact.ViewModel.Controls;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.ServiceTests;

/// <summary>在临时目录验证库存合并、异常、快照和恢复，不启动游戏或 UI。</summary>
[Collection("AccountRecordsManualOperations")]
public sealed class InventoryStoreTests : IDisposable
{
    /// <summary>每个测试独立的数据根。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BetterGI.InventoryTests", Guid.NewGuid().ToString("N"));
    /// <summary>小型可控公共目录。</summary>
    private readonly InventoryCatalog _catalog;
    /// <summary>测试存储。</summary>
    private readonly InventoryStore _store;
    /// <summary>固定测试账号，与实际账号无关。</summary>
    private const string Uid = "100000001";

    /// <summary>人工确认归档积压错误并终止待核实增量，保留先前已完成核实的证据。</summary>
    [Fact]
    public async Task ManualConfirmation_ArchivesErrorsAndAbortsPendingButPreservesPreviousVerification()
    {
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 100)]);
        await _store.ReportDeltaAsync(Uid, Source("gain"), [new()
        { Name = "摩拉", Delta = 20, RecordedAt = _time.AddMinutes(1).ToString("O") }]);
        var verified = Report("摩拉", 120, 2);

        await _store.ReportAsync(Uid, Source("scan"), [verified]);
        var previous = (await _store.ReadAsync(Uid)).Inventory.Events.Single(item => item.Kind == "Delta");
        await _store.ReportAsync(Uid, Source("bad-a"), [Report("摩拉", -1, 3)]);
        await _store.ReportAsync(Uid, Source("bad-b"), [Report("摩拉", -2, 4)]);
        // 红灯不阻断后续有效核实记录更新当前库存。
        var later = Report("摩拉", 122, 4);

        await _store.ReportAsync(Uid, Source("later-scan"), [later]);
        Assert.Equal(122, (await _store.ReadAsync(Uid)).Inventory.Items["摩拉"][0].Quantity);
        await _store.ReportDeltaAsync(Uid, Source("gain"), [new()
        { Name = "摩拉", Delta = 5, RecordedAt = _time.AddMinutes(5).ToString("O") }]);
        var before = (await _store.ReadAsync(Uid)).Inventory;
        var errors = before.Events.Where(item => item.State == "Error").Select(item => item.Id).ToArray();
        var pendingId = before.Events.Single(item => item.State == "Pending").Id;

        await _store.SetLocalQuantityAsync(Uid, "摩拉", 124);
        var after = (await _store.ReadAsync(Uid)).Inventory;
        Assert.Equal(124, after.Items["摩拉"][0].Quantity);
        Assert.Equal("AccountRecords.SetLocalQuantityAsync", after.Items["摩拉"][0].Source);
        Assert.Empty(after.Errors);
        Assert.Equal(2, errors.Length);
        Assert.All(errors, id => Assert.Contains(after.Events, item => item.Id == id && item.State == "Error" && item.Archived));
        var completed = after.Events.Single(item => item.Id == pendingId);
        Assert.Equal("Aborted", completed.State);
        Assert.True(completed.Archived);
        Assert.Null(completed.VerifiedCorrect);
        Assert.Empty(completed.VerificationSource);
        Assert.Contains(completed.History, line => line.Contains("ManualConfirmation") && line.Contains("Quantity=124"));
        var oldCompleted = after.Events.Single(item => item.Id == previous.Id);
        Assert.Equal(previous.VerificationId, oldCompleted.VerificationId);
        Assert.Equal(previous.VerifiedCorrect, oldCompleted.VerifiedCorrect);
        Assert.Equal(previous.History, oldCompleted.History);
        Assert.False(oldCompleted.Archived);
        Assert.Equal("Completed", InventoryEventPolicy.SelectActive(after.Events.Where(item => item.Name == "摩拉"))!.State);

        // 后续读取不能因为最近库存来自人工就清除新错误；再次人工提交才结束它。
        await _store.ReportFailureAsync(Uid, Source("new-failure"), "摩拉", "新的采集失败");
        Assert.Contains("摩拉", (await _store.ReadAsync(Uid)).Inventory.Errors.Keys);
        await _store.SetLocalQuantityAsync(Uid, "摩拉", 130);
        var repeated = (await _store.ReadAsync(Uid)).Inventory;
        Assert.Equal(completed.VerificationId, repeated.Events.Single(item => item.Id == pendingId).VerificationId);
        Assert.Null(repeated.Events.Single(item => item.Id == pendingId).VerifiedCorrect);
        Assert.Equal(completed.History, repeated.Events.Single(item => item.Id == pendingId).History);
        var resolved = (await _store.ReadAsync(Uid)).Inventory;
        Assert.Empty(resolved.Errors);
        Assert.Equal(130, resolved.Items["摩拉"][0].Quantity);
    }

    /// <summary>修订未知名称会处理全部积压错误，以人工总数覆盖目标，后续同名上报仍独立校验。</summary>
    [Fact]
    public async Task ManualRename_ArchivesAllOriginalNameErrorsWithoutCreatingAlias()
    {
        await _store.ReportAsync(Uid, Source("scan"), [Report("未知名称", 10)]);
        await _store.ReportAsync(Uid, Source("scan-2"), [Report("未知名称", 20, 1)]);
        await _store.ReportDeltaAsync(Uid, Source("delta"), [new()
        { Name = "未知名称", Delta = 5, RecordedAt = _time.AddMinutes(2).ToString("O") }]);
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 100)]);
        var workspace = await _store.ReadAsync(Uid);
        var error = workspace.Inventory.Errors["未知名称"];
        var editor = new AccountRecordsInventoryEditViewModel(_store, _catalog, Uid,
            new AccountRecordsInventoryRow { Name = "未知名称", Error = error }, workspace.Definitions)
        { Name = "摩拉", QuantityText = "12" };
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.Empty(editor.Message);
        var after = (await _store.ReadAsync(Uid)).Inventory;
        Assert.Equal(12, after.Items["摩拉"][0].Quantity);
        Assert.DoesNotContain("未知名称", after.Errors.Keys);
        var archived = after.Events.Where(item => item.Name == "未知名称").ToArray();
        Assert.Equal(3, archived.Length);
        Assert.All(archived, item =>
        {
            Assert.True(item.Archived);
            Assert.Equal("Error", item.State);
            Assert.Contains(item.History, line => line.Contains("摩拉") && line.Contains("12"));
        });
        Assert.DoesNotContain("未知名称", after.Items.Keys);
        await _store.ReportAsync(Uid, Source("new-scan"), [Report("未知名称", 30, 3)]);
        var later = (await _store.ReadAsync(Uid)).Inventory;
        Assert.Contains("未知名称", later.Errors.Keys);
        Assert.Equal(12, later.Items["摩拉"][0].Quantity);
    }

    /// <summary>所有空名称形式均在批次写入前拒绝，不留下有效项的部分写入或空名称错误行。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public async Task EmptyNames_RejectWholeBatchBeforeWriting(string? name)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _store.ReportAsync(Uid, Source("scan"),
            [Report("摩拉", 100), Report(name!, 10)]));
        await Assert.ThrowsAsync<ArgumentException>(() => _store.ReportDeltaAsync(Uid, Source("delta"),
            [new() { Name = "摩拉", Delta = 1, RecordedAt = _time.ToString("O") },
             new() { Name = name!, Delta = 1, RecordedAt = _time.ToString("O") }]));
        var data = (await _store.ReadAsync(Uid)).Inventory;
        Assert.Empty(data.Items);
        Assert.Empty(data.Errors);
        Assert.Empty(data.Events);
    }

    /// <summary>旧错误在详情中使用明确的人工核实指引，但不修改原始事件证据。</summary>
    [Fact]
    public void LegacyVerificationError_ExplainsTimeOrderWithoutRewritingEvidence()
    {
        const string original = "核实记录已过期，无法确认当前库存变动。";
        var entry = new InventoryEvent { Name = "摩拉", State = "Error", Message = original, History = [original] };
        var viewModel = new AccountRecordsEventsViewModel(new InventoryDocument { Events = [entry] }, entry.Id,
            (_, _) => Task.FromResult(new InventoryDocument()));
        Assert.Contains("早于待核实的库存变动", viewModel.Details);
        Assert.Contains("当前实际库存总数", viewModel.Details);
        Assert.Equal(original, entry.Message);
        Assert.Contains(original, viewModel.HistoryDetails);
    }

    /// <summary>仅事件变化也更新总表文件时间；轻量检查不依赖库存业务时间或读取内容。</summary>
    [Fact]
    public async Task SnapshotTimestamp_TracksEventOnlyChanges()
    {
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 10)]);
        await _store.ReportDeltaAsync(Uid, Source("delta"), [new()
        { Name = "摩拉", Delta = 1, RecordedAt = _time.AddMinutes(1).ToString("O") }]);
        var path = Path.Combine(_root, "records", Uid, "inventory.json");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-1));
        var before = await _store.ReadAsync(Uid);
        Assert.Equal(before.UpdatedAt, await _store.GetInventoryUpdatedAtAsync(Uid));
        var pending = before.Inventory.Events.Single(item => item.State == "Pending");
        await _store.UpdateEventAsync(Uid, pending.Id, true, false);
        var after = await _store.ReadAsync(Uid);
        Assert.Equal(before.Inventory.ModifiedAt, after.Inventory.ModifiedAt);
        Assert.True(after.UpdatedAt > before.UpdatedAt);
        Assert.Equal(after.UpdatedAt, await _store.GetInventoryUpdatedAtAsync(Uid));
    }

    /// <summary>提交时排除新出现阻塞的物品，不归档新到达的完成事件或其他类型。</summary>
    [Fact]
    public async Task BatchArchive_RechecksBlockersAndPreservesUnselectedEvents()
    {
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 10), Report("须臾树脂", 2)]);
        var data = (await _store.ReadAsync(Uid)).Inventory;
        data.Events.AddRange([
            new() { Id = "a", Name = "摩拉", Kind = "Delta", State = "Completed" },
            new() { Id = "b", Name = "须臾树脂", Kind = "Delta", State = "Completed" }
        ]);
        var ids = InventoryEventPolicy.GetCompletedArchiveCandidates(data, "Delta").Select(item => item.Id).ToArray();
        Assert.Equal(2, ids.Length);
        data.Events.AddRange([
            new() { Id = "z-block", Name = "须臾树脂", Kind = "Delta", State = "Pending" },
            new() { Id = "c", Name = "摩拉", Kind = "Delta", State = "Completed" },
            new() { Id = "0-other", Name = "摩拉", Kind = "Requirement", State = "Completed" }
        ]);
        var path = Path.Combine(_root, "records", Uid, "inventory.json");
        File.WriteAllText(path, JsonConvert.SerializeObject(data));
        Assert.Equal(1, await _store.ArchiveCompletedEventsAsync(Uid, "Delta", ids));
        data = (await _store.ReadAsync(Uid)).Inventory;
        Assert.True(data.Events.Single(item => item.Id == "a").Archived);
        Assert.All(data.Events.Where(item => item.Id is "b" or "c" or "0-other"), item => Assert.False(item.Archived));
        // 主动请求也不能越过尚未解决的 Pending。
        Assert.Equal(0, await _store.ArchiveCompletedEventsAsync(Uid, "Delta", ["b"]));
    }

    /// <summary>新账号的展示读取仅返回默认内容，不建立任何数据文件。</summary>
    [Fact]
    public async Task ReadSnapshot_DoesNotInitializeStorage()
    {
        var directory = Path.Combine(_root, "records");
        Assert.False(Directory.Exists(directory));
        var snapshot = await _store.ReadAsync(Uid);
        Assert.Empty(snapshot.Inventory.Items);
        Assert.Contains("摩拉", snapshot.Definitions.Keys);
        Assert.False(Directory.Exists(directory));
    }

    /// <summary>到期状态由显式维护更新；读取展示不能改写库存、快照或提醒。</summary>
    [Fact]
    public async Task ReadSnapshot_SeparatesExpirationMaintenance()
    {
        var report = Report("须臾树脂", 2);
        report.Expirations = [new() { Quantity = 2, ExpiresAt = _time.AddDays(3).ToString("O") }];
        await _store.ReportAsync(Uid, Source("scan"), [report]);
        var directory = Path.Combine(_root, "records");
        var path = Path.Combine(directory, Uid, "inventory.json");
        var document = JObject.Parse(File.ReadAllText(path));
        document["Items"]!["须臾树脂"]![0]!["Expirations"]![0]!["ExpiresAt"] = _time.AddMinutes(-1).ToString("O");
        File.WriteAllText(path, document.ToString());
        var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(file => file, file => (Text: File.ReadAllText(file), Time: File.GetLastWriteTimeUtc(file)));
        var snapshot = await _store.ReadAsync(Uid);
        Assert.DoesNotContain(snapshot.Inventory.Events, item => item.Kind == "ExpirationCheck" && !item.Archived);
        Assert.Equal(files.Count, Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Length);
        foreach (var (file, previous) in files)
        {
            Assert.Equal(previous.Text, File.ReadAllText(file));
            Assert.Equal(previous.Time, File.GetLastWriteTimeUtc(file));
        }
        await _store.MaintainAsync(Uid);
        Assert.Contains((await _store.ReadAsync(Uid)).Inventory.Events, item => item.Kind == "ExpirationCheck" && item.State == "Pending" && !item.Archived);
    }

    /// <summary>新关联独立于日志文案；旧记录可在读取时补齐关联且不重写文件。</summary>
    [Fact]
    public async Task ErrorAssociation_UsesTypedIdAndReadsLegacyHistory()
    {
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 10)]);
        await _store.ReportAsync(Uid, Source("old"), [Report("摩拉", 8, -1)]);
        var data = (await _store.ReadAsync(Uid)).Inventory;
        var error = data.Errors["摩拉"];
        var item = data.Events.Single(value => value.State == "Error");
        Assert.Equal(error.Id, item.ErrorId);
        item.History.Clear();
        Assert.False(new AccountRecordsInventoryRow { Error = error, Event = item }.CanAcceptDisplayedError);

        var path = Path.Combine(_root, "records", Uid, "inventory.json");
        var document = JObject.Parse(File.ReadAllText(path));
        var oldEvent = document["Events"]!.OfType<JObject>().Single(value => (string?)value["Id"] == item.Id);
        oldEvent.Remove("ErrorId");
        var legacy = document.ToString();
        File.WriteAllText(path, legacy);
        data = (await _store.ReadAsync(Uid)).Inventory;
        Assert.Equal(error.Id, data.Events.Single(value => value.Id == item.Id).ErrorId);
        Assert.Equal(legacy, File.ReadAllText(path));
        await _store.ResolveAsync(Uid, "摩拉", error.Id, false, item.Id);
        Assert.Equal(10, (await _store.ReadAsync(Uid)).Inventory.Items["摩拉"][0].Quantity);
    }

    /// <summary>先打开的编辑器在提交时仍受保护，而任务上报和查看库存不受锁影响。</summary>
    [Fact]
    public async Task ManualEditor_IsBlockedButAutomaticReportsContinue()
    {
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80)]);
        var workspace = await _store.LoadAsync(Uid);
        var editor = new AccountRecordsInventoryEditViewModel(_store, _catalog, Uid,
            new AccountRecordsInventoryRow { Name = "摩拉", Quantity = 80 }, workspace.Definitions)
        { QuantityText = "999" };
        using (await AccountRecordsInteractionGate.Instance.LockForTaskAsync())
        {
            await editor.SaveCommand.ExecuteAsync(null);
            Assert.Equal(AccountRecordsInteractionGate.LockedMessage, editor.Message);
            Assert.Equal(80, (await _store.LoadAsync(Uid)).Inventory.Items["摩拉"][0].Quantity);
            await _store.ReportAsync(Uid, Source("another-scan"), [Report("摩拉", 81, 1)]);
            Assert.Equal(81, (await _store.LoadAsync(Uid)).Inventory.Items["摩拉"][0].Quantity);
        }
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.Equal("", editor.Message);
        Assert.Equal(999, (await _store.LoadAsync(Uid)).Inventory.Items["摩拉"][0].Quantity);
    }

    /// <summary>正常报告保留批次证据，重复输入不新增事件，日常读取不加载归档。</summary>
    [Fact]
    public async Task EventHistory_RecordsSuccessWithoutChangingDuplicateHandling()
    {
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80)]);
        var item = Assert.Single((await _store.LoadAsync(Uid)).Inventory.Events);
        Assert.Equal("Completed", item.State);
        Assert.True(item.Archived);
        Assert.NotEmpty(item.AttemptId);
        Assert.Empty((await _store.LoadAsync(Uid, includeHistory: false)).Inventory.Events);
        Assert.Equal(1, (await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80)])).DuplicateCount);
        Assert.Single((await _store.LoadAsync(Uid)).Inventory.Events);
        Assert.DoesNotContain("Attempts", File.ReadAllText(Path.Combine(_root, "records", "sources.json")));
    }

    /// <summary>旧来源摘要仅迁移一次，旧设置可读取，后续保存只使用新字段。</summary>
    [Fact]
    public async Task EventHistory_MigratesLegacySourcesAndSettings()
    {
        var directory = Path.Combine(_root, "records");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "settings.json"), "{\"ArchivedEventLimit\":100}");
        File.WriteAllText(Path.Combine(directory, "sources.json"), JsonConvert.SerializeObject(new
        {
            scan = new { Reference = "scan", DisplayName = "scan", Attempts = new[]
                { new { Id = "old-attempt", Uid, CreatedAt = _time, Failed = true, Errors = new[] { "旧错误" } } } }
        }));
        var workspace = await _store.LoadAsync(Uid);
        Assert.Equal(1000, workspace.Settings.EventLimit);
        var item = Assert.Single(workspace.Inventory.Events);
        Assert.Equal("old-attempt", item.AttemptId);
        Assert.Equal("Error", item.State);
        Assert.True(item.Archived);
        Assert.DoesNotContain("Attempts", File.ReadAllText(Path.Combine(directory, "sources.json")));
        Assert.Single((await _store.LoadAsync(Uid)).Inventory.Events);
        await _store.ConfigureAsync(workspace.Settings);
        Assert.DoesNotContain("ArchivedEventLimit", File.ReadAllText(Path.Combine(directory, "settings.json")));
    }

    /// <summary>旧字典中不支持的 UID 不阻塞合法账号迁移，重新打开后不会重复迁移。</summary>
    [Theory]
    [InlineData("1")]
    [InlineData("012345678")]
    [InlineData("12345678901234567890")]
    public async Task SourceMigration_SkipsUnsupportedUidAndPersistsCleanup(string unsupportedUid)
    {
        var directory = Path.Combine(_root, "records");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "sources.json");
        File.WriteAllText(path, JsonConvert.SerializeObject(new
        {
            scan = new { Reference = "scan", DisplayName = "scan", Attempts = new[]
            {
                new { Id = "unsupported", Uid = unsupportedUid, CreatedAt = _time, Failed = false, Errors = Array.Empty<string>() },
                new { Id = "supported", Uid, CreatedAt = _time, Failed = false, Errors = Array.Empty<string>() }
            } }
        }));
        var workspace = await _store.LoadAsync(Uid);
        Assert.Equal("supported", Assert.Single(workspace.Inventory.Events).AttemptId);
        Assert.False(Directory.Exists(Path.Combine(directory, unsupportedUid)));
        Assert.DoesNotContain("Attempts", File.ReadAllText(path));
        var reopened = new InventoryStore(_catalog, directory);
        Assert.Single((await reopened.LoadAsync(Uid)).Inventory.Events);
        await reopened.ReportAsync(Uid, Source("scan"), [Report("摩拉", 10)]);
        Assert.Equal(10, (await reopened.ReadAsync(Uid)).Inventory.Items["摩拉"][0].Quantity);
    }

    /// <summary>完成状态只为增量事件显示核实结果，显式空来源也不会阻断建行。</summary>
    [Theory]
    [InlineData("Report", null, false)]
    [InlineData("Requirement", null, false)]
    [InlineData("Delta", null, true)]
    [InlineData("Delta", "scan", true)]
    public async Task CompletedRow_OnlyDeltaShowsVerification(string kind, string? verificationSource, bool showsResult)
    {
        var workspace = await _store.ReadAsync(Uid);
        workspace.Sources["scan"] = new AccountRecordSource { Reference = "scan", DisplayName = "测试扫描" };
        workspace.Settings.PreferReadableSource = true;
        workspace.Inventory.Events.Add(JsonConvert.DeserializeObject<InventoryEvent>(JsonConvert.SerializeObject(new
        {
            Name = "摩拉", Kind = kind, State = "Completed", Source = "scan",
            VerificationSource = verificationSource, VerifiedCorrect = true
        }))!);
        var rows = await new AccountRecordsInventoryRowFactory(_catalog, new InventoryIconCache()).BuildAsync(workspace);
        var row = Assert.Single(rows);
        Assert.Equal(showsResult, row.StatusDetails.Contains("结果："));
        if (showsResult) Assert.Contains("累计推算与确认库存：一致", row.StatusDetails);
        if (showsResult && verificationSource != null) Assert.Contains("核实来源：测试扫描", row.StatusDetails);
    }

    /// <summary>软上限保护未解决事件，成功记录可淘汰；保存后不能复活已清理事件。</summary>
    [Fact]
    public async Task EventRetention_ProtectsUnresolvedEventsAboveLimit()
    {
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80)]);
        var path = Path.Combine(_root, "records", Uid, "inventory.json");
        var data = JsonConvert.DeserializeObject<InventoryDocument>(File.ReadAllText(path))!;
        for (var index = 0; index < 1001; index++)
            data.Events.Add(new InventoryEvent { Name = "摩拉", State = index % 2 == 0 ? "Pending" : "Error" });
        File.WriteAllText(path, JsonConvert.SerializeObject(data));
        await _store.ConfigureAsync(new AccountRecordsSettings { EventLimit = 1000 });
        var retained = (await _store.LoadAsync(Uid)).Inventory.Events;
        Assert.Equal(1001, retained.Count);
        Assert.All(retained, item => Assert.False(item.Archived));
        Assert.DoesNotContain(retained, item => item.State == "Completed");
        Assert.Equal(5000, new AccountRecordsSettings().EventLimit);
    }

    /// <summary>错误筛选不改变完整快照，从具体提醒跳转时允许显示非错误目标。</summary>
    [Fact]
    public void EventDialog_DefaultFilterAndExplicitNavigation()
    {
        var error = new InventoryEvent { State = "Error" };
        var pending = new InventoryEvent { State = "Pending" };
        InventoryEvent[] events = [error, pending];
        var inventory = new InventoryDocument { Events = events.ToList() };
        var vm = new AccountRecordsEventsViewModel(inventory, null,
            (_, _) => Task.FromResult(inventory));
        Assert.Same(error, Assert.Single(vm.Events));
        vm.ErrorsOnly = false;
        Assert.Equal(2, vm.Events.Count);
        var targeted = new AccountRecordsEventsViewModel(inventory, pending.Id,
            (_, _) => Task.FromResult(inventory));
        Assert.False(targeted.ErrorsOnly);
        Assert.Same(pending, targeted.Selected);
    }

    /// <summary>清空用户字典后删除补充项，即使有旧备份也不恢复；原始物品目录仍保留。</summary>
    [Theory]
    [InlineData("")]
    [InlineData(" \r\n\t")]
    [InlineData("{}")]
    public async Task UserDictionary_EmptyContentDoesNotRestoreBackup(string content)
    {
        var path = Path.Combine(_root, "records", "item-overrides.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var original = JsonConvert.SerializeObject(new Dictionary<string, InventoryItemOverride>
        { ["测试补充物品"] = new() { Category = "测试分类" } });
        File.WriteAllText(path, original);
        File.WriteAllText(path + ".bak", original);
        Assert.Contains("测试补充物品", (await _store.LoadAsync(Uid)).Definitions.Keys);
        File.WriteAllText(path, content);
        var definitions = (await _store.LoadAsync(Uid)).Definitions;
        Assert.DoesNotContain("测试补充物品", definitions.Keys);
        Assert.Contains("摩拉", definitions.Keys);
        Assert.Equal(content, File.ReadAllText(path));
    }
    /// <summary>所有测试上报早于当前时间。</summary>
    private readonly DateTimeOffset _time = DateTimeOffset.UtcNow.AddHours(-2);

    /// <summary>建立测试字典，不依赖资源包或用户目录。</summary>
    public InventoryStoreTests()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "items.csv");
        File.WriteAllText(path, "item_name,material_type\n甜甜花,\n摩拉,货币\n");
        _catalog = new InventoryCatalog(path);
        _store = new InventoryStore(_catalog, Path.Combine(_root, "records"));
    }

    /// <summary>构造明确带时区的上报。</summary>
    private InventoryReport Report(string name, long count, int minute = 0) => new()
    { Name = name, Quantity = count, RecordedAt = _time.AddMinutes(minute).ToString("O", CultureInfo.InvariantCulture) };

    /// <summary>来源引用使用原始目录。</summary>
    private static AccountRecordSource Source(string reference) => new() { Reference = reference, DisplayName = reference };

    /// <summary>交换只携带当前项，往返保留零值、原始 UTC 时间和来源。</summary>
    [Fact]
    public async Task Exchange_RoundTripPreservesCurrentRecord()
    {
        await _store.ReportAsync(Uid, Source("a"), [Report("摩拉", 8)]);
        await _store.ReportAsync(Uid, Source("b"), [Report("摩拉", 0, 1)]);
        var package = await _store.ExportInventoryAsync(Uid);
        var path = Path.Combine(_root, "exchange.json");
        await File.WriteAllTextAsync(path, package.ToString());
        var parsed = await InventoryStore.ReadExchangeAsync(path);
        var result = await _store.ImportInventoryAsync(parsed, "100000002", false);
        Assert.Single(result.Accepted);
        var record = Assert.Single((await _store.LoadAsync("100000002")).Inventory.Items["摩拉"]);
        Assert.Equal(0L, record.Quantity);
        Assert.Equal(_time.AddMinutes(1), record.RecordedAt);
        Assert.Equal("b", record.Source);
    }

    /// <summary>缺少历史时间不能误写为本次导入时间，任何结构错误都应在写入前拒绝。</summary>
    [Fact]
    public async Task Exchange_MissingTimeRejectedBeforeWrite()
    {
        await _store.ReportAsync(Uid, Source("a"), [Report("摩拉", 8)]);
        var package = await _store.ExportInventoryAsync(Uid);
        ((Newtonsoft.Json.Linq.JObject)package["accounts"]![Uid]!["inventory"]!["摩拉"]![0]!).Remove("recordedAt");
        await Assert.ThrowsAsync<InvalidDataException>(() => _store.ImportInventoryAsync(package, "100000002", false));
        Assert.DoesNotContain("100000002", await _store.ListUidsAsync());
    }

    /// <summary>旧导入进入异常；文件不能篡改已登记的来源名称或清除未提及的材料。</summary>
    [Fact]
    public async Task Exchange_OldRecordKeepsCurrentAndLocalSourceName()
    {
        await _store.ReportAsync(Uid, Source("a"), [Report("摩拉", 8)]);
        var package = await _store.ExportInventoryAsync(Uid);
        package["sources"]!["a"]!["displayName"] = "外部改名";
        await _store.ReportAsync("100000002", Source("a"), [Report("摩拉", 9, 1), Report("甜甜花", 3, 1)]);
        var result = await _store.ImportInventoryAsync(package, "100000002", false);
        Assert.Single(result.Rejected);
        var workspace = await _store.LoadAsync("100000002");
        Assert.Equal(9L, workspace.Inventory.Items["摩拉"][0].Quantity);
        Assert.Equal(3L, workspace.Inventory.Items["甜甜花"][0].Quantity);
        Assert.Equal("a", workspace.Sources["a"].DisplayName);
        Assert.Equal("RecordConflict", workspace.Inventory.Errors["摩拉"].Code);
    }

    /// <summary>外部字典须明确授权；授权后也不能覆盖本地已有分类。</summary>
    [Fact]
    public async Task Exchange_DefinitionsRequireConsentAndPreserveLocalCategory()
    {
        await _store.ReportAsync(Uid, Source("a"), [Report("甜甜花", 8)]);
        var package = await _store.ExportInventoryAsync(Uid);
        package["itemDefinitions"]!["甜甜花"] = new Newtonsoft.Json.Linq.JObject { ["category"] = "外部分类" };
        await _store.ImportInventoryAsync(package, "100000002", false);
        Assert.Equal("", (await _store.LoadAsync("100000002")).Definitions["甜甜花"]);
        await _store.DefineAsync("甜甜花", "本地分类");
        await _store.ImportInventoryAsync(package, "100000002", true);
        Assert.Equal("本地分类", (await _store.LoadAsync("100000002")).Definitions["甜甜花"]);
    }

    /// <summary>数量不变也更新，同一来源只留一项并移动到队首。</summary>
    [Fact]
    public async Task SameQuantity_MovesSourceToFrontWithoutDuplicate()
    {
        await _store.ReportAsync(Uid, Source("a"), [Report("甜甜花", 8)]);
        await _store.ReportAsync(Uid, Source("b"), [Report("甜甜花", 8, 1)]);
        await _store.ReportAsync(Uid, Source("a"), [Report("甜甜花", 8, 2)]);
        var records = (await _store.LoadAsync(Uid)).Inventory.Items["甜甜花"];
        Assert.Equal(new[] { "a", "b" }, records.Select(record => record.Source));
        Assert.Equal(_time.AddMinutes(2), records[0].RecordedAt);
    }

    /// <summary>部分异常不阻止有效项目；零值是真实记录。</summary>
    [Fact]
    public async Task InvalidNameAndNegativeQuantity_DoNotBlockValidZero()
    {
        await _store.ReportAsync(Uid, Source("a"), [Report("摩拉", 0), Report("甜甜花", -1), Report("未知物品", 5)]);
        var document = (await _store.LoadAsync(Uid)).Inventory;
        Assert.Equal(0L, document.Items["摩拉"][0].Quantity);
        Assert.False(document.Items.ContainsKey("未知物品"));
        Assert.Equal(2, document.Errors.Count);
    }

    /// <summary>识别失败不得把已有数量改成零或刷新其时间。</summary>
    [Fact]
    public async Task ReadFailure_PreservesLastValidRecord()
    {
        await _store.ReportAsync(Uid, Source("a"), [Report("甜甜花", 8)]);
        await _store.ReportFailureAsync(Uid, Source("a"), "甜甜花", "没有识别到数量");
        var document = (await _store.LoadAsync(Uid)).Inventory;
        Assert.Equal(10L, document.Items["甜甜花"][0].Quantity);
        Assert.Equal(_time.AddMinutes(2), document.Items["甜甜花"][0].RecordedAt);
        Assert.Equal("ReadFailed", document.Errors["甜甜花"].Code);
    }

    /// <summary>清表保留 UID，而删除账号移除 UID；两者都移除库存。</summary>
    [Fact]
    public async Task ClearAndDeleteUid_HaveDistinctSemantics()
    {
        await _store.ReportAsync(Uid, Source("a"), [Report("甜甜花", 8)]);
        await _store.ClearAsync(Uid, false, false);
        Assert.Contains(Uid, await _store.ListUidsAsync());
        Assert.Empty((await _store.LoadAsync(Uid)).Inventory.Items);
        await _store.ClearAsync(Uid, true, true);
        Assert.DoesNotContain(Uid, await _store.ListUidsAsync());
    }

    /// <summary>冲突错误禁止接受；舍弃只清理异常，不覆盖确认库存。</summary>
    [Fact]
    public async Task OlderRecord_CannotBeAccepted()
    {
        await _store.ReportAsync(Uid, Source("a"), [Report("甜甜花", 10, 2)]);
        await _store.ReportAsync(Uid, Source("b"), [Report("甜甜花", 8)]);
        var document = (await _store.LoadAsync(Uid)).Inventory;
        Assert.Equal(10L, document.Items["甜甜花"][0].Quantity);
        var error = document.Errors["甜甜花"];
        Assert.Equal(10L, error.Current!.Quantity);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.ResolveAsync(Uid, "甜甜花", error.Id, true));
        await _store.ResolveAsync(Uid, "甜甜花", error.Id, false);
        document = (await _store.LoadAsync(Uid)).Inventory;
        Assert.Equal(10L, document.Items["甜甜花"][0].Quantity);
        Assert.Equal(_time.AddMinutes(2), document.Items["甜甜花"][0].RecordedAt);
        Assert.Empty(document.Errors);
    }

    /// <summary>打开异常详情后发生新上报，旧处理请求不能覆盖新库存。</summary>
    [Fact]
    public async Task ConflictAcceptance_RejectsChangedCurrentRecord()
    {
        await _store.ReportAsync(Uid, Source("a"), [Report("甜甜花", 10, 2)]);
        await _store.ReportAsync(Uid, Source("b"), [Report("甜甜花", 8)]);
        var error = (await _store.LoadAsync(Uid)).Inventory.Errors["甜甜花"];
        await _store.ReportAsync(Uid, Source("c"), [Report("甜甜花", 12, 3)]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.ResolveAsync(Uid, "甜甜花", error.Id, true));
        Assert.Equal(12L, (await _store.LoadAsync(Uid)).Inventory.Items["甜甜花"][0].Quantity);
    }

    /// <summary>队列超过配置上限只淘汰最久未上报的来源。</summary>
    [Fact]
    public async Task RecentQueue_RespectsConfiguredLimit()
    {
        await _store.ConfigureAsync(new AccountRecordsSettings { RecentLimit = 2 });
        await _store.ReportAsync(Uid, Source("a"), [Report("甜甜花", 8)]);
        await _store.ReportAsync(Uid, Source("b"), [Report("甜甜花", 8, 1)]);
        await _store.ReportAsync(Uid, Source("c"), [Report("甜甜花", 8, 2)]);
        Assert.Equal(new[] { "c", "b" }, (await _store.LoadAsync(Uid)).Inventory.Items["甜甜花"].Select(record => record.Source));
    }

    /// <summary>不带时区的外部时间被明确拒绝，而不是按本机时间猜测。</summary>
    [Fact]
    public async Task AmbiguousTime_ProducesErrorWithoutInventory()
    {
        await _store.ReportAsync(Uid, Source("a"), [new InventoryReport { Name = "甜甜花", Quantity = 8, RecordedAt = "2026-10-26 12:00:00" }]);
        var document = (await _store.LoadAsync(Uid)).Inventory;
        Assert.Empty(document.Items);
        Assert.False(document.Errors["甜甜花"].CanAccept);
    }

    /// <summary>保留记录重传不因当前值更新或待核实增量而制造新错误。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptedDuplicate_PrecedesConflictAndVerification(bool withPendingDelta)
    {
        var original = Report("摩拉", 80);
        await _store.ReportAsync(Uid, Source("first"), [original]);
        await _store.ReportAsync(Uid, Source("second"), [Report("摩拉", 90, 1)]);
        if (withPendingDelta)
            await _store.ReportDeltaAsync(Uid, Source("delta"),
                [new InventoryDeltaReport { Name = "摩拉", Delta = -1, RecordedAt = _time.AddMinutes(2).ToString("O") }]);
        var result = await _store.ReportAsync(Uid, Source("first"), [original]);
        Assert.Equal(1, result.DuplicateCount);
        Assert.Empty(result.Accepted);
        Assert.Empty(result.Rejected);
        var data = (await _store.LoadAsync(Uid)).Inventory;
        Assert.Equal(90L, data.Items["摩拉"][0].Quantity);
        Assert.Empty(data.Errors);
        Assert.DoesNotContain(data.Events, item => !item.Archived && (item.State == "Error" || item.State == "Completed"));
        if (withPendingDelta) Assert.Contains(data.Events, item => item.State == "Pending" && !item.Archived);
    }

    /// <summary>重复识别不能吞掉同时间不同数量或同批矛盾的输入。</summary>
    [Fact]
    public async Task AcceptedDuplicate_DoesNotHideConflictingQuantity()
    {
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80)]);
        var different = await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 81)]);
        Assert.Single(different.Rejected);
        Assert.Equal(0, different.DuplicateCount);
        var conflictingBatch = await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80), Report("摩拉", 82)]);
        Assert.Single(conflictingBatch.Rejected);
        Assert.Equal(0, conflictingBatch.DuplicateCount);
        Assert.Equal(80L, (await _store.LoadAsync(Uid)).Inventory.Items["摩拉"][0].Quantity);
    }

    /// <summary>同批重复但互相矛盾的材料不可通过先后顺序静默覆盖。</summary>
    [Fact]
    public async Task DuplicateConflict_RejectsOnlyConflictingItem()
    {
        await _store.ReportAsync(Uid, Source("a"), [Report("甜甜花", 1), Report("甜甜花", 2), Report("摩拉", 3)]);
        var document = (await _store.LoadAsync(Uid)).Inventory;
        Assert.False(document.Items.ContainsKey("甜甜花"));
        Assert.Equal(3L, document.Items["摩拉"][0].Quantity);
        Assert.Contains("甜甜花", document.Errors.Keys);
    }

    /// <summary>多个服务实例串行修改同一账号，不丢失局部更新。</summary>
    [Fact]
    public async Task ConcurrentStores_PreserveDifferentItemsAndUidIsolation()
    {
        var other = new InventoryStore(_catalog, Path.Combine(_root, "records"));
        await Task.WhenAll(_store.ReportAsync(Uid, Source("a"), [Report("甜甜花", 8)]),
            other.ReportAsync(Uid, Source("b"), [Report("摩拉", 100)]),
            other.ReportAsync("100000002", Source("a"), [Report("甜甜花", 99)]));
        Assert.Equal(2, (await _store.LoadAsync(Uid)).Inventory.Items.Count);
        Assert.Equal(99L, (await _store.LoadAsync("100000002")).Inventory.Items["甜甜花"][0].Quantity);
    }

    /// <summary>北京时间凌晨四点前后恰好分属两个记录日。</summary>
    [Fact]
    public void RecordDay_UsesBeijingFourAm()
    {
        Assert.Equal(new DateOnly(2026, 10, 25), InventoryStore.RecordDay(DateTimeOffset.Parse("2026-10-26T03:59:59+08:00")));
        Assert.Equal(new DateOnly(2026, 10, 26), InventoryStore.RecordDay(DateTimeOffset.Parse("2026-10-26T04:00:00+08:00")));
    }

    /// <summary>隔多日运行只生成一份已知状态快照，重复读取不重复备份。</summary>
    [Fact]
    public async Task Snapshot_UsesLastKnownDayAndDoesNotInventGapDays()
    {
        await _store.ReportAsync(Uid, Source("a"), [Report("甜甜花", 8)]);
        var path = Path.Combine(_root, "records", Uid, "inventory.json");
        var document = JsonConvert.DeserializeObject<InventoryDocument>(File.ReadAllText(path))!;
        document.ModifiedAt = DateTimeOffset.UtcNow.AddDays(-12);
        File.WriteAllText(path, JsonConvert.SerializeObject(document));
        await _store.LoadAsync(Uid);
        await _store.LoadAsync(Uid);
        var snapshots = Directory.GetFiles(Path.Combine(_root, "records", Uid, "snapshots"), "*.json");
        Assert.Single(snapshots);
        Assert.Equal(InventoryStore.RecordDay(document.ModifiedAt.Value).ToString("yyyy-MM-dd") + ".json", Path.GetFileName(snapshots[0]));
        var snapshot = JsonConvert.DeserializeObject<InventoryDocument>(File.ReadAllText(snapshots[0]))!;
        Assert.Equal(_time, snapshot.Items["甜甜花"][0].RecordedAt);
    }

    /// <summary>补充字典覆盖内置分类，本地新增和跨 UID 读取一致，删除覆盖后回退内置分类。</summary>
    [Fact]
    public async Task UserDictionary_OverridesBuiltInCategoryForLocalRecords()
    {
        // 摩拉的内置测试分类为货币，预设补充字典覆盖为通用货币。
        await _store.SetLocalQuantityAsync(Uid, "摩拉", 5);
        Assert.Equal("通用货币", (await _store.LoadAsync(Uid)).Definitions["摩拉"]);
        var path = Path.Combine(_root, "records", "item-overrides.json");
        File.WriteAllText(path, """{"摩拉":{"Category":"自定义货币"}}""");
        await _store.SetLocalQuantityAsync("100000002", "摩拉", 6);
        Assert.Equal("自定义货币", (await _store.LoadAsync(Uid)).Definitions["摩拉"]);
        Assert.Equal("自定义货币", (await _store.LoadAsync("100000002")).Definitions["摩拉"]);
        Assert.Equal("货币", _catalog.Items["摩拉"]);
        File.WriteAllText(path, "{}");
        var workspace = await _store.LoadAsync(Uid);
        Assert.Equal("货币", workspace.Definitions["摩拉"]);
        Assert.Equal(5, workspace.Inventory.Items["摩拉"][0].Quantity);
    }

    /// <summary>用户字典补充独立保存，不改公共 CSV；数量编辑入口仍不直接改写内置分类。</summary>
    [Fact]
    public async Task UserDefinition_AllowsExplicitAdditionButCannotReplaceBuiltInCategory()
    {
        await _store.DefineAsync("甜甜花", "烹饪材料");
        await _store.DefineAsync("测试新材料", "测试分类");
        await _store.SetLocalQuantityAsync(Uid, "测试新材料", 5);
        var workspace = await _store.LoadAsync(Uid);
        Assert.Equal("烹饪材料", workspace.Definitions["甜甜花"]);
        Assert.Equal("", _catalog.Items["甜甜花"]);
        Assert.Equal("AccountRecords.SetLocalQuantityAsync", workspace.Inventory.Items["测试新材料"][0].Source);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.DefineAsync("摩拉", "其他"));
    }

    /// <summary>坏 JSON 从上一份有效备份恢复，而不是创建空库存。</summary>
    [Fact]
    public async Task CorruptJson_RecoversPreviousAtomicSave()
    {
        await _store.ReportAsync(Uid, Source("a"), [Report("甜甜花", 8)]);
        await _store.ReportAsync(Uid, Source("a"), [Report("甜甜花", 9, 1)]);
        File.WriteAllText(Path.Combine(_root, "records", Uid, "inventory.json"), "{broken");
        Assert.Equal(8L, (await _store.LoadAsync(Uid)).Inventory.Items["甜甜花"][0].Quantity);
    }

    /// <summary>可编辑字典语法错误保留原文，不静默覆盖为旧备份。</summary>
    [Theory]
    [InlineData("item-overrides.json")]
    [InlineData("sources.json")]
    public async Task Dictionary_InvalidJsonPreservesEditedFile(string file)
    {
        var path = Path.Combine(_root, "records", file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{broken");
        File.WriteAllText(path + ".bak", "{}");
        await Assert.ThrowsAsync<InvalidDataException>(() => _store.LoadAsync(Uid));
        Assert.Equal("{broken", File.ReadAllText(path));
        File.WriteAllText(path, "{}");
        await _store.LoadAsync(Uid);
    }

    /// <summary>各上报路径使用相同来源回退规则，重建服务后仍能读取。</summary>
    [Fact]
    public async Task Sources_EmptyDisplayNamesFallBackAcrossReportPaths()
    {
        await _store.ReportAsync(Uid, new AccountRecordSource { Reference = "scan", DisplayName = " " }, [Report("摩拉", 80)]);
        await _store.ReportDeltaAsync(Uid, new AccountRecordSource { Reference = "delta", DisplayName = "" },
            [new InventoryDeltaReport { Name = "摩拉", Delta = 1, RecordedAt = _time.AddMinutes(1).ToString("O") }]);
        await _store.ReportFailureAsync(Uid, new AccountRecordSource { Reference = "failure", DisplayName = " " }, "摩拉", "测试失败");
        var reloaded = await new InventoryStore(_catalog, Path.Combine(_root, "records")).LoadAsync(Uid);
        foreach (var name in new[] { "scan", "delta", "failure" }) Assert.Equal(name, reloaded.Sources[name].DisplayName);
        await Assert.ThrowsAsync<ArgumentException>(async () => await _store.ReportAsync(Uid,
            new AccountRecordSource { Reference = "bad", DisplayName = new string('x', 1025) }, [Report("摩拉", 81, 2)]));
        Assert.False((await _store.LoadAsync(Uid)).Sources.ContainsKey("bad"));
    }

    /// <summary>无基准增量拒绝并保留事件；不得建立虚假的确认库存。</summary>
    [Fact]
    public async Task Delta_RequiresBaselineAndDeduplicatesTimestamp()
    {
        var delta = new InventoryDeltaReport { Name = "摩拉", Delta = -3, RecordedAt = _time.AddMinutes(1).ToString("O") };
        var rejected = await _store.ReportDeltaAsync(Uid, Source("delta"), [delta]);
        Assert.Single(rejected.Rejected);
        var data = (await _store.LoadAsync(Uid)).Inventory;
        Assert.Empty(data.Items);
        Assert.Equal("Error", Assert.Single(data.Events).State);
        await _store.ReportDeltaAsync(Uid, Source("delta"), [delta]);
        Assert.Single((await _store.LoadAsync(Uid)).Inventory.Events);
    }

    /// <summary>归档不解除去重；删除物品后可用同一输入重建，旧证据仍保留。</summary>
    [Fact]
    public async Task Delta_CanBeReimportedAfterItemDeletion()
    {
        var delta = new InventoryDeltaReport { Name = "摩拉", Delta = 3, RecordedAt = _time.AddMinutes(1).ToString("O") };
        await _store.ReportDeltaAsync(Uid, Source("delta"), [delta]);
        var original = Assert.Single((await _store.LoadAsync(Uid)).Inventory.Events);
        await _store.UpdateEventAsync(Uid, original.Id, false, true);
        await _store.ReportDeltaAsync(Uid, Source("delta"), [delta]);
        Assert.Single((await _store.LoadAsync(Uid)).Inventory.Events);
        await _store.DeleteItemAsync(Uid, "摩拉");
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80)]);
        var result = await _store.ReportDeltaAsync(Uid, Source("delta"), [delta]);
        Assert.Single(result.Accepted);
        var events = (await _store.LoadAsync(Uid)).Inventory.Events.Where(item => item.Kind == "Delta").ToList();
        Assert.Equal(2, events.Count);
        Assert.True(events[0].ItemDeleted);
        Assert.True(events[0].Archived);
        Assert.Equal("Pending", events[1].State);
        Assert.False(events[1].ItemDeleted);
        await _store.ReportDeltaAsync(Uid, Source("delta"), [delta]);
        Assert.Equal(2, (await _store.LoadAsync(Uid)).Inventory.Events.Count(item => item.Kind == "Delta"));
    }
    /// <summary>累加不改确认值，净零仍待核实；重复输入只接受一次。</summary>
    [Fact]
    public async Task Delta_PreservesConfirmedQuantityAndNetZeroRemainsPending()
    {
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80)]);
        var first = new InventoryDeltaReport { Name = "摩拉", Delta = 3, RecordedAt = _time.AddMinutes(1).ToString("O") };
        await _store.ReportDeltaAsync(Uid, Source("delta"), [first, first,
            new InventoryDeltaReport { Name = "摩拉", Delta = -3, RecordedAt = _time.AddMinutes(2).ToString("O") }]);
        var data = (await _store.LoadAsync(Uid)).Inventory;
        Assert.Equal(80L, data.Items["摩拉"][0].Quantity);
        Assert.Equal(2, data.Events.Count(item => item.Kind == "Delta"));
        Assert.All(data.Events.Where(item => item.Kind == "Delta"), item => Assert.Equal("Pending", item.State));
        Assert.Equal(0L, data.Events.Where(item => item.Kind == "Delta").Sum(item => item.Quantity));
    }

    /// <summary>仅凭记录时间拒绝旧库存并保留增量；新记录无需额外核实时间即可核实。</summary>
    [Fact]
    public async Task Verification_RejectsOldScanThenArchivesOnConfirmation()
    {
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80)]);
        await _store.ReportDeltaAsync(Uid, Source("delta"), [new InventoryDeltaReport
        { Name = "摩拉", Delta = -3, RecordedAt = _time.AddMinutes(2).ToString("O") }]);
        var old = Report("摩拉", 80, 1);
        Assert.Single((await _store.ReportAsync(Uid, Source("scan"), [old])).Rejected);
        var pending = (await _store.LoadAsync(Uid)).Inventory;
        Assert.Equal(80L, pending.Items["摩拉"][0].Quantity);
        Assert.Contains(pending.Events, item => item.Kind == "Delta" && item.State == "Pending");
        var fresh = Report("摩拉", 76, 4);
        await _store.ReportAsync(Uid, Source("scan"), [fresh]);
        var workspace = await _store.LoadAsync(Uid);
        Assert.Equal("Completed", workspace.Inventory.Events.Last(item => item.Source == "scan").State);
        var verified = Assert.Single(workspace.Inventory.Events.Where(item => item.Kind == "Delta"));
        Assert.Equal("Completed", verified.State);
        Assert.False(verified.VerifiedCorrect);
        Assert.Contains(verified.History, line => line.Contains("VerifiedCorrect=False"));
        await _store.UpdateEventAsync(Uid, verified.Id, true, false);
        Assert.True((await _store.LoadAsync(Uid)).Inventory.Events.Single(item => item.Id == verified.Id).KeepNotice);
        await _store.UpdateEventAsync(Uid, verified.Id, false, true);
        Assert.True((await _store.LoadAsync(Uid)).Inventory.Events.Single(item => item.Id == verified.Id).Archived);
    }

    /// <summary>缺少业务时间不能使用接收时间代替，已有库存及待核实增量保持不变。</summary>
    [Fact]
    public async Task Verification_MissingRecordedAtDoesNotVerifyPending()
    {
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80)]);
        await _store.ReportDeltaAsync(Uid, Source("delta"), [new()
        { Name = "摩拉", Delta = 5, RecordedAt = _time.AddMinutes(1).ToString("O") }]);
        var report = Report("摩拉", 85);
        report.RecordedAt = null;
        var result = await _store.ReportAsync(Uid, Source("scan"), [report]);
        Assert.Contains("缺少记录时间", result.Rejected["摩拉"]);
        var data = (await _store.ReadAsync(Uid)).Inventory;
        Assert.Equal(80, data.Items["摩拉"][0].Quantity);
        Assert.Equal("Pending", data.Events.Single(item => item.Kind == "Delta").State);
    }

    /// <summary>严格拒绝双时间字段，包括显式 null，不改变库存或待核实增量。</summary>
    [Fact]
    public async Task Exchange_RejectsRemovedVerificationTime()
    {
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80)]);
        var package = await _store.ExportInventoryAsync(Uid);
        var record = (JObject)package["accounts"]![Uid]!["inventory"]!["摩拉"]![0]!;
        record["recordedAt"] = _time.AddMinutes(3).ToString("O");
        record["verifiedAt"] = _time.AddMinutes(1).ToString("O");
        await _store.ReportDeltaAsync(Uid, Source("delta"), [new()
        { Name = "摩拉", Delta = 5, RecordedAt = _time.AddMinutes(2).ToString("O") }]);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => _store.ImportInventoryAsync(package, Uid, false));
        Assert.Contains("不支持 verifiedAt", error.Message);
        Assert.Equal("Pending", (await _store.ReadAsync(Uid)).Inventory.Events.Single(item => item.Kind == "Delta").State);
        Assert.NotNull(record["verifiedAt"]);
        record["verifiedAt"] = JValue.CreateNull();
        await Assert.ThrowsAsync<InvalidDataException>(() => _store.ImportInventoryAsync(package, Uid, false));
        Assert.Equal(80, (await _store.ReadAsync(Uid)).Inventory.Items["摩拉"][0].Quantity);
    }

    /// <summary>保留扫描失败证据和重复批次去重，但不再生成来源错误提醒。</summary>
    [Fact]
    public async Task Verification_RepeatedFailuresDoNotCreateSourceWarnings()
    {
        for (var i = 0; i < 5; i++)
            await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", -1, i)]);
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", -1, 4)]);
        Assert.Equal(5, (await _store.LoadAsync(Uid)).Inventory.Events.Count(item => item.Source == "scan"));
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80, 5)]);
        var workspace = await _store.LoadAsync(Uid);
        Assert.DoesNotContain(workspace.Inventory.Events, item => item.Kind == "SourceWarning");
        Assert.Equal(5, workspace.Inventory.Events.Count(item => item.Source == "scan" && item.State == "Error"));
    }

    /// <summary>旧规则文件的来源字段失效，新规则文件只包含物品规则。</summary>
    [Fact]
    public async Task WarningRules_LegacySourceFieldsAreIgnored()
    {
        var path = await _store.GetWarningRulesPathAsync();
        var preset = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path));
        Assert.Single(preset.Properties());
        Assert.NotNull(preset["InventoryRules"]);
        File.WriteAllText(path, """{"Enabled":true,"Source":"*","WindowSize":3,"ErrorThreshold":1,"Message":"旧来源提醒","InventoryRules":[]}""");
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", -1)]);
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80, 1)]);
        Assert.DoesNotContain((await _store.LoadAsync(Uid)).Inventory.Events, item => item.Kind == "SourceWarning");
    }

    /// <summary>旧来源预警自动归档，物品规则仍独立触发，不受旧顶层开关影响。</summary>
    [Fact]
    public async Task WarningRules_ArchiveLegacyWarningsAndKeepItemRules()
    {
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80)]);
        var inventoryPath = Path.Combine(_root, "records", Uid, "inventory.json");
        var document = JObject.Parse(File.ReadAllText(inventoryPath));
        ((JArray)document["Events"]!).Add(JObject.FromObject(new InventoryEvent
        {
            Id = "legacy-warning", Name = "摩拉", Source = "scan", Kind = "SourceWarning",
            State = "Warning", Message = "旧来源提醒", History = ["原始证据"]
        }));
        File.WriteAllText(inventoryPath, document.ToString());
        var path = await _store.GetWarningRulesPathAsync();
        File.WriteAllText(path, """{"Enabled":false,"InventoryRules":[{"Id":"low-mora","Enabled":true,"Name":"摩拉","Kind":"QuantityBelow","Threshold":100,"Message":"摩拉不足"}]}""");

        var workspace = await _store.LoadAsync(Uid);
        var legacy = Assert.Single(workspace.Inventory.Events.Where(item => item.Id == "legacy-warning"));
        Assert.True(legacy.Archived);
        Assert.Contains("原始证据", legacy.History);
        Assert.Contains(workspace.Inventory.Events, item => item.Kind == "ThresholdWarning" && !item.Archived && item.Message == "摩拉不足");
        Assert.DoesNotContain((await _store.LoadAsync(Uid)).Inventory.Events,
            item => item.Kind == "SourceWarning" && !item.Archived);
    }

    /// <summary>全部重置覆盖自定义字典、来源、规则、设置及多个 UID，原数据保留独立备份。</summary>
    [Fact]
    public async Task ResetToDefaults_ReplacesAllAccountsAndSharedSettings()
    {
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80)]);
        await _store.AddUidAsync("100000002");
        await _store.DefineAsync("自定义物品", "自定义分类");
        await _store.ConfigureAsync(new AccountRecordsSettings { RecentLimit = 9, ShowExpirationColumn = true });
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", -1, 1)]);
        var snapshotDirectory = Path.Combine(_root, "records", Uid, "snapshots");
        Directory.CreateDirectory(snapshotDirectory);
        File.WriteAllText(Path.Combine(snapshotDirectory, "old.json"), "{}");
        var originalInventory = File.ReadAllText(Path.Combine(_root, "records", Uid, "inventory.json"));
        var rulesPath = await _store.GetWarningRulesPathAsync();
        File.WriteAllText(rulesPath, """{"InventoryRules":[]}""");
        await _store.ResetToDefaultsAsync();

        Assert.Empty(await _store.ListUidsAsync());
        var settings = await _store.GetSettingsAsync();
        Assert.Equal(5, settings.RecentLimit);
        Assert.False(settings.ShowExpirationColumn);
        var workspace = await _store.LoadAsync(Uid);
        Assert.Empty(workspace.Inventory.Items);
        Assert.Empty(workspace.Inventory.Events);
        Assert.Empty(workspace.Inventory.Errors);
        var presetSource = Assert.Single(workspace.Sources).Value;
        Assert.Equal("AccountRecords.SetLocalQuantityAsync", presetSource.Reference);
        Assert.DoesNotContain("Attempts", JsonConvert.SerializeObject(presetSource));
        Assert.DoesNotContain("自定义物品", workspace.Definitions.Keys);
        Assert.Contains("须臾树脂", workspace.Definitions.Keys);
        var rules = JsonConvert.DeserializeObject<InventoryWarningRule>(File.ReadAllText(rulesPath))!;
        Assert.False(Assert.Single(rules.InventoryRules).Enabled);
        var backup = Assert.Single(Directory.GetDirectories(_root, "records.backup-*"));
        Assert.True(File.Exists(Path.Combine(backup, Uid, "inventory.json")));
        Assert.Equal(originalInventory, File.ReadAllText(Path.Combine(backup, Uid, "inventory.json")));
        Assert.True(File.Exists(Path.Combine(backup, Uid, "snapshots", "old.json")));
        Assert.False(File.Exists(Path.Combine(_root, "records", Uid, "snapshots", "old.json")));
        Assert.True(Directory.Exists(Path.Combine(backup, "100000002")));

        // 重置后的数据根仍能接收新账号上报，不会从备份恢复自定义内容。
        await _store.ReportAsync("100000003", Source("new-scan"), [Report("摩拉", 12)]);
        var fresh = await _store.LoadAsync("100000003");
        Assert.Equal(12, fresh.Inventory.Items["摩拉"][0].Quantity);
        Assert.DoesNotContain("scan", fresh.Sources.Keys);
        Assert.DoesNotContain("自定义物品", fresh.Definitions.Keys);
    }
    /// <summary>置顶按 UID 隔离，跨实例保存，上报不覆盖，取消和删除均清除标记。</summary>
    [Fact]
    public async Task Prior_PersistsPerUidAndSurvivesReports()
    {
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80)]);
        await _store.ReportAsync("100000002", Source("scan"), [Report("摩拉", 90)]);
        await _store.SetPriorAsync(Uid, "摩拉", true);
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 81, 1)]);
        var reopened = new InventoryStore(_catalog, Path.Combine(_root, "records"));
        Assert.Contains("摩拉", (await reopened.LoadAsync(Uid)).Inventory.Prior);
        Assert.Empty((await reopened.LoadAsync("100000002")).Inventory.Prior);
        await reopened.SetPriorAsync(Uid, "摩拉", false);
        Assert.Empty((await reopened.LoadAsync(Uid)).Inventory.Prior);
        await reopened.SetPriorAsync(Uid, "摩拉", true);
        await reopened.DeleteItemAsync(Uid, "摩拉");
        Assert.Empty((await reopened.LoadAsync(Uid)).Inventory.Prior);
        await Assert.ThrowsAsync<InvalidOperationException>(() => reopened.SetPriorAsync(Uid, "摩拉", true));
    }

    /// <summary>人工反馈修改原上报结果，不制造新一次核实统计；人工来源不能上报增量。</summary>
    [Fact]
    public async Task ManualFeedback_MarksOriginalAttemptOnly()
    {
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80)]);
        var attemptId = (await _store.LoadAsync(Uid)).Inventory.Items["摩拉"][0].AttemptId;
        await _store.SetLocalQuantityAsync(Uid, "摩拉", 79, attemptId);
        var source = (await _store.LoadAsync(Uid)).Sources["scan"];
        Assert.DoesNotContain("Attempts", JsonConvert.SerializeObject(source));
        Assert.Contains((await _store.LoadAsync(Uid)).Inventory.Events, item => item.AttemptId == attemptId && item.State == "Error");
        Assert.Throws<ArgumentException>(() => { _ = _store.ReportDeltaAsync(Uid, Source("AccountRecords.SetLocalQuantityAsync"),
            [new InventoryDeltaReport { Name = "摩拉", Delta = 1, RecordedAt = _time.ToString("O") }]); });
    }

    /// <summary>开发增量导入必须先确认，不能因为文件有基准就绕过授权。</summary>
    [Fact]
    public async Task Exchange_DeveloperDeltasRequireExplicitConfirmation()
    {
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80)]);
        var package = await _store.ExportInventoryAsync(Uid);
        package["accounts"]![Uid]!["deltas"] = new Newtonsoft.Json.Linq.JArray(new Newtonsoft.Json.Linq.JObject
        { ["name"] = "摩拉", ["delta"] = -3, ["recordedAt"] = _time.AddMinutes(1).ToString("O"), ["source"] = "scan" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.ImportInventoryAsync(package, "100000002", false));
        Assert.DoesNotContain("100000002", await _store.ListUidsAsync());
        await _store.ImportInventoryAsync(package, "100000002", false, true);
        Assert.Contains((await _store.LoadAsync("100000002")).Inventory.Events, item => item.Kind == "Delta" && item.State == "Pending");
    }

    /// <summary>仅删除本测试创建的目录。</summary>
    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
    /// <summary>同一失败输入只保留一个事件和异常凭证。</summary>
    [Fact]
    public async Task RepeatedFailure_PreservesEventAndErrorId()
    {
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", -1)]);
        var first = (await _store.LoadAsync(Uid)).Inventory;
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", -1)]);
        var next = (await _store.LoadAsync(Uid)).Inventory;
        Assert.Equal(first.Errors["摩拉"].Id, next.Errors["摩拉"].Id);
        var item = Assert.Single(next.Events);
        Assert.Equal(2L, item.RepeatCount);
        Assert.Contains("EventCreated State=Error", item.History[0]);
        Assert.Contains("Quantity must be non-negative", item.History[0]);
    }

    /// <summary>归档保存原始说明，并将历史移出主库存文件。</summary>
    [Fact]
    public async Task Archive_PreservesMessageAndTrace()
    {
        await _store.ReportDeltaAsync(Uid, Source("delta"), [new InventoryDeltaReport { Name = "摩拉", Delta = 1, RecordedAt = _time.ToString("O") }]);
        var item = Assert.Single((await _store.LoadAsync(Uid)).Inventory.Events);
        await _store.UpdateEventAsync(Uid, item.Id, false, true);
        var archived = Assert.Single((await _store.LoadAsync(Uid)).Inventory.Events);
        Assert.Equal(item.Message, archived.Message);
        Assert.True(archived.Archived);
        var raw = JsonConvert.DeserializeObject<InventoryDocument>(File.ReadAllText(Path.Combine(_root, "records", Uid, "inventory.json")))!;
        Assert.Empty(raw.Events);
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "records", Uid, "events"), "*.json"));
    }

    /// <summary>字典中合法 JSON 的空条目应报字段错误，不出现空引用。</summary>
    [Fact]
    public async Task Dictionary_NullEntryReportsFileAndField()
    {
        var directory = Path.Combine(_root, "records");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "item-overrides.json"), "{\"测试物品\":null}");
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => _store.LoadAsync(Uid));
        Assert.Contains("item-overrides.json", error.Message);
        Assert.Contains("测试物品", error.Message);
    }

    /// <summary>增量不按来源分组打乱时间，先增加再扣除允许成功。</summary>
    [Fact]
    public async Task Exchange_DeltasFollowTimeAcrossSources()
    {
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 0)]);
        var package = await _store.ExportInventoryAsync(Uid);
        package["sources"]!["spend"] = Newtonsoft.Json.Linq.JObject.FromObject(new { reference = "spend", displayName = "spend" });
        package["sources"]!["gain"] = Newtonsoft.Json.Linq.JObject.FromObject(new { reference = "gain", displayName = "gain" });
        package["accounts"]![Uid]!["deltas"] = new Newtonsoft.Json.Linq.JArray(
            Newtonsoft.Json.Linq.JObject.FromObject(new { name = "摩拉", delta = -5, source = "spend", recordedAt = _time.AddMinutes(2).ToString("O") }),
            Newtonsoft.Json.Linq.JObject.FromObject(new { name = "摩拉", delta = 10, source = "gain", recordedAt = _time.AddMinutes(1).ToString("O") }));
        var result = await _store.ImportInventoryAsync(package, Uid, false, true);
        Assert.Empty(result.Rejected);
        Assert.Equal(5L, (await _store.LoadAsync(Uid)).Inventory.Events.Where(item => item.Kind == "Delta").Sum(item => item.Quantity));
    }

    /// <summary>UID 严格校验位数，不保留短编号例外。</summary>
    [Theory]
    [InlineData("123456789", true)]
    [InlineData("1800000000", true)]
    [InlineData("100000001", true)]
    [InlineData("100000002", true)]
    [InlineData("00000", false)]
    [InlineData("00001", false)]
    [InlineData("12345678", false)]
    [InlineData("12345678901", false)]
    [InlineData("012345678", false)]
    [InlineData("123456789\n", false)]
    public void Uid_ValidatesLength(string uid, bool valid) => Assert.Equal(valid, InventoryStore.IsValidUid(uid));
    /// <summary>来源集合显式空值不能进入业务逻辑。</summary>
    [Fact]
    public async Task Dictionary_NullAttemptsReportsField()
    {
        var directory = Path.Combine(_root, "records");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "sources.json"), "{\"scan\":{\"Reference\":\"scan\",\"DisplayName\":\"scan\",\"Attempts\":null}}");
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => _store.LoadAsync(Uid));
        Assert.Contains("Attempts", error.Message);
    }
    /// <summary>保留上限只清理归档历史，并保留增量去重及活动事件。</summary>
    [Fact]
    public async Task ArchiveRetention_PreservesPendingAndDeduplication()
    {
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80)]);
        var path = Path.Combine(_root, "records", Uid, "inventory.json");
        var data = JsonConvert.DeserializeObject<InventoryDocument>(File.ReadAllText(path))!;
        for (var index = 0; index < 1001; index++)
            data.Events.Add(new InventoryEvent { Name = "摩拉", Kind = "Delta", Source = "delta", Quantity = 1,
                State = "Completed", Archived = true, RecordedAt = _time.AddSeconds(index + 1), EndedAt = _time.AddMinutes(index + 1) });
        data.Events.Add(new InventoryEvent { Name = "甜甜花", State = "Pending", Message = "pending" });
        File.WriteAllText(path, JsonConvert.SerializeObject(data));
        await _store.ConfigureAsync(new AccountRecordsSettings { EventLimit = 1000 });
        var loaded = (await _store.LoadAsync(Uid)).Inventory;
        Assert.Equal(999, loaded.Events.Count(item => item.Archived));
        Assert.Single(loaded.Events.Where(item => !item.Archived));
        var result = await _store.ReportDeltaAsync(Uid, Source("delta"),
            [new InventoryDeltaReport { Name = "摩拉", Delta = 1, RecordedAt = _time.AddSeconds(1).ToString("O") }]);
        Assert.Empty(result.Accepted);
        Assert.Empty(result.Rejected);
        await _store.DeleteItemAsync(Uid, "摩拉");
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80)]);
        result = await _store.ReportDeltaAsync(Uid, Source("delta"),
            [new InventoryDeltaReport { Name = "摩拉", Delta = 1, RecordedAt = _time.AddSeconds(1).ToString("O") }]);
        Assert.Single(result.Accepted);
    }
    /// <summary>模拟磁盘库存到期后直接读取，即使自定义规则损坏也创建一次 Pending，不扣数量。</summary>
    [Fact]
    public async Task ExpirationCheck_LoadDetectsExpiredStockWithoutDuplicateEvents()
    {
        var report = Report("须臾树脂", 2);
        report.Expirations = [new() { Quantity = 1, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1).ToString("O") },
            new() { Quantity = 1, ExpiresAt = DateTimeOffset.UtcNow.AddDays(3).ToString("O") }];
        await _store.ReportAsync(Uid, Source("scan"), [report]);
        var data = (await _store.LoadAsync(Uid)).Inventory;
        Assert.DoesNotContain(data.Events, item => item.Kind == "ExpirationCheck");

        // 仅改变持久化批次的到期时刻来模拟时间流逝，不再次上报，也不等待真实时钟。
        data.Items["须臾树脂"][0].Expirations![0].ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O");
        File.WriteAllText(Path.Combine(_root, "records", Uid, "inventory.json"), JsonConvert.SerializeObject(data));
        File.WriteAllText(await _store.GetWarningRulesPathAsync(), "{broken");
        var workspace = await _store.LoadAsync(Uid);
        Assert.NotNull(workspace.WarningRuleError);
        var pending = Assert.Single(workspace.Inventory.Events.Where(item => item.Kind == "ExpirationCheck"));
        Assert.Equal("Pending", pending.State);
        Assert.False(pending.Archived);
        Assert.Equal(2, workspace.Inventory.Items["须臾树脂"][0].Quantity);
        var repeated = Assert.Single((await _store.LoadAsync(Uid)).Inventory.Events.Where(item => item.Kind == "ExpirationCheck"));
        Assert.Equal(pending.Id, repeated.Id);
        Assert.Equal(pending.History.Count, repeated.History.Count);
        await _store.UpdateEventAsync(Uid, pending.Id, false, true);
        Assert.False((await _store.LoadAsync(Uid)).Inventory.Events.Single(item => item.Id == pending.Id).Archived);
    }

    /// <summary>未知到期信息不解除提醒；明确更新为未过期库存或人工检查后结束，再次过期可新建提醒。</summary>
    [Fact]
    public async Task ExpirationCheck_ResolvesAfterFreshBatchesOrManualCorrection()
    {
        var expired = Report("须臾树脂", 2);
        expired.Expirations = [new() { Quantity = 2, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O") }];
        await _store.ReportAsync(Uid, Source("scan"), [expired]);
        var first = Assert.Single((await _store.LoadAsync(Uid)).Inventory.Events.Where(item => item.Kind == "ExpirationCheck"));
        await _store.ReportAsync(Uid, Source("scan"), [Report("须臾树脂", 2, 1)]);
        Assert.False((await _store.LoadAsync(Uid)).Inventory.Events.Single(item => item.Id == first.Id).Archived);
        var fresh = Report("须臾树脂", 2, 2);
        fresh.Expirations = [new() { Quantity = 2, ExpiresAt = DateTimeOffset.UtcNow.AddDays(2).ToString("O") }];
        await _store.ReportAsync(Uid, Source("scan"), [fresh]);
        var resolved = (await _store.LoadAsync(Uid)).Inventory.Events.Single(item => item.Id == first.Id);
        Assert.Equal("Completed", resolved.State);
        Assert.True(resolved.Archived);
        expired.RecordedAt = _time.AddMinutes(3).ToString("O");
        await _store.ReportAsync(Uid, Source("scan"), [expired]);
        var second = Assert.Single((await _store.LoadAsync(Uid)).Inventory.Events.Where(item => item.Kind == "ExpirationCheck" && !item.Archived));
        Assert.NotEqual(first.Id, second.Id);
        await _store.SetLocalQuantityAsync(Uid, "须臾树脂", 1);
        var final = await _store.LoadAsync(Uid);
        Assert.DoesNotContain(final.Inventory.Events, item => item.Kind == "ExpirationCheck" && !item.Archived);
        Assert.Equal("Aborted", final.Inventory.Events.Single(item => item.Id == second.Id).State);
        Assert.Equal("Completed", final.Inventory.Events.Single(item => item.Id == first.Id).State);
        Assert.Equal(1, final.Inventory.Items["须臾树脂"][0].Quantity);
    }

    /// <summary>连续消耗按到期先后推算，跨批扣减不修改确认库存或原始批次。</summary>
    [Fact]
    public async Task DeltaExpirations_ConsumesEarliestFirstWithoutChangingBaseline()
    {
        var early = DateTimeOffset.UtcNow.AddHours(6).ToString("O");
        var late = DateTimeOffset.UtcNow.AddDays(3).ToString("O");
        var report = Report("须臾树脂", 4);
        report.Expirations = [new() { Quantity = 2, ExpiresAt = late }, new() { Quantity = 2, ExpiresAt = early }];
        await _store.ReportAsync(Uid, Source("scan"), [report]);
        await _store.ReportDeltaAsync(Uid, Source("consume"), [new() { Name = "须臾树脂", Delta = -1, RecordedAt = _time.AddMinutes(1).ToString("O") }]);
        var data = (await _store.LoadAsync(Uid)).Inventory;
        var estimated = InventoryStore.EstimateExpirations(data.Items["须臾树脂"][0], data.Events)!;
        Assert.Equal(new long[] { 1, 2 }, estimated.Select(item => item.Quantity));
        Assert.Equal(early, estimated[0].ExpiresAt);
        await _store.ReportDeltaAsync(Uid, Source("consume"), [new() { Name = "须臾树脂", Delta = -2, RecordedAt = _time.AddMinutes(2).ToString("O") }]);
        data = (await _store.LoadAsync(Uid)).Inventory;
        var remaining = Assert.Single(InventoryStore.EstimateExpirations(data.Items["须臾树脂"][0], data.Events)!);
        Assert.Equal(1, remaining.Quantity);
        Assert.Equal(late, remaining.ExpiresAt);
        Assert.Equal(4, data.Items["须臾树脂"][0].Quantity);
        Assert.All(data.Items["须臾树脂"][0].Expirations!, batch => Assert.Equal(2, batch.Quantity));
    }

    /// <summary>迟到消耗不能借用未来入库；拒绝后批次推算仍与数量一致。</summary>
    [Fact]
    public async Task DeltaExpirations_RejectsHistoricalUnderflow()
    {
        var report = Report("须臾树脂", 2);
        report.Expirations = [new() { Quantity = 2, ExpiresAt = _time.AddDays(3).ToString("O") }];
        await _store.ReportAsync(Uid, Source("scan"), [report]);
        await _store.ReportDeltaAsync(Uid, Source("gain"), [new()
        {
            Name = "须臾树脂", Delta = 2, RecordedAt = _time.AddMinutes(2).ToString("O"),
            Expirations = [new() { Quantity = 2, ExpiresAt = _time.AddDays(4).ToString("O") }]
        }]);
        var invalid = await _store.ReportDeltaAsync(Uid, Source("late-consume"), [new()
        { Name = "须臾树脂", Delta = -3, RecordedAt = _time.AddMinutes(1).ToString("O") }]);
        Assert.Contains("有效库存范围", invalid.Rejected["须臾树脂"]);
        var valid = await _store.ReportDeltaAsync(Uid, Source("valid-late-consume"), [new()
        { Name = "须臾树脂", Delta = -1, RecordedAt = _time.AddMinutes(1).ToString("O") }]);
        Assert.Single(valid.Accepted);
        var data = (await _store.LoadAsync(Uid)).Inventory;
        Assert.Equal(3, InventoryStore.EstimateExpirations(data.Items["须臾树脂"][0], data.Events)!.Sum(item => item.Quantity));
        Assert.Equal(3, 2 + data.Events.Where(item => item.Kind == "Delta" && item.State == "Pending").Sum(item => item.Quantity));
    }

    /// <summary>历史中途溢出同样拒绝，即使最终数量因后续消耗回到合法范围。</summary>
    [Fact]
    public async Task Delta_RejectsHistoricalOverflow()
    {
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", long.MaxValue - 1)]);
        await _store.ReportDeltaAsync(Uid, Source("consume"), [new()
        { Name = "摩拉", Delta = -2, RecordedAt = _time.AddMinutes(2).ToString("O") }]);
        var invalid = await _store.ReportDeltaAsync(Uid, Source("late-gain"), [new()
        { Name = "摩拉", Delta = 2, RecordedAt = _time.AddMinutes(1).ToString("O") }]);
        Assert.Contains("有效库存范围", invalid.Rejected["摩拉"]);
    }

    /// <summary>切换显示偏好或提高上限不重写账号库存、事件和日期快照。</summary>
    [Fact]
    public async Task DisplayPreferences_DoNotRewriteAccountFiles()
    {
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80)]);
        var files = Directory.GetFiles(Path.Combine(_root, "records", Uid), "*", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        var stamp = DateTime.UtcNow.AddDays(-10);
        foreach (var file in files) File.SetLastWriteTimeUtc(file, stamp);
        var previous = files.ToDictionary(file => file, File.GetLastWriteTimeUtc);
        var settings = await _store.GetSettingsAsync();
        settings.ShowExpirationColumn = !settings.ShowExpirationColumn;
        await _store.ConfigureAsync(settings);
        settings.EventLimit = 50000;
        await _store.ConfigureAsync(settings);
        foreach (var file in files) Assert.Equal(previous[file], File.GetLastWriteTimeUtc(file));
    }

    /// <summary>底栏展示后出现新错误，不能通过旧事件目标接受旧库存；舍弃仍处理该物品全部错误。</summary>
    [Fact]
    public async Task Resolve_RejectsStaleDisplayedErrorButAllowsDiscard()
    {
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 10)]);
        await _store.ReportAsync(Uid, Source("old"), [Report("摩拉", 8, -1)]);
        var data = (await _store.LoadAsync(Uid)).Inventory;
        var error = data.Errors["摩拉"];
        Assert.False(error.CanAccept);
        var shown = data.Events.Single(item => item.State == "Error");
        await _store.ReportDeltaAsync(Uid, Source("bad-delta"), [new()
        { Name = "摩拉", Delta = -100, RecordedAt = _time.AddMinutes(1).ToString("O") }]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.ResolveAsync(Uid, "摩拉", error.Id, true, shown.Id));
        Assert.Equal(10, (await _store.LoadAsync(Uid)).Inventory.Items["摩拉"][0].Quantity);
        await _store.ResolveAsync(Uid, "摩拉", error.Id, false, shown.Id);
        Assert.DoesNotContain((await _store.LoadAsync(Uid)).Inventory.Events, item => item.State == "Error" && !item.Archived);
    }

    /// <summary>已有到期信息的新增库存拒绝缺失或不完整批次，导入携带有效批次后参与推算。</summary>
    [Fact]
    public async Task DeltaExpirations_RequiresMatchingBatchesAndImportsThem()
    {
        var report = Report("须臾树脂", 2);
        report.Expirations = [new() { Quantity = 2, ExpiresAt = DateTimeOffset.UtcNow.AddDays(3).ToString("O") }];
        await _store.ReportAsync(Uid, Source("scan"), [report]);
        var missing = await _store.ReportDeltaAsync(Uid, Source("gain"), [new() { Name = "须臾树脂", Delta = 1, RecordedAt = _time.AddMinutes(1).ToString("O") }]);
        Assert.Contains("到期时间", missing.Rejected["须臾树脂"]);
        var incomplete = await _store.ReportDeltaAsync(Uid, Source("gain"), [new()
        {
            Name = "须臾树脂", Delta = 2, RecordedAt = _time.AddMinutes(2).ToString("O"),
            Expirations = [new() { Quantity = 1, ExpiresAt = DateTimeOffset.UtcNow.AddDays(5).ToString("O") }]
        }]);
        Assert.Contains("必须等于", incomplete.Rejected["须臾树脂"]);
        var package = await _store.ExportInventoryAsync(Uid);
        package["accounts"]![Uid]!["inventory"] = new JObject();
        package["itemDefinitions"] = new JObject();
        package["accounts"]![Uid]!["deltas"] = new JArray(new JObject
        {
            ["name"] = "须臾树脂", ["delta"] = 1, ["source"] = "scan", ["recordedAt"] = _time.AddMinutes(3).ToString("O"),
            ["expirations"] = new JArray(new JObject { ["quantity"] = 1, ["expiresAt"] = DateTimeOffset.UtcNow.AddHours(6).ToString("O") })
        });
        Assert.Contains("须臾树脂", (await _store.ImportInventoryAsync(package, Uid, false, true)).Accepted);
        var data = (await _store.LoadAsync(Uid)).Inventory;
        Assert.Equal(3, InventoryStore.EstimateExpirations(data.Items["须臾树脂"][0], data.Events)!.Sum(item => item.Quantity));
        Assert.Equal(2, data.Items["须臾树脂"][0].Quantity);
    }

    /// <summary>重复错误不计为新增；归档后重复不再提示未处理异常。</summary>
    [Fact]
    public async Task DuplicateDeltaError_ReportsExistingAndArchivedSeparately()
    {
        var report = new InventoryDeltaReport { Name = "摩拉", Delta = 1, RecordedAt = _time.ToString("O") };
        await _store.ReportDeltaAsync(Uid, Source("delta"), [report]);
        var repeated = await _store.ReportDeltaAsync(Uid, Source("delta"), [report]);
        Assert.Empty(repeated.Rejected);
        Assert.Equal(1, repeated.DuplicateCount);
        Assert.Equal(1, repeated.DuplicateErrorCount);
        var item = Assert.Single((await _store.LoadAsync(Uid)).Inventory.Events);
        await _store.UpdateEventAsync(Uid, item.Id, false, true);
        repeated = await _store.ReportDeltaAsync(Uid, Source("delta"), [report]);
        Assert.Equal(1, repeated.DuplicateCount);
        Assert.Equal(0, repeated.DuplicateErrorCount);
        Assert.Empty(repeated.Rejected);
    }
}

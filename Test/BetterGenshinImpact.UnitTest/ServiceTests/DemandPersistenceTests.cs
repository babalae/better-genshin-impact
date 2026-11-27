using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.Service.AccountRecords.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.ServiceTests;

/// <summary>校验需求持久化不补造缺失字段，并保持有效备份恢复能力。</summary>
public sealed class DemandPersistenceTests : IDisposable
{
    /// <summary>隔离的测试数据根。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), nameof(DemandPersistenceTests), Guid.NewGuid().ToString("N"));
    /// <summary>测试账号。</summary>
    private const string Uid = "123456789";
    /// <summary>被测需求存储。</summary>
    private readonly InventoryStore _store;
    /// <summary>测试需求文件。</summary>
    private string DemandPath => Path.Combine(_root, "records", Uid, "demands.json");

    /// <summary>创建符合当前格式的测试目录。</summary>
    public DemandPersistenceTests()
    {
        Directory.CreateDirectory(_root);
        var csv = Path.Combine(_root, "items.csv");
        File.WriteAllText(csv, "item_name,material_type,page\n甜甜花,植物,材料\n");
        _store = new InventoryStore(new InventoryCatalog(csv), Path.Combine(_root, "records"));
    }

    /// <summary>当前需求缺少必填字段时必须拒绝，不能静默生成修订标识、时间或人工来源。</summary>
    [Theory]
    [InlineData("Revision")]
    [InlineData("Source")]
    [InlineData("CreatedAt")]
    [InlineData("TargetQuantity")]
    public async Task MissingDemandFieldIsRejected(string field)
    {
        await _store.SetDemandAsync(Uid, "甜甜花", 1, null);
        var document = JObject.Parse(File.ReadAllText(DemandPath));
        ((JObject)document["Items"]!["甜甜花"]!).Remove(field);
        File.WriteAllText(DemandPath, document.ToString());
        await Assert.ThrowsAsync<InvalidDataException>(() => _store.ReadDemandsAsync(Uid));
    }

    /// <summary>存在有效备份时允许只读回退，但不能覆盖损坏的正式文件。</summary>
    [Fact]
    public async Task MissingSourceUsesValidBackupWithoutWritingOnRead()
    {
        await _store.SetDemandAsync(Uid, "甜甜花", 1, null);
        var first = await _store.ReadDemandsAsync(Uid);
        await _store.SetDemandAsync(Uid, "甜甜花", 2, first.Items["甜甜花"].Revision);
        var document = JObject.Parse(File.ReadAllText(DemandPath));
        ((JObject)document["Items"]!["甜甜花"]!).Remove("Source");
        var damaged = document.ToString();
        File.WriteAllText(DemandPath, damaged);
        var recovered = await _store.ReadDemandsAsync(Uid);
        Assert.Equal(first.Items["甜甜花"].Revision, recovered.Items["甜甜花"].Revision);
        Assert.Equal(1, recovered.Items["甜甜花"].TargetQuantity);
        Assert.Equal(damaged, File.ReadAllText(DemandPath));
    }

    /// <summary>拒绝不完整的需求文档以及空来源，而不丢失已有文件。</summary>
    [Theory]
    [InlineData("History")]
    [InlineData("Prior")]
    [InlineData("SchemaVersion")]
    public async Task MissingDocumentFieldIsRejected(string field)
    {
        await _store.SetDemandAsync(Uid, "甜甜花", 1, null);
        var document = JObject.Parse(File.ReadAllText(DemandPath));
        document.Remove(field);
        File.WriteAllText(DemandPath, document.ToString());
        await Assert.ThrowsAsync<InvalidDataException>(() => _store.ReadDemandsAsync(Uid));
    }

    /// <summary>上限内可继续修改现有需求，新需求则拒绝，防止导出后无法重新导入。</summary>
    [Fact]
    public async Task DemandLimitStillAllowsUpdatingExistingTarget()
    {
        await _store.SetDemandAsync(Uid, "甜甜花", 1, null);
        var data = await _store.ReadDemandsAsync(Uid);
        for (var index = 1; index < 5000; index++) data.Items["材料" + index] = new MaterialDemand();
        File.WriteAllText(DemandPath, JsonConvert.SerializeObject(data));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.SetDemandAsync(Uid, "新材料", 1, null));
        await _store.SetDemandAsync(Uid, "甜甜花", 2, data.Items["甜甜花"].Revision);
        var response = await _store.ReportDemandsAsync(Uid, new AccountRecordSource { Reference = "test", DisplayName = "测试" },
            new Dictionary<string, (long Quantity, string? Category, string? Comment)> { ["新材料"] = (1, "材料", null) });
        Assert.Contains("新材料", response.Rejected.Keys);
        Assert.Equal(2, (await _store.ReadDemandsAsync(Uid)).Items["甜甜花"].TargetQuantity);
    }

    /// <summary>当前目录要求 page 表头，具体条目没有背包页值仍可载入。</summary>
    [Fact]
    public void CatalogRequiresCurrentHeadersButAllowsEmptyPage()
    {
        var path = Path.Combine(_root, "catalog.csv");
        File.WriteAllText(path, "item_name,material_type\n甜甜花,植物\n");
        Assert.Throws<InvalidDataException>(() => new InventoryCatalog(path).Items);
        File.WriteAllText(path, "item_name,material_type,page\n甜甜花,植物,\n");
        var catalog = new InventoryCatalog(path);
        Assert.Equal("植物", catalog.Items["甜甜花"]);
        Assert.Equal("", catalog.GetPage("甜甜花"));
    }

    /// <summary>清理本次测试的随机目录。</summary>
    public void Dispose() => Directory.Delete(_root, true);

    /// <summary>置顶接受或拒绝带空白的名称后，持久化需求都必须仍可读取。</summary>
    [Theory]
    [InlineData("甜甜花")]
    [InlineData(" 甜甜花 ")]
    public async Task DemandPrior_InputMustNotPoisonPersistedDocument(string name)
    {
        await _store.SetDemandAsync(Uid, "甜甜花", 5, null);
        var before = File.ReadAllText(DemandPath);
        var writeError = await Record.ExceptionAsync(() => _store.SetDemandPriorAsync(Uid, name, true));
        // 允许规范化，也允许在写入前拒绝；不允许成功写入不可读文档。
        if (writeError != null)
        {
            Assert.IsAssignableFrom<ArgumentException>(writeError);
            Assert.Equal(before, File.ReadAllText(DemandPath));
        }
        var document = await _store.ReadDemandsAsync(Uid);
        Assert.Equal(5, document.Items["甜甜花"].TargetQuantity);
        if (writeError == null) Assert.Contains("甜甜花", document.Prior);
        Assert.DoesNotContain(" 甜甜花 ", document.Prior);
    }

    /// <summary>添加与取消置顶使用同一规范化名称，空白和过长输入不得写入。</summary>
    [Fact]
    public async Task DemandPrior_NormalizesRemovalAndRejectsInvalidNames()
    {
        await _store.SetDemandAsync(Uid, "甜甜花", 5, null);
        await _store.SetDemandPriorAsync(Uid, " 甜甜花 ", true);
        await _store.SetDemandPriorAsync(Uid, " 甜甜花 ", false);
        Assert.Empty((await _store.ReadDemandsAsync(Uid)).Prior);
        var path = DemandPath;
        var before = File.ReadAllText(path);
        Assert.NotNull(await Record.ExceptionAsync(() => _store.SetDemandPriorAsync(Uid, "   ", true)));
        Assert.NotNull(await Record.ExceptionAsync(() => _store.SetDemandPriorAsync(Uid, new string('花', 129), true)));
        Assert.Equal(before, File.ReadAllText(path));
    }

}

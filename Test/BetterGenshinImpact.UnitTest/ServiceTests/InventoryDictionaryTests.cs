using BetterGenshinImpact.Service.AccountRecords;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.ServiceTests;

/// <summary>补充物品字典的字段校验与背包页读取，不访问实际用户数据。</summary>
public sealed class InventoryDictionaryTests : IDisposable
{
    /// <summary>用例独占的临时目录。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), nameof(InventoryDictionaryTests), Guid.NewGuid().ToString("N"));
    /// <summary>测试账号。</summary>
    private const string Uid = "123456789";
    /// <summary>隔离的存储目录。</summary>
    private string RecordsRoot => Path.Combine(_root, "records");
    /// <summary>被测库存服务。</summary>
    private readonly InventoryStore _store;

    /// <summary>创建没有默认背包页的测试物品目录。</summary>
    public InventoryDictionaryTests()
    {
        Directory.CreateDirectory(_root);
        var csv = Path.Combine(_root, "items.csv");
        File.WriteAllText(csv, "item_name,material_type,page\n甜甜花,植物,\n");
        _store = new InventoryStore(new InventoryCatalog(csv), RecordsRoot);
    }

    /// <summary>大小写不同的补充字段仍须遵守类型、长度和重复字段约束。</summary>
    [Theory]
    [InlineData("long-page")]
    [InlineData("invalid-comment")]
    [InlineData("duplicate-page")]
    public async Task Dictionary_RejectsInvalidCaseInsensitiveFields(string scenario)
    {
        await _store.SetDemandAsync(Uid, "甜甜花", 5, null);
        var item = new JObject { ["Category"] = "植物" };
        if (scenario == "long-page") item["page"] = new string('页', 129);
        if (scenario == "invalid-comment") item["comment"] = 123;
        if (scenario == "duplicate-page") { item["Page"] = "材料"; item["page"] = "食物"; }
        var path = Path.Combine(RecordsRoot, "item-overrides.json");
        var text = new JObject { ["甜甜花"] = item }.ToString();
        File.WriteAllText(path, text);
        await Assert.ThrowsAsync<InvalidDataException>(() => _store.GetItemClassificationsAsync());
        Assert.Equal(text, File.ReadAllText(path));
    }

    /// <summary>大小写兼容不改变背包页允许 null、空字符串和普通文本的约定。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("材料")]
    public async Task Dictionary_AcceptsValidLowercasePage(string? page)
    {
        await _store.SetDemandAsync(Uid, "甜甜花", 5, null);
        var path = Path.Combine(RecordsRoot, "item-overrides.json");
        File.WriteAllText(path, new JObject
        {
            ["甜甜花"] = new JObject { ["category"] = "植物", ["page"] = page, ["comment"] = "备注" }
        }.ToString());
        var values = await _store.GetItemClassificationsAsync();
        Assert.Equal("植物", values.Categories["甜甜花"]);
        Assert.Equal(page ?? "", values.Pages["甜甜花"]);
    }
    /// <summary>只清理当前用例创建的随机目录。</summary>
    public void Dispose() => Directory.Delete(_root, true);
}
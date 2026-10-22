using System.Linq;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using BetterGenshinImpact.ViewModel.SoloTaskSettings;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 一条龙设置字段的空值、周覆盖与数组选项纯模型回归，不涉及服务与窗口。
/// </summary>
public sealed class PuloniaOneDragonSettingsTests
{
    /// <summary>
    /// 自动秘境在任务计划中的任务类型。
    /// </summary>
    private static readonly string DomainTaskType = PuloniaSoloTaskExecutor.ToTaskType(AutoDomainTask.TaskId);

    /// <summary>打开尘歌壶设置时，缺失、空值、空购买列表及已有配置都不能自动生成覆盖。</summary>
    /// <param name="purchaseJson">原始购买配置；null 表示缺失此参数。</param>
    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("[\"星期五\",\"须臾树脂\",\"摩拉\"]")]
    public void SereniteaPot_OpeningSettingsKeepsPurchasesUnchanged(string? purchaseJson)
    {
        var original = new JObject { ["teleport_type"] = "地图传送" };
        if (purchaseJson is not null) original["secret_treasure_objects"] = JToken.Parse(purchaseJson);
        var snapshot = original.DeepClone();
        var settings = new SereniteaPotSettingsViewModel(original);
        var changes = new JObject();

        settings.CollectChanges(changes);

        Assert.Empty(changes);
        Assert.True(JToken.DeepEquals(snapshot, original));
    }

    /// <summary>更改日期与商品后仍可按执行器合同读取，并保留导入商品及原购买顺序。</summary>
    [Fact]
    public void SereniteaPot_EditedPurchasesRoundTripToTaskParameters()
    {
        var original = new JObject
        {
            ["teleport_type"] = "地图传送",
            ["secret_treasure_objects"] = new JArray("星期五", "摩拉", "导入商品", "须臾树脂")
        };
        var snapshot = original.DeepClone();
        var settings = new SereniteaPotSettingsViewModel(original)
        {
            TeleportType = "尘歌壶道具",
            PurchaseSchedule = "每天重复"
        };
        settings.PurchaseItems.Single(item => item.Name == "摩拉").IsSelected = false;
        settings.PurchaseItems.Single(item => item.Name == "布匹").IsSelected = true;
        var changes = new JObject();

        settings.CollectChanges(changes);
        var parameters = SoloTaskJson.Deserialize<SereniteaPotParam>(changes.ToString());

        Assert.Equal("尘歌壶道具", parameters.TeleportType);
        Assert.Equal(new[] { "每天重复", "导入商品", "须臾树脂", "布匹" }, parameters.SecretTreasureObjects);
        Assert.True(JToken.DeepEquals(snapshot, original));
    }

    /// <summary>取消全部商品只清空购买内容，进壶方式继续继承当前任务参数。</summary>
    [Fact]
    public void SereniteaPot_ClearingGoodsKeepsTeleportInherited()
    {
        var settings = new SereniteaPotSettingsViewModel(new JObject
        {
            ["teleport_type"] = "尘歌壶道具",
            ["secret_treasure_objects"] = new JArray("星期四", "须臾树脂", "摩拉")
        });
        foreach (var item in settings.PurchaseItems) item.IsSelected = false;
        var changes = new JObject();

        settings.CollectChanges(changes);

        Assert.Single(changes);
        Assert.Equal(new[] { "星期四" }, changes["secret_treasure_objects"]!.Values<string>());
    }

    /// <summary>
    /// 可空类型字段保持 null 值时打开设置不产生变更，构建值仍为 null。
    /// </summary>
    [Theory]
    [InlineData("boolean")]
    [InlineData("array")]
    [InlineData("object")]
    [InlineData("integer")]
    public void NullableField_OpeningSettingsKeepsNullValue(string type)
    {
        var schema = new JObject { ["type"] = new JArray(type, "null") };

        var field = new PuloniaOneDragonParameterFieldViewModel("nullable_setting", schema, JValue.CreateNull());

        Assert.False(field.HasChanges());
        Assert.Equal(JTokenType.Null, field.BuildValue()!.Type);
    }

    /// <summary>
    /// weekday_overrides 打开设置不产生变更，仅被修改的日期生成覆盖项。
    /// </summary>
    [Fact]
    public void WeekdayOverrides_OnlyModifiedDateProducesOverride()
    {
        // 生效值提供各日期的默认内容，空覆盖值表示尚未修改任何日期。
        var allProperties = new JObject
        {
            ["domain_name"] = new JObject { ["type"] = "string" },
            ["domain_round_num"] = new JObject { ["type"] = "integer" }
        };
        var effectiveValues = new JObject { ["domain_name"] = "测试秘境", ["domain_round_num"] = 2 };
        var schema = PuloniaOneDragonSettingChoices.WeekdaySchema(allProperties, effectiveValues, DomainTaskType);

        var field = new PuloniaOneDragonParameterFieldViewModel("weekday_overrides", schema, new JObject());
        Assert.False(field.HasChanges());
        Assert.True(JToken.DeepEquals(new JObject(), field.BuildValue()));

        field.Children[1].Children.First(item => item.Name == "enabled").BooleanValue = false;

        var built = Assert.IsType<JObject>(field.BuildValue());
        var overrideItem = Assert.Single(built.Properties());
        Assert.Equal("1", overrideItem.Name);
        Assert.False(built["1"]!["enabled"]!.Value<bool>());
        // 仅关闭当天不能把基础秘境和次数冻结为当天的显式覆盖。
        Assert.Single(Assert.IsType<JObject>(built["1"]));
    }

    /// <summary>修改已有日期的一项参数时，保留原覆盖，但不写入未修改的基础默认值。</summary>
    [Fact]
    public void WeekdayOverrides_EditingOneFieldKeepsRemainingFieldsInherited()
    {
        var properties = new JObject
        {
            ["party_name"] = new JObject { ["type"] = "string" },
            ["domain_round_num"] = new JObject { ["type"] = "integer" }
        };
        var values = new JObject { ["party_name"] = "基础队伍", ["domain_round_num"] = 2 };
        var original = new JObject { ["1"] = new JObject { ["enabled"] = false } };
        var field = new PuloniaOneDragonParameterFieldViewModel("weekday_overrides",
            PuloniaOneDragonSettingChoices.WeekdaySchema(properties, values, DomainTaskType), original);
        field.Children[1].Children.Single(item => item.Name == "domain_round_num").NumberValue = 3;

        var built = Assert.IsType<JObject>(field.BuildValue());
        var monday = Assert.IsType<JObject>(built["1"]);
        Assert.Equal(2, monday.Count);
        Assert.False(monday.Value<bool>("enabled"));
        Assert.Equal(3, monday.Value<int>("domain_round_num"));
        Assert.False(monday.ContainsKey("party_name"));
        Assert.True(JToken.DeepEquals(new JObject { ["enabled"] = false }, original["1"]));
    }

    /// <summary>
    /// 树脂优先级数组仅替换被修改的选项，其余项与字符串类型原样保留。
    /// </summary>
    [Fact]
    public void ResinPriority_ChangingFirstChoicePreservesRemainingStrings()
    {
        var value = new JArray("浓缩树脂", "原粹树脂");
        var schema = PuloniaOneDragonSettingChoices.Decorate(
            "resin_priority_list", new JObject { ["type"] = "array" }, value, DomainTaskType);

        var field = new PuloniaOneDragonParameterFieldViewModel("resin_priority_list", schema, value);
        Assert.False(field.HasChanges());

        field.Children[0].SelectedChoice = "脆弱树脂";

        var built = Assert.IsType<JArray>(field.BuildValue());
        Assert.Equal(JTokenType.String, built[0]!.Type);
        Assert.Equal("脆弱树脂", built[0]!.Value<string>());
        Assert.Equal(JTokenType.String, built[1]!.Type);
        Assert.Equal("原粹树脂", built[1]!.Value<string>());
    }

    /// <summary>
    /// 显式 JSON null 不同于空集合，文本输入 null 后构建值保持 null 类型。
    /// </summary>
    [Theory]
    [InlineData("array")]
    [InlineData("object")]
    public void NullableJsonField_ExplicitNullKeepsNullType(string type)
    {
        var schema = new JObject { ["type"] = new JArray(type, "null") };
        JToken initial = type == "array" ? new JArray("甲") : new JObject { ["flag"] = true };

        var field = new PuloniaOneDragonParameterFieldViewModel("optional_json", schema, initial);
        field.TextValue = "null";

        Assert.True(field.HasChanges());
        Assert.Equal(JTokenType.Null, field.BuildValue()!.Type);
    }
}

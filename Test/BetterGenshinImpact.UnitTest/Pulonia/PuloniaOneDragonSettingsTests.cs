using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Pulonia.Services;
using System.Linq;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.Common.Job;
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

    /// <summary>旧优先级字段不再提供专用选项，使用顺序由公共模型固定。</summary>
    [Fact]
    public void ResinPriority_NoLongerHasSpecialChoices()
    {
        var schema = PuloniaOneDragonSettingChoices.Decorate("resin_priority_list",
            new JObject { ["type"] = "array" }, new JArray("浓缩树脂", "原粹树脂"), DomainTaskType);
        Assert.Null(schema["properties"]);
        Assert.Null(schema["enum"]);
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

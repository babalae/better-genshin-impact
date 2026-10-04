using System.Linq;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 一条龙设置字段的空值、周覆盖与数组选项纯模型回归，不涉及服务与窗口。
/// </summary>
public sealed class PuloniaOneDragonSettingsTests
{
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
            ["round_count"] = new JObject { ["type"] = "integer" }
        };
        var effectiveValues = new JObject { ["domain_name"] = "测试秘境", ["round_count"] = 2 };
        var schema = PuloniaOneDragonSettingChoices.WeekdaySchema(allProperties, effectiveValues, "builtin.auto_domain");

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
            ["round_count"] = new JObject { ["type"] = "integer" }
        };
        var values = new JObject { ["party_name"] = "基础队伍", ["round_count"] = 2 };
        var original = new JObject { ["1"] = new JObject { ["enabled"] = false } };
        var field = new PuloniaOneDragonParameterFieldViewModel("weekday_overrides",
            PuloniaOneDragonSettingChoices.WeekdaySchema(properties, values, "builtin.auto_domain"), original);
        field.Children[1].Children.Single(item => item.Name == "round_count").NumberValue = 3;

        var built = Assert.IsType<JObject>(field.BuildValue());
        var monday = Assert.IsType<JObject>(built["1"]);
        Assert.Equal(2, monday.Count);
        Assert.False(monday.Value<bool>("enabled"));
        Assert.Equal(3, monday.Value<int>("round_count"));
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
            "resin_priority_list", new JObject { ["type"] = "array" }, value, "builtin.auto_domain");

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

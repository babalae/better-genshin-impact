using System.Linq;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 把共享设置值转换为界面短文本：选项显示名称、开关显示开或关、数值带单位。
/// </summary>
internal static class PuloniaSharedSettingText
{
    /// <summary>格式化设置值；未知设置项按紧凑 JSON 显示。</summary>
    /// <param name="key">设置项 ID。</param>
    /// <param name="value">设置值。</param>
    public static string Format(string key, JToken? value)
    {
        if (value is null || value.Type == JTokenType.Null)
            return "未设置";
        if (!PuloniaSettingCatalog.TryGet(key, out var definition))
            return value.ToString(Formatting.None);
        if (definition.Choices.FirstOrDefault(choice => JToken.DeepEquals(choice.Value, value)) is { } choice)
            return choice.Label;
        return definition.Editor switch
        {
            PuloniaSettingEditorKind.Boolean => value.Value<bool>() ? "开" : "关",
            PuloniaSettingEditorKind.CollectionCooldown => PuloniaCollectionCooldown.DescribeSettings(value),
            _ when value.Type == JTokenType.String && string.IsNullOrEmpty(value.Value<string>()) => "空",
            _ when value.Type == JTokenType.String => value.Value<string>()!,
            _ => value.ToString(Formatting.None) + (definition.Unit is { } unit ? " " + unit : string.Empty)
        };
    }
}

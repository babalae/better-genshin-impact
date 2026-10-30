using System.Dynamic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Core.Script.Settings;

/// <summary>
/// 调度器的设置值（ExpandoObject）与 HTML 设置页使用的 JSON 对象之间的转换。
/// </summary>
public static class ScriptSettingsJson
{
    /// <summary>
    /// 嵌套对象和数组转成 ExpandoObject 与 List，配置组用 System.Text.Json 保存时能正确写出。
    /// </summary>
    private static readonly JsonSerializer ExpandoSerializer = JsonSerializer.Create(new JsonSerializerSettings
    {
        Converters = { new ExpandoObjectConverter() }
    });

    /// <summary>读取设置值；未设置时为空对象。</summary>
    public static JObject FromExpando(ExpandoObject? settings)
        => settings is null ? new JObject() : JObject.FromObject(settings);

    /// <summary>把页面提交的对象转成调度器保存的设置值。</summary>
    public static ExpandoObject ToExpando(JObject settings)
        => settings.ToObject<ExpandoObject>(ExpandoSerializer) ?? new ExpandoObject();
}

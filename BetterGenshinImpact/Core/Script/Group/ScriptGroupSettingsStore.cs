using System;
using System.Collections.Concurrent;
using System.Dynamic;
using System.IO;
using System.Text.Json;
using BetterGenshinImpact.Service;

namespace BetterGenshinImpact.Core.Script.Group;

public static class ScriptGroupSettingsStore
{
    private static readonly ConcurrentDictionary<string, object> GroupLocks = new(StringComparer.OrdinalIgnoreCase);

    public static ExpandoObject Save(ScriptGroupProject project, string json, string groupDirectory)
    {
        if (project.GroupInfo == null)
        {
            throw new InvalidOperationException("当前脚本条目没有所属配置组，无法保存调度器设置。");
        }

        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("调度器设置必须是 JSON object。");
        }

        var replacement = JsonSerializer.Deserialize<ExpandoObject>(document.RootElement.GetRawText(), ConfigService.JsonOptions)
                          ?? new ExpandoObject();
        var group = project.GroupInfo;
        var filePath = Path.Combine(groupDirectory, $"{group.Name}.json");
        var groupLock = GroupLocks.GetOrAdd(Path.GetFullPath(filePath), _ => new object());

        lock (groupLock)
        {
            var previous = project.JsScriptSettingsObject;
            project.JsScriptSettingsObject = replacement;
            try
            {
                group.WriteToFileAtomically(filePath);
            }
            catch
            {
                project.JsScriptSettingsObject = previous;
                throw;
            }
        }

        return replacement;
    }

    public static ExpandoObject CloneSettings(ExpandoObject? source)
    {
        var json = JsonSerializer.Serialize(source ?? new ExpandoObject(), ConfigService.JsonOptions);
        return JsonSerializer.Deserialize<ExpandoObject>(json, ConfigService.JsonOptions) ?? new ExpandoObject();
    }
}

using BetterGenshinImpact.Core.Script.Group;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.GearTask;

/// <summary>
/// GearTask 任务组配置的序列化辅助方法。
/// </summary>
internal static class GearTaskGroupConfigHelper
{
    public static string? Serialize(ScriptGroupConfig? groupConfig)
    {
        if (groupConfig == null)
        {
            return null;
        }

        return System.Text.Json.JsonSerializer.Serialize(groupConfig, global::BetterGenshinImpact.Service.ConfigService.JsonOptions);
    }

    public static ScriptGroupConfig? Deserialize(string? groupConfigJson)
    {
        if (string.IsNullOrWhiteSpace(groupConfigJson))
        {
            return null;
        }

        try
        {
            var groupConfig = JsonConvert.DeserializeObject<ScriptGroupConfig>(groupConfigJson);
            if (groupConfig == null)
            {
                return null;
            }

            groupConfig.PathingConfig ??= new();
            groupConfig.ShellConfig ??= new();
            return groupConfig;
        }
        catch
        {
            return null;
        }
    }
}

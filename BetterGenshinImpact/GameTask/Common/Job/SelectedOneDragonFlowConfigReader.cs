using System.IO;
using BetterGenshinImpact.Core.Config;
using Newtonsoft.Json;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 读取 User\OneDragon 目录下当前选中的一条龙配置。
/// 仅供参数类的 FromSelectedOneDragonConfig 使用，让 JS 等旧入口保持“读取当前一条龙配置”的原有行为。
/// </summary>
internal static class SelectedOneDragonFlowConfigReader
{
    /// <summary>
    /// 一条龙配置文件所在目录。
    /// </summary>
    private static readonly string OneDragonFlowConfigFolder = Global.Absolute(@"User\OneDragon");

    /// <summary>
    /// 按原逻辑读取当前选中的一条龙配置：
    /// 名称与 <see cref="AllConfig.SelectedOneDragonFlowConfigName"/> 相同的配置优先；
    /// 找不到时使用第一个配置；目录中没有任何配置时返回默认配置。
    /// </summary>
    /// <param name="config">配置根，提供当前选中的一条龙配置名</param>
    /// <returns>当前选中的一条龙配置，不会为 null</returns>
    public static OneDragonFlowConfig Read(AllConfig config)
    {
        Directory.CreateDirectory(OneDragonFlowConfigFolder);

        // 读取文件夹内所有 json 配置，记录第一个可用配置和名称匹配的配置（与原逻辑一致，多个同名时取最后一个）
        var configFiles = Directory.GetFiles(OneDragonFlowConfigFolder, "*.json");
        OneDragonFlowConfig? first = null;
        OneDragonFlowConfig? selected = null;
        foreach (var configFile in configFiles)
        {
            var json = File.ReadAllText(configFile);
            var flowConfig = JsonConvert.DeserializeObject<OneDragonFlowConfig>(json);
            if (flowConfig == null)
            {
                continue;
            }

            first ??= flowConfig;
            if (flowConfig.Name == config.SelectedOneDragonFlowConfigName)
            {
                selected = flowConfig;
            }
        }

        // 找不到选中配置时退回第一个配置，再退回默认配置
        return selected ?? first ?? new OneDragonFlowConfig
        {
            Name = "默认配置"
        };
    }
}

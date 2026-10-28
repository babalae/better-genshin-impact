using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Group;
using Newtonsoft.Json.Linq;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 把旧配置组设置或全局配置转换为共享设置；只写与目录默认值不同的项，避免遮住上层设置。
/// 转换是一次性的显式复制，之后修改全局配置不影响已生成的设置。
/// </summary>
public static class PuloniaLegacySettingsConverter
{
    /// <summary>
    /// 旧配置组设置转为共享设置，按旧总开关语义冻结借用的全局配置：
    /// 行走总开关关闭时队伍与角色位按条件配置、战斗与回复等取全局配置，组内行走值被忽略；
    /// 只关闭战斗开关时战斗取全局自动战斗配置；未启用组 Shell 配置时不写 Shell 项。
    /// </summary>
    /// <param name="source">旧配置组设置。</param>
    /// <param name="global">导入时的全局配置。</param>
    /// <param name="includeShell">是否转换 Shell 设置。</param>
    /// <param name="notes">需要写入差异报告的说明。</param>
    public static JObject FromGroupConfig(ScriptGroupConfig source, AllConfig global, bool includeShell, ICollection<string> notes)
    {
        var pathing = source.PathingConfig;
        PathingPartyConfig effective;
        if (!pathing.Enabled)
        {
            effective = CreateFromGlobal(global);
            notes.Add("原配置未启用地图追踪行走配置，已改为按地图追踪条件配置选队，战斗、回复、小道具与吃药已按导入时的全局配置固定。");
        }
        else
        {
            effective = pathing;
            if (!pathing.AutoFightEnabled)
            {
                // 只替换战斗配置后立即序列化，全局对象不会被修改或保存引用。
                var withGlobalFight = JsonSerializer.Deserialize<PathingPartyConfig>(JsonSerializer.Serialize(pathing))!;
                withGlobalFight.AutoFightConfig = global.AutoFightConfig;
                effective = withGlobalFight;
                notes.Add("原配置未启用组战斗配置，战斗设置已按导入时的全局自动战斗配置固定。");
            }
        }

        var settings = new JObject();
        AddChanged(settings, PuloniaTaskCommonSettings.ToParameters("pathing", new ScriptGroupConfig { PathingConfig = effective }),
            PuloniaSettingCatalog.HostCategories);
        if (includeShell && source.EnableShellConfig)
            AddChanged(settings, PuloniaTaskCommonSettings.ToParameters("shell", source), [PuloniaSettingCatalog.ShellCategory]);
        return settings;
    }

    /// <summary>
    /// 按当前全局配置生成共享设置：队伍与角色位按地图追踪条件配置，战斗取全局自动战斗配置，
    /// 回复时机、小道具间隔和自动吃药取地图追踪条件配置；与旧调度器“未启用配置组设置”时的行为一致。
    /// </summary>
    /// <param name="global">当前全局配置。</param>
    public static JObject FromGlobalConfig(AllConfig global)
    {
        var settings = new JObject();
        AddChanged(settings, PuloniaTaskCommonSettings.ToParameters("pathing",
            new ScriptGroupConfig { PathingConfig = CreateFromGlobal(global) }), PuloniaSettingCatalog.HostCategories);
        return settings;
    }

    /// <summary>建立旧调度器回落全局配置时实际生效的行走配置。</summary>
    private static PathingPartyConfig CreateFromGlobal(AllConfig global)
    {
        var condition = global.PathingConditionConfig;
        return new PathingPartyConfig
        {
            PartyByCondition = true,
            RecoverTiming = condition.RecoverTiming,
            UseGadgetIntervalMs = condition.UseGadgetIntervalMs,
            AutoEatEnabled = condition.AutoEatEnabled,
            AutoFightConfig = global.AutoFightConfig
        };
    }

    /// <summary>按设置项的参数路径取值，与目录默认值不同才写入。</summary>
    private static void AddChanged(JObject target, JObject parameters, string[] categories)
    {
        foreach (var definition in PuloniaSettingCatalog.Definitions.Where(item => categories.Contains(item.Category)))
        {
            JToken? value = parameters;
            foreach (var segment in definition.ParameterPath.Split('.'))
                value = (value as JObject)?[segment];
            if (value is not null && !JToken.DeepEquals(value, definition.DefaultValue))
                target[definition.Key] = value.DeepClone();
        }
    }
}

using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 合成浓缩树脂任务的参数。
/// </summary>
public class CraftCondensedResinParam
{
    /// <summary>
    /// 前往哪个国家的合成台，对应路线文件“合成台_{国家}.json”。
    /// </summary>
    public string Country { get; set; } = "枫丹";

    /// <summary>
    /// 合成后需要保留的原粹树脂数量；大于 0 时按保留数量计算合成次数，否则直接按默认数量合成。
    /// </summary>
    public int MinResinToKeep { get; set; }

    /// <summary>
    /// 按原逻辑读取当前选中一条龙配置中的保留树脂数，生成参数。供 JS 入口保持原有行为。
    /// </summary>
    /// <param name="config">配置根，提供当前选中的一条龙配置名</param>
    /// <param name="country">前往哪个国家的合成台</param>
    /// <returns>合成浓缩树脂参数</returns>
    public static CraftCondensedResinParam FromSelectedOneDragonConfig(AllConfig config, string country)
    {
        var selected = SelectedOneDragonFlowConfigReader.Read(config);
        return new CraftCondensedResinParam
        {
            Country = country,
            MinResinToKeep = selected.MinResinToKeep
        };
    }
}

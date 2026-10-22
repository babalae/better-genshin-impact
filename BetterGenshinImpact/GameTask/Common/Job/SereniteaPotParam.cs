using System.Collections.Generic;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 领取尘歌壶奖励任务的参数。
/// </summary>
public class SereniteaPotParam
{
    /// <summary>
    /// 进入尘歌壶的方式：“地图传送”通过大地图传送；其他值（如“尘歌壶道具”）使用背包中的尘歌壶道具。
    /// </summary>
    public string TeleportType { get; set; } = "地图传送";

    /// <summary>
    /// 洞天百宝购买配置。第一项是购买日期（“星期一”~“星期日”或“每天重复”），其后是要购买的商品名；为空表示不购买。
    /// </summary>
    public List<string> SecretTreasureObjects { get; set; } = [];

    /// <summary>
    /// 按原逻辑读取当前选中一条龙配置中的进壶方式和洞天百宝购买配置，生成参数。供 JS 入口保持原有行为。
    /// </summary>
    /// <param name="config">配置根，提供当前选中的一条龙配置名</param>
    /// <returns>领取尘歌壶奖励参数</returns>
    public static SereniteaPotParam FromSelectedOneDragonConfig(AllConfig config)
    {
        var selected = SelectedOneDragonFlowConfigReader.Read(config);
        return new SereniteaPotParam
        {
            TeleportType = selected.SereniteaPotTpType,
            // 复制一份，避免任务运行时与一条龙配置共享同一个列表
            SecretTreasureObjects = [.. selected.SecretTreasureObjects]
        };
    }
}

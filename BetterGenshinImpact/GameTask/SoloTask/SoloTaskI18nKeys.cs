using System.Collections.Generic;
using BetterGenshinImpact.Service.I18n;

namespace BetterGenshinImpact.GameTask.SoloTask;

/// <summary>
/// 独立任务显示名的 i18n Key 登记表。
/// </summary>
/// <remarks>
/// <see cref="SoloTaskInfoAttribute"/> 中的显示名与说明是特性参数，i18n 同步工具不会扫描，因此在这里用
/// <c>I18nService.Key("...")</c> 逐条登记。显示名本身仍是数据（任务计划节点默认名称、旧配置导入的匹配等），
/// 只在界面展示时通过 <see cref="SoloTaskDescriptor.LocalizedDisplayName"/> 或 <c>{i18n:TBinding}</c> 翻译。
/// 新增、改名或删除带 <see cref="SoloTaskInfoAttribute"/> 的任务时，必须同步修改本列表。
/// 可用 <c>grep -rn "\[SoloTaskInfo(" --include=*.cs</c> 核对。
/// </remarks>
internal static class SoloTaskI18nKeys
{
    /// <summary>
    /// 全部独立任务的显示名，按 Order 排列。
    /// </summary>
    public static readonly IReadOnlyList<string> DisplayNames =
    [
        I18nService.Key("一条龙"),
        I18nService.Key("领取邮件"),
        I18nService.Key("合成浓缩树脂"),
        I18nService.Key("领取每日奖励"),
        I18nService.Key("领取尘歌壶奖励"),
        I18nService.Key("领取纪行"),
        I18nService.Key("领取历练点"),
        I18nService.Key("体力计划"),
        I18nService.Key("自动秘境"),
        I18nService.Key("自动首领讨伐"),
        I18nService.Key("自动幽境危战"),
        I18nService.Key("自动地脉花"),
        I18nService.Key("自动战斗"),
        I18nService.Key("自动伐木"),
        I18nService.Key("自动钓鱼"),
        I18nService.Key("自动烹饪"),
        I18nService.Key("自动分解圣遗物"),
        I18nService.Key("自动使用兑换码"),
        I18nService.Key("返回主界面"),
        I18nService.Key("自动连招构建"),
        I18nService.Key("自动七圣召唤"),
        I18nService.Key("自动音游"),
        I18nService.Key("自动演奏专辑"),
        I18nService.Key("截取物品图标"),
        I18nService.Key("模型准确率测试"),
        I18nService.Key("数量 OCR 对比")
    ];

    /// <summary>
    /// 带说明的独立任务的说明文字。
    /// </summary>
    public static readonly IReadOnlyList<string> Descriptions =
    [
        I18nService.Key("按体力用量依次调度合成浓缩、首领、提升指南养成与剩余树脂任务，并汇总本次全部奖励。")
    ];
}

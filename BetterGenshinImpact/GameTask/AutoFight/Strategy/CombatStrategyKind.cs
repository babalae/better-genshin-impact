using System;

namespace BetterGenshinImpact.GameTask.AutoFight.Strategy;

/// <summary>战斗策略类型；组合值表示入口或控件允许使用的类型集合。</summary>
[Flags]
public enum CombatStrategyKind
{
    /// <summary>无。</summary>
    None = 0,
    /// <summary>根据队伍匹配目录内的文本策略。</summary>
    AutoSelect = 1,
    /// <summary>文本战斗策略。</summary>
    Txt = 2,
    /// <summary>JSON 条件战斗策略。</summary>
    Json = 4,
    /// <summary>自动连招行为树。</summary>
    Combo = 8,
    /// <summary>键鼠录制宏。</summary>
    KeyMouse = 16,
    /// <summary>全部策略类型。</summary>
    All = AutoSelect | Txt | Json | Combo | KeyMouse,
    /// <summary>首领、幽境和地脉花允许的策略类型。</summary>
    WithoutCombo = All & ~Combo,
}

using System.Runtime.Serialization;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 路线 CD 的规则模式。参数和规则对象中都按蛇形小写字符串保存（auto、calendar_days 等），取值与原字符串一致。
/// </summary>
public enum PuloniaCollectionCooldownMode
{
    /// <summary>按目录自动识别材料或类别；只出现在参数中，规则对象里会被替换为识别出的模式。</summary>
    Auto,

    /// <summary>不检查 CD；完整成功后仍记录时间，之后改为其他规则时可直接使用。</summary>
    Disabled,

    /// <summary>成功时刻加固定小时数。</summary>
    Rolling,

    /// <summary>下一个服务器游戏日开始时。</summary>
    Daily,

    /// <summary>成功当天的服务器日期加若干天的 00:00（午夜保守估算）。</summary>
    CalendarDays,

    /// <summary>下一个游戏日后顺延到指定星期。</summary>
    Weekly,

    /// <summary>有成功记录后不再自动执行。</summary>
    Once
}

/// <summary>
/// 规则对象的来源；JSON 沿用原有的中文取值，已固定的运行快照不需要迁移。
/// </summary>
public enum PuloniaCollectionCooldownRuleSource
{
    /// <summary>自动识别命中有 CD 的材料或类别目录。</summary>
    [EnumMember(Value = "目录自动识别")]
    Directory,

    /// <summary>自动识别命中混合类别（圣遗物、木材、摩拉盒等），不检查 CD。</summary>
    [EnumMember(Value = "混合类别")]
    Mixed,

    /// <summary>自动识别没有命中任何材料或类别，不检查 CD。</summary>
    [EnumMember(Value = "未匹配规则")]
    Unmatched,

    /// <summary>用户选择了不检查或自定义周期。</summary>
    [EnumMember(Value = "手动覆盖")]
    Manual
}

/// <summary>
/// 一份路线文件在某个账号下的 CD 状态；运行判定、运行预览、任务树标记和 CD 总览使用同一组状态。
/// </summary>
public enum PuloniaCollectionCooldownState
{
    /// <summary>没有成功记录，或已过下次可执行时间；本次执行。</summary>
    Ready,

    /// <summary>有成功记录且未到下次可执行时间；本次跳过。</summary>
    CoolingDown,

    /// <summary>一次性规则且已有成功记录；本次跳过。</summary>
    Completed,

    /// <summary>自动识别没有匹配，不限制；本次执行。</summary>
    Unmatched,

    /// <summary>用户关闭限制或识别为混合类别，不检查；本次执行。</summary>
    NotChecked
}

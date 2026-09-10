using CsTrees;
using CsTrees.Blackboard;
using CsTrees.FluentBuilder;
using System.Collections.Generic;
using System.ComponentModel;

namespace BetterGenshinImpact.GameTask.AutoBuildCombo;

/// <summary>
/// 自动连招行为目录
/// 工厂方法会被 CsTrees.MEAI 源生成器转换为 LLM 的工具调用方法，
/// 因此方法签名与 [Description] 就是 LLM 可见的动作词汇表，修改需谨慎
/// </summary>
public class AutoBuildComboCatalog : IBehaviourCatalog
{
    [Description("使用元素战技E：若目标角色不在场则先切换到该角色，再释放战技，释放后自动识别并记录冷却。注意：本动作不检查冷却状态，冷却中调用是无效按键。返回状态：角色不存在时Failure，否则Success。")]
    public UseSkill UseSkill(
        string name,
        [Description("目标角色名")] string avatarName,
        [Description("是否长按")] bool hold,
        Blackboard blackboard)
        => new(name, avatarName, hold, blackboard);

    [Description("检查并使用元素战技E：先根据冷却记录检测目标角色E是否就绪，未就绪返回Failure（不切人、不按键）；就绪则切换到该角色并释放，返回Success。优先用本动作代替单步IsSkillReady+UseSkill组合，未就绪时不会假成功堵死优先级")]
    public UseSkillIfReady UseSkillIfReady(
        string name,
        [Description("目标角色名")] string avatarName,
        [Description("是否长按")] bool hold,
        Blackboard blackboard)
        => new(name, avatarName, hold, blackboard);

    [Description("使用元素爆发Q：若目标角色不在场则先切换到该角色，再释放爆发。注意：能量不足或冷却中时内部静默跳过但仍返回Success，什么都不做。返回状态：角色不存在时Failure，否则恒为Success。")]
    public UseBurst UseBurst(
        string name,
        [Description("目标角色名")] string avatarName,
        Blackboard blackboard)
        => new(name, avatarName, blackboard);

    [Description("查询E技能是否就绪：基于冷却跟踪记录，就绪返回Success，冷却中返回Failure。不切人")]
    public IsSkillReady IsSkillReady(
        string name,
        [Description("目标角色名")] string avatarName,
        Blackboard blackboard)
        => new(name, avatarName, blackboard);

    [Description("查询Q爆发是否就绪：场上角色检测中央图标，场下角色检测右侧队伍栏图标，就绪返回Success，未就绪返回Failure。不切人")]
    public IsBurstReady IsBurstReady(
        string name,
        [Description("目标角色名")] string avatarName,
        Blackboard blackboard)
        => new(name, avatarName, blackboard);

    [Description("检查并使用元素爆发Q：先检测目标角色Q是否就绪，未就绪返回Failure（不切人、不按键）；就绪则切换到该角色并释放，返回Success。优先用本动作代替单步IsBurstReady+UseBurst组合，未就绪时不会假成功堵死优先级")]
    public UseBurstIfReady UseBurstIfReady(
        string name,
        [Description("目标角色名")] string avatarName,
        Blackboard blackboard)
        => new(name, avatarName, blackboard);

    [Description("普攻：连续短按左键1秒进行攻击。基础动作。返回状态：指定角色名不存在时Failure，否则Success。")]
    public Attack Attack(
        string name,
        [Description("目标角色名；若不在场会先切换到该角色")] string avatarName,
        Blackboard blackboard)
        => new(name, avatarName, 1, blackboard);

    [Description("重击：长按左键1秒蓄力攻击。基础动作。返回状态：指定角色名不存在时Failure，否则Success。")]
    public Charge Charge(
        string name,
        [Description("目标角色名；若不在场会先切换到该角色")] string avatarName,
        Blackboard blackboard)
        => new(name, avatarName, 1, blackboard);

    [Description("朝指定方向行走，持续指定秒数。基础动作。返回状态：指定角色名不存在时Failure，否则Success。")]
    public Walk Walk(
        string name,
        [Description("目标角色名；若不在场会先切换到该角色")] string avatarName,
        [Description("行走方向，只能是w(前)/a(左)/s(后)/d(右)之一")] string direction,
        [Description("持续行走的秒数")] double seconds,
        Blackboard blackboard)
        => new(name, avatarName, direction, seconds, blackboard);

    [Description("冲刺，持续指定秒数。基础动作。返回状态：指定角色名不存在时Failure，否则Success。")]
    public Dash Dash(
        string name,
        [Description("目标角色名；若不在场会先切换到该角色")] string avatarName,
        [Description("持续冲刺的秒数")] double seconds,
        Blackboard blackboard)
        => new(name, avatarName, seconds, blackboard);

    [Description("跳跃一次。基础动作。返回状态：指定角色名不存在时Failure，否则Success")]
    public Jump Jump(
        string name,
        [Description("目标角色名；若不在场会先切换到该角色")] string avatarName,
        Blackboard blackboard)
        => new(name, avatarName, blackboard);

    [Description("打开一个 BasicActionsByDuration（按时长循环基础动作序列）作用域，打开后须要在其中依次添加角色的基础动作子节点。会切换到目标角色并站场，循环执行子节点指定秒数。期间返回 Running，时间到返回 Success，角色不存在返回 Failure。")]
    public BasicActionsByDuration BasicActionsByDuration(
        [Description("节点名，应附带站场秒数")] string name,
        [Description("角色名")] string avatarName,
        [Description("站场秒数")] double seconds,
        [Description("是否站场时自动穿插E技能（点按）")] bool useE,
        IEnumerable<Behaviour> children,
        Blackboard blackboard)
        => new(name, avatarName, seconds, useE, children, blackboard);

    [Description("打开一个 BasicActionsByCount（按次数循环基础动作序列）作用域，打开后须要在其中依次添加角色的基础动作子节点。会切换到目标角色并站场，循环执行子节点指定轮数。期间返回 Running，轮数完成返回 Success，角色不存在返回 Failure。")]
    public BasicActionsByCount BasicActionsByCount(
        [Description("节点名，应附带站场轮数")] string name,
        [Description("角色名")] string avatarName,
        [Description("循环执行轮数")] int times,
        [Description("是否站场时自动穿插E技能（点按）")] bool useE,
        IEnumerable<Behaviour> children,
        Blackboard blackboard)
        => new(name, avatarName, times, useE, children, blackboard);
}

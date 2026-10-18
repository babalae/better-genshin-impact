using BetterGenshinImpact.GameTask.AutoFight.Model;
using CsTrees;
using CsTrees.Blackboard;
using CsTrees.FluentBuilder;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoCombo.ComboBuild;

/// <summary>
/// 自动连招行为目录
/// 工厂方法会被 CsTrees.MEAI 源生成器转换为 LLM 的工具调用方法，
/// 因此方法签名与 [Description] 就是 LLM 可见的动作词汇表，修改需谨慎
/// </summary>
public class AutoComboBuildCatalog(Avatar[] avatars) : IBehaviourCatalog
{
    /// <summary>
    /// 按名字解析队伍成员，不存在时抛异常（Build 期即暴露名字错误）
    /// </summary>
    private Avatar GetAvatarByName(string avatarName)
    {
        return avatars.FirstOrDefault(a => a.Name == avatarName)
            ?? throw new ArgumentException(
                $"角色“{avatarName}”不在当前队伍中（当前队伍：{string.Join("、", avatars.Select(a => a.Name))}）",
                nameof(avatarName));
    }

    [Description("使用元素战技E：若目标角色不在场则先切换到该角色，再释放战技。注意：本动作不检查冷却状态，冷却中调用是无效按键。返回Success")]
    public UseSkill UseSkill(
        string name,
        [Description("角色名")] string avatarName,
        [Description("是否长按")] bool hold)
        => new(name, GetAvatarByName(avatarName), hold);

    [Description("检查并使用元素战技E：先检测目标角色E是否就绪，冷却中返回Failure；就绪则释放并返回Success。优先用本动作代替单步IsSkillReady+UseSkill组合，未就绪时不会假成功堵死优先级")]
    public UseSkillIfReady UseSkillIfReady(
        string name,
        [Description("角色名")] string avatarName,
        [Description("是否长按")] bool hold,
        Blackboard blackboard)
        => new(name, GetAvatarByName(avatarName), hold, blackboard);

    [Description("使用元素爆发Q：若目标角色不在场则先切换到该角色，再释放爆发。注意：能量不足或冷却中时内部静默跳过但仍返回Success，什么都不做。返回Success")]
    public UseBurst UseBurst(
        string name,
        [Description("角色名")] string avatarName)
        => new(name, GetAvatarByName(avatarName));

    [Description("查询E技能是否就绪：就绪返回Success，冷却中返回Failure")]
    public IsSkillReady IsSkillReady(
        string name,
        [Description("角色名")] string avatarName,
        Blackboard blackboard)
        => new(name, GetAvatarByName(avatarName), blackboard);

    [Description("查询Q爆发是否就绪：场上角色检测中央图标，场下角色检测右侧队伍栏图标，就绪返回Success，未就绪返回Failure。不切人")]
    public IsBurstReady IsBurstReady(
        string name,
        [Description("角色名")] string avatarName)
        => new(name, GetAvatarByName(avatarName));

    [Description("检查并使用元素爆发Q：先检测目标角色Q是否就绪，未就绪返回Failure（不切人、不按键）；就绪则切换到该角色并释放，返回Success。优先用本动作代替单步IsBurstReady+UseBurst组合，未就绪时不会假成功堵死优先级")]
    public UseBurstIfReady UseBurstIfReady(
        string name,
        [Description("角色名")] string avatarName)
        => new(name, GetAvatarByName(avatarName));

    [Description("普攻：连续短按左键1秒进行攻击。基础动作。返回Success")]
    public Attack Attack(
        string name,
        [Description("角色名；若不在场会先切换到该角色")] string avatarName)
        => new(name, GetAvatarByName(avatarName), 1);

    [Description("重击：长按左键1秒蓄力攻击。基础动作。返回Success")]
    public Charge Charge(
        string name,
        [Description("角色名；若不在场会先切换到该角色")] string avatarName)
        => new(name, GetAvatarByName(avatarName), 1);

    [Description("朝指定方向行走，持续指定秒数。基础动作。返回Success")]
    public Walk Walk(
        string name,
        [Description("角色名；若不在场会先切换到该角色")] string avatarName,
        [Description("行走方向，只能是w(前)/a(左)/s(后)/d(右)之一")] string direction,
        [Description("持续行走的秒数")] double seconds)
        => new(name, GetAvatarByName(avatarName), direction, seconds);

    [Description("冲刺，持续指定秒数。基础动作。返回Success")]
    public Dash Dash(
        string name,
        [Description("角色名；若不在场会先切换到该角色")] string avatarName,
        [Description("持续冲刺的秒数")] double seconds)
        => new(name, GetAvatarByName(avatarName), seconds);

    [Description("跳跃一次。基础动作。返回Success")]
    public Jump Jump(
        string name,
        [Description("角色名；若不在场会先切换到该角色")] string avatarName)
        => new(name, GetAvatarByName(avatarName));
    [Description("先检查角色的元素战技E，冷却中返回Failure，就绪则释放，然后打开一个按时长循环动作序列的作用域，打开后须要在其中依次添加动作子节点，会循环执行序列指定秒数，子节点失败会被跳过，时间到返回 Success。")]
    public UseSkillIfReadyThenDoActionsByDuration UseSkillIfReadyThenDoActionsByDuration(
        [Description("节点名，应附带站场秒数")] string name,
        [Description("角色名")] string avatarName,
        [Description("是否长按战技")] bool hold,
        [Description("站场秒数")] double seconds,
        IEnumerable<Behaviour> children,
        Blackboard blackboard)
        => new(name, GetAvatarByName(avatarName), hold, seconds, children, blackboard);

    [Description("先检查角色的元素战技E，冷却中返回Failure，就绪则释放，然后打开一个按次数循环动作序列的作用域，打开后须要在其中依次添加动作子节点，会循环执行序列指定轮数，子节点失败会被跳过，轮数完成返回 Success。")]
    public UseSkillIfReadyThenDoActionsByCount UseSkillIfReadyThenDoActionsByCount(
        [Description("节点名，应附带站场轮数")] string name,
        [Description("角色名")] string avatarName,
        [Description("是否长按战技")] bool hold,
        [Description("循环执行轮数")] int times,
        IEnumerable<Behaviour> children,
        Blackboard blackboard)
        => new(name, GetAvatarByName(avatarName), hold, times, children, blackboard);

    [Description("先检查角色的元素爆发Q，未就绪返回Failure，就绪则释放，然后打开一个按时长循环动作序列的作用域，打开后须要在其中依次添加动作子节点，会循环执行序列指定秒数，子节点失败会被跳过，时间到返回 Success。")]
    public UseBurstIfReadyThenDoActionsByDuration UseBurstIfReadyThenDoActionsByDuration(
        [Description("节点名，应附带站场秒数")] string name,
        [Description("角色名")] string avatarName,
        [Description("站场秒数")] double seconds,
        IEnumerable<Behaviour> children,
        Blackboard blackboard)
        => new(name, GetAvatarByName(avatarName), seconds, children, blackboard);

    [Description("先检查角色的元素爆发Q，未就绪返回Failure，就绪则释放，然后打开一个按次数循环动作序列的作用域，打开后须要在其中依次添加动作子节点，会循环执行序列指定轮数，子节点失败会被跳过，轮数完成返回 Success。")]
    public UseBurstIfReadyThenDoActionsByCount UseBurstIfReadyThenDoActionsByCount(
        [Description("节点名，应附带站场轮数")] string name,
        [Description("角色名")] string avatarName,
        [Description("循环执行轮数")] int times,
        IEnumerable<Behaviour> children,
        Blackboard blackboard)
        => new(name, GetAvatarByName(avatarName), times, children, blackboard);
}

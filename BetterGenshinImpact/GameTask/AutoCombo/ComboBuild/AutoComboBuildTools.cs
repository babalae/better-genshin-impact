using BetterGenshinImpact.GameTask.AutoFight.Model;
using CsTrees.MEAI;
using System.ComponentModel;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoCombo.ComboBuild;

/// <summary>
/// 行为树构建工具宿主
/// CsTrees.MEAI 源生成器会根据 AutoComboBuildBuilder 中的 Catalog 工厂方法
/// 自动生成对应的工具方法（含 End/BuildTree/ShowTreeStatus 等基类内置方法），
/// 聚合到 Tools 属性（Delegate[]）供 MEAI Agent 注册
/// </summary>
/// <param name="builder">行为树构建器，节点工厂类工具经其操作树</param>
/// <param name="avatars">建树时的队伍成员实例，设置类工具直接修改其状态（随会话携带到运行时）</param>
public partial class AutoComboBuildTools(AutoComboBuildBuilder builder, Avatar[] avatars)
    : BuildToolsBase<AutoComboBuildBuilder>(builder)
{
    /// <summary>
    /// 设置类工具：构建期直接修改队伍成员实例状态（非树节点，不进入行为树）
    /// </summary>
    [Description("设置角色E技能的手动冷却秒数，覆盖战斗中的动态识别：之后UseSkill产生的CD将直接按此值记录与判断。seconds传0表示取消手动CD，恢复动态识别。本设置属于战斗上下文配置")]
    public ToolResult SetManualSkillCd(
        [Description("角色名")] string avatarName,
        [Description("手动CD秒数，大于0生效，等于0则取消手动CD")] double seconds)
    {
        var avatar = avatars.FirstOrDefault(a => a.Name == avatarName)
            ?? throw new System.ArgumentException(
                $"角色“{avatarName}”不在当前队伍中（当前队伍：{string.Join("、", avatars.Select(a => a.Name))}）",
                nameof(avatarName));

        if (double.IsNaN(seconds) || seconds < 0)
        {
            throw new System.ArgumentOutOfRangeException(nameof(seconds), seconds, "手动CD秒数不能为负数或非数字");
        }

        avatar.ManualSkillCd = seconds;

        return new ToolResult
        {
            Message = seconds > 0
            ? $"{avatarName} 的E技能手动CD已设为 {seconds} 秒"
            : $"{avatarName} 的手动CD已取消，恢复动态识别"
        };
    }
}

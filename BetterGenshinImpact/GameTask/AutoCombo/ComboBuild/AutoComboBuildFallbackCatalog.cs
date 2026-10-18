using BetterGenshinImpact.GameTask.AutoFight.Model;
using CsTrees.FluentBuilder;
using System;
using System.ComponentModel;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoCombo.ComboBuild
{
    /// <summary>
    /// 兜底攻击行为目录
    /// 仅提供基础动作（普攻/重击/行走/冲刺/跳跃）
    /// </summary>
    public class AutoComboBuildFallbackCatalog(Avatar[] avatars) : IBehaviourCatalog
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

        [Description("普攻：连续短按左键1秒进行攻击。返回Success")]
        public Attack Attack(
            string name,
            [Description("角色名；若不在场会先切换到该角色")] string avatarName)
            => new(name, GetAvatarByName(avatarName), 1);

        [Description("重击：长按左键1秒蓄力攻击。返回Success")]
        public Charge Charge(
            string name,
            [Description("角色名；若不在场会先切换到该角色")] string avatarName)
            => new(name, GetAvatarByName(avatarName), 1);

        [Description("朝指定方向行走指定秒数。返回Success")]
        public Walk Walk(
            string name,
            [Description("角色名；若不在场会先切换到该角色")] string avatarName,
            [Description("行走方向，只能是w(前)/a(左)/s(后)/d(右)之一")] string direction,
            [Description("持续行走的秒数")] double seconds)
            => new(name, GetAvatarByName(avatarName), direction, seconds);

        [Description("冲刺指定秒数。返回Success")]
        public Dash Dash(
            string name,
            [Description("角色名；若不在场会先切换到该角色")] string avatarName,
            [Description("持续冲刺的秒数")] double seconds)
            => new(name, GetAvatarByName(avatarName), seconds);

        [Description("跳跃一次。返回Success")]
        public Jump Jump(
            string name,
            [Description("角色名；若不在场会先切换到该角色")] string avatarName)
            => new(name, GetAvatarByName(avatarName));
    }
}

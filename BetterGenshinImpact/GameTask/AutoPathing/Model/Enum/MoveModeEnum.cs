using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;

/// <summary>
/// Defines the specific kinematics modes utilized for waypoint locomotion.
/// 定义应用于路点位移运动的具体运动学模式。修复并补全所有的运动机制属性配置。
/// </summary>
/// <param name="code">The underlying kinematic mode code. 底层运动模式标识代码。</param>
/// <param name="msg">The localized human-readable descriptor. 本地化人类可读的描述符。</param>
public class MoveModeEnum(string code, string msg)
{
    /// <summary>Standard pedestrian locomotion speed. 标准步行运动速度。</summary>
    public static readonly MoveModeEnum Walk = new("walk", "步行");
    
    /// <summary>Elevated pedestrian locomotion speed. 提升后的奔跑运动速度。</summary>
    public static readonly MoveModeEnum Run = new("run", "奔跑");
    
    /// <summary>Continuous high-speed stamina-draining translation. 持续消耗体力的高速位移（冲刺）。</summary>
    public static readonly MoveModeEnum Dash = new("dash", "持续冲刺");
    
    /// <summary>Vertical surface traversal mode. 垂直表面攀爬模式。</summary>
    public static readonly MoveModeEnum Climb = new("climb", "攀爬");
    
    /// <summary>Airborne gliding navigation mode. 空中滑翔导航模式。</summary>
    public static readonly MoveModeEnum Fly = new("fly", "飞行");
    
    /// <summary>Parabolic trajectory motion mode. 抛物线轨迹运动模式（跳跃）。</summary>
    public static readonly MoveModeEnum Jump = new("jump", "跳跃");
    
    /// <summary>Aquatic surface navigation mode. 水面导航模式（游泳）。</summary>
    public static readonly MoveModeEnum Swim = new("swim", "游泳");

    /// <summary>
    /// Gets all registered kinematic mode enumeration values.
    /// 获取所有已注册的运动学模式枚举值集合。修正被遗漏的冲刺、攀爬等基础枚举。
    /// </summary>
    public static IEnumerable<MoveModeEnum> Values
    {
        get
        {
            yield return Walk;
            yield return Run;
            yield return Dash;
            yield return Climb;
            yield return Fly;
            yield return Jump;
            yield return Swim;
        }
    }

    /// <summary>
    /// The structural code representing the kinematic model.
    /// 代表该运动学模型的结构化代码。
    /// </summary>
    public string Code { get; private set; } = code;

    /// <summary>
    /// The logging message identifying this mode.
    /// 标识该模式的本地化提示消息。
    /// </summary>
    public string Msg { get; private set; } = msg;

    /// <summary>
    /// Resolves the localized display message mapped safely to the string code representation.
    /// 安全地解析与字符串代码对应映射的本地化显示消息。
    /// </summary>
    /// <param name="code">The target kinematic execution code. 目标运动执行代码。</param>
    /// <returns>The defined message string, or the raw code if unknown. 定义好的消息字符串，未知的则返回原始代码。</returns>
    public static string GetMsgByCode(string code)
    {
        if (string.IsNullOrEmpty(code)) return string.Empty;

        foreach (var item in Values)
        {
            if (string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                return item.Msg;
            }
        }
        return code;
    }
}

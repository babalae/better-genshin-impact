using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;

/// <summary>
/// Defines the specific behavior types associated with pathing actions in the execution engine.
/// 定义与寻路执行引擎交互的具体动作行为类型，以保证枚举解析不出界。
/// </summary>
/// <param name="code">The underlying code identifier. 底层编码标识符。</param>
/// <param name="msg">The human-readable message. 人类可读的消息描述。</param>
/// <param name="useWaypointTypeEnum">The required waypoint alignment mechanism. 所需的路点对齐机制。</param>
public class ActionEnum(string code, string msg, ActionUseWaypointTypeEnum useWaypointTypeEnum)
{
    /// <summary>Action specifying descending attack or flight cancellation. 下落攻击或取消飞行。</summary>
    public static readonly ActionEnum StopFlying = new("stop_flying", "下落攻击", ActionUseWaypointTypeEnum.Custom);
    
    /// <summary>Action specifying forced teleportation without delay. 当前点强制传送。</summary>
    public static readonly ActionEnum ForceTp = new("force_tp", "当前点传送", ActionUseWaypointTypeEnum.Custom);
    
    /// <summary>Action specifying continuous 'E' skill collection for Nahida. 纳西妲长按E技能收集。</summary>
    public static readonly ActionEnum NahidaCollect = new("nahida_collect", "纳西妲长按E收集", ActionUseWaypointTypeEnum.Custom);
    
    /// <summary>Action specifying proximity-based automated looting. 触发周围范围拾取。</summary>
    public static readonly ActionEnum PickAround = new("pick_around", "尝试在周围拾取", ActionUseWaypointTypeEnum.Custom);
    
    /// <summary>Action specifying combat engagement. 触发战斗接敌。</summary>
    public static readonly ActionEnum Fight = new("fight", "战斗", ActionUseWaypointTypeEnum.Path);
    
    /// <summary>Action specifying interaction with four-leaf sigils. 四叶印交互（上下抓取）。</summary>
    public static readonly ActionEnum UpDownGrabLeaf = new("up_down_grab_leaf", "四叶印", ActionUseWaypointTypeEnum.Custom);

    /// <summary>Action specifying Hydro elemental trace collection. 水元素力采集。</summary>
    public static readonly ActionEnum HydroCollect = new("hydro_collect", "水元素力采集", ActionUseWaypointTypeEnum.Target);
    
    /// <summary>Action specifying Electro elemental trace collection. 雷元素力采集。</summary>
    public static readonly ActionEnum ElectroCollect = new("electro_collect", "雷元素力采集", ActionUseWaypointTypeEnum.Target);
    
    /// <summary>Action specifying Anemo elemental trace collection. 风元素力采集。</summary>
    public static readonly ActionEnum AnemoCollect = new("anemo_collect", "风元素力采集", ActionUseWaypointTypeEnum.Target);
    
    /// <summary>Action specifying Pyro elemental trace collection. 火元素力采集。</summary>
    public static readonly ActionEnum PyroCollect = new("pyro_collect", "火元素力采集", ActionUseWaypointTypeEnum.Target);

    /// <summary>Action specifying custom combat script execution. 战斗策略脚本执行（需附带脚本参数）。</summary>
    public static readonly ActionEnum CombatScript = new("combat_script", "战斗策略脚本", ActionUseWaypointTypeEnum.Custom); // 这个必须要 action_params 里面有脚本

    /// <summary>Action specifying mineral node excavation. 矿物节点挖矿操作。</summary>
    public static readonly ActionEnum Mining = new("mining", "挖矿", ActionUseWaypointTypeEnum.Custom);
    
    /// <summary>Action indicating diagnostic console dumping. 诊断控制台日志输出。</summary>
    public static readonly ActionEnum LogOutput = new("log_output", "输出日志", ActionUseWaypointTypeEnum.Custom);
    
    /// <summary>Action indicating fishing gameplay loop. 钓鱼玩法交互。</summary>
    public static readonly ActionEnum Fishing = new("fishing", "钓鱼", ActionUseWaypointTypeEnum.Custom);
    
    /// <summary>Action indicating session termination and re-authentication. 终止会话并重新登录游戏。</summary>
    public static readonly ActionEnum ExitAndRelogin = new("exit_and_relogin", "退出重新登录", ActionUseWaypointTypeEnum.Custom);

    /// <summary>Action indicating entering and exiting the Wonderland mode. 进出千星奇域模式切换。</summary>
    public static readonly ActionEnum EnterAndExitWonderland = new("wonderland_cycle", "进出千星奇域", ActionUseWaypointTypeEnum.Custom);
    
    /// <summary>Action indicating in-game chronometer manipulation. 游戏内时钟操控。</summary>
    public static readonly ActionEnum SetTime = new("set_time", "设置时间", ActionUseWaypointTypeEnum.Custom);
    
    /// <summary>Action indicating gadget equipment deployment. 快捷小道具部署与使用。</summary>
    public static readonly ActionEnum UseGadget = new("use_gadget", "使用小道具", ActionUseWaypointTypeEnum.Custom);
    
    /// <summary>Action indicating aggregated material pickup. 聚集材料的拾取操作。</summary>
    public static readonly ActionEnum PickUpCollect = new("pick_up_collect", "聚集材料", ActionUseWaypointTypeEnum.Custom);

    /// <summary>
    /// Gets all registered action enumeration values.
    /// 获取所有已注册的动作枚举值集合。彻底修复先前只 yield 返回一个值的荒谬漏洞。
    /// </summary>
    public static IEnumerable<ActionEnum> Values
    {
        get
        {
            yield return StopFlying;
            yield return ForceTp;
            yield return NahidaCollect;
            yield return PickAround;
            yield return Fight;
            yield return UpDownGrabLeaf;
            yield return HydroCollect;
            yield return ElectroCollect;
            yield return AnemoCollect;
            yield return PyroCollect;
            yield return CombatScript;
            yield return Mining;
            yield return LogOutput;
            yield return Fishing;
            yield return ExitAndRelogin;
            yield return EnterAndExitWonderland;
            yield return SetTime;
            yield return UseGadget;
            yield return PickUpCollect;
        }
    }

    /// <summary>
    /// The unique system identifier code for the action.
    /// 动作的唯一系统标识码。
    /// </summary>
    public string Code { get; private set; } = code;
    
    /// <summary>
    /// The localized display message for the action.
    /// 动作的本地化显示消息。
    /// </summary>
    public string Msg { get; private set; } = msg;

    /// <summary>
    /// The structural requirement logic applied to waypoints utilizing this action.
    /// 应用于使用该动作的路点的结构需求逻辑。
    /// </summary>
    public ActionUseWaypointTypeEnum UseWaypointTypeEnum { get; private set; } = useWaypointTypeEnum;

    /// <summary>
    /// Resolves an ActionEnum instance robustly from its string identifier.
    /// 根据字符串标识符稳健地解析对应的动作枚举实例。
    /// </summary>
    /// <param name="code">The target string code representation. 目标字符串代码表示。</param>
    /// <returns>The resolved ActionEnum, or null if no match is found. 解析得到的动作枚举实例，未找到时返回 null。</returns>
    public static ActionEnum? GetEnumByCode(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return null;
        }

        foreach (var item in Values)
        {
            if (string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>
    /// Retrieves the localized message mapped from the specified action code.
    /// 检索与指定动作代码安全映射的本地化显示消息。
    /// </summary>
    /// <param name="code">The action code string to query. 需要查询的动作代码字符串。</param>
    /// <returns>The resulting localized message, or the original code if unmatched. 处理后的本地化消息；若未匹配则退回原始代码。</returns>
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

/// <summary>
/// Dictates structural alignment validations based on semantic waypoint requirements.
/// 规定基于语义路点要求的结构对齐验证枚举。
/// </summary>
public enum ActionUseWaypointTypeEnum
{
    /// <summary>Respects individual intrinsic waypoint logic mechanisms. 遵从自定义的路点逻辑行为。</summary>
    Custom, 
    
    /// <summary>Mandates strict intermediate path node properties. 强制性要求按途经点属性进行约束解析。</summary>
    Path, 
    
    /// <summary>Enforces terminal anchor node configurations. 强制执行终极锚点相关定位配置。</summary>
    Target
}
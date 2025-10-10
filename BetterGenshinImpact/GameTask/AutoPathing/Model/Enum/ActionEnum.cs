using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;

public class ActionEnum(string code, string msg, ActionUseWaypointTypeEnum useWaypointTypeEnum)
{
    public static readonly ActionEnum StopFlying = new("stop_flying", "下落攻击", ActionUseWaypointTypeEnum.Custom);
    public static readonly ActionEnum ForceTp = new("force_tp", "当前点传送", ActionUseWaypointTypeEnum.Custom);
    public static readonly ActionEnum NahidaCollect = new("nahida_collect", "纳西妲长按E收集", ActionUseWaypointTypeEnum.Custom);
    public static readonly ActionEnum PickAround = new("pick_around", "尝试在周围拾取", ActionUseWaypointTypeEnum.Custom);
    public static readonly ActionEnum Fight = new("fight", "战斗", ActionUseWaypointTypeEnum.Path);
    public static readonly ActionEnum UpDownGrabLeaf = new("up_down_grab_leaf", "四叶印", ActionUseWaypointTypeEnum.Custom);

    public static readonly ActionEnum HydroCollect = new("hydro_collect", "水元素力采集", ActionUseWaypointTypeEnum.Target);
    public static readonly ActionEnum ElectroCollect = new("electro_collect", "雷元素力采集", ActionUseWaypointTypeEnum.Target);
    public static readonly ActionEnum AnemoCollect = new("anemo_collect", "风元素力采集", ActionUseWaypointTypeEnum.Target);
    public static readonly ActionEnum PyroCollect = new("pyro_collect", "火元素力采集", ActionUseWaypointTypeEnum.Target);

    public static readonly ActionEnum CombatScript = new("combat_script", "战斗策略脚本", ActionUseWaypointTypeEnum.Custom); // 这个必须要 action_params 里面有脚本

    public static readonly ActionEnum Mining = new("mining", "挖矿", ActionUseWaypointTypeEnum.Custom);
    public static readonly ActionEnum LogOutput = new("log_output", "输出日志", ActionUseWaypointTypeEnum.Custom);
    
    public static readonly ActionEnum Fishing = new("fishing", "钓鱼", ActionUseWaypointTypeEnum.Custom);
    public static readonly ActionEnum ExitAndRelogin = new("exit_and_relogin", "退出重新登录", ActionUseWaypointTypeEnum.Custom);
    public static readonly ActionEnum SetTime = new("set_time", "设置时间", ActionUseWaypointTypeEnum.Custom);
    public static readonly ActionEnum UseGadget = new("use_gadget", "使用小道具", ActionUseWaypointTypeEnum.Custom);
    public static readonly ActionEnum PickUpCollect = new("pick_up_collect", "聚集材料", ActionUseWaypointTypeEnum.Custom);

    // 还有要加入的其他动作
    // 滚轮F
    // 触发自动战斗任务
    // 执行 js 脚本(推荐)
    // 执行键鼠脚本
    // 纳西达采集

    public static IEnumerable<ActionEnum> Values
    {
        get { yield return StopFlying; }
    }

    public string Code { get; private set; } = code;
    public string Msg { get; private set; } = msg;

    public ActionUseWaypointTypeEnum UseWaypointTypeEnum { get; private set; } = useWaypointTypeEnum;

    public static ActionEnum? GetEnumByCode(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return null;
        }

        foreach (var item in Values)
        {
            if (item.Code == code)
            {
                return item;
            }
        }

        return null;
    }

    public static string GetMsgByCode(string code)
    {
        foreach (var item in Values)
        {
            if (item.Code == code)
            {
                return item.Msg;
            }
        }

        return code;
    }
}

public enum ActionUseWaypointTypeEnum
{
    Custom, // 跟随路径点 Type
    Path, // 强制 Path
    Target // 强制 Target
}
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;

public class ActionEnum(string code, string msg)
{
    public static readonly ActionEnum StopFlying = new("stop_flying", "下落攻击");
    public static readonly ActionEnum ForceTp = new("force_tp", "当前点传送");
    public static readonly ActionEnum NahidaCollect = new("nahida_collect", "纳西妲长按E收集");
    public static readonly ActionEnum PickAround = new("pick_around", "尝试在周围拾取");
    public static readonly ActionEnum Fight = new("fight", "战斗");
    public static readonly ActionEnum UpDownGrabLeaf = new("up_down_grab_leaf", "四叶印");

    public static readonly ActionEnum HydroCollect = new("hydro_collect", "水元素力采集");
    public static readonly ActionEnum ElectroCollect = new("electro_collect", "雷元素力采集");
    public static readonly ActionEnum AnemoCollect = new("anemo_collect", "风元素力采集");
    
    public static readonly ActionEnum CombatScript = new("combat_script", "战斗策略脚本"); // 这个必须要 action_params 里面有脚本
    
    public static readonly ActionEnum Mining = new("mining", "挖矿");

    // 还有要加入的其他动作
    // 滚轮F
    // 触发自动战斗任务
    // 执行 js 脚本(推荐)
    // 执行键鼠脚本
    // 纳西达采集

    public static IEnumerable<ActionEnum> Values
    {
        get
        {
            yield return StopFlying;
        }
    }

    public string Code { get; private set; } = code;
    public string Msg { get; private set; } = msg;

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

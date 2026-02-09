using BetterGenshinImpact.Helpers;
﻿using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;

public class ActionEnum(string code, string msg, ActionUseWaypointTypeEnum useWaypointTypeEnum)
{
    public static readonly ActionEnum StopFlying = new("stop_flying", Lang.S["GameTask_11172_c6f442"], ActionUseWaypointTypeEnum.Custom);
    public static readonly ActionEnum ForceTp = new("force_tp", Lang.S["GameTask_11171_7e865c"], ActionUseWaypointTypeEnum.Custom);
    public static readonly ActionEnum NahidaCollect = new("nahida_collect", Lang.S["GameTask_11170_02445a"], ActionUseWaypointTypeEnum.Custom);
    public static readonly ActionEnum PickAround = new("pick_around", Lang.S["GameTask_11169_f22186"], ActionUseWaypointTypeEnum.Custom);
    public static readonly ActionEnum Fight = new("fight", Lang.S["GameTask_11168_da01bb"], ActionUseWaypointTypeEnum.Path);
    public static readonly ActionEnum UpDownGrabLeaf = new("up_down_grab_leaf", Lang.S["GameTask_11149_a2be0d"], ActionUseWaypointTypeEnum.Custom);

    public static readonly ActionEnum HydroCollect = new("hydro_collect", Lang.S["GameTask_11167_793d96"], ActionUseWaypointTypeEnum.Target);
    public static readonly ActionEnum ElectroCollect = new("electro_collect", Lang.S["GameTask_11166_4ebfce"], ActionUseWaypointTypeEnum.Target);
    public static readonly ActionEnum AnemoCollect = new("anemo_collect", Lang.S["GameTask_11165_5ea999"], ActionUseWaypointTypeEnum.Target);
    public static readonly ActionEnum PyroCollect = new("pyro_collect", Lang.S["GameTask_11164_48ba3b"], ActionUseWaypointTypeEnum.Target);

    public static readonly ActionEnum CombatScript = new("combat_script", Lang.S["GameTask_11163_bf4e71"], ActionUseWaypointTypeEnum.Custom); // 这个必须要 action_params 里面有脚本

    public static readonly ActionEnum Mining = new("mining", Lang.S["GameTask_11162_09401a"], ActionUseWaypointTypeEnum.Custom);
    public static readonly ActionEnum LogOutput = new("log_output", Lang.S["GameTask_11161_d3746b"], ActionUseWaypointTypeEnum.Custom);
    
    public static readonly ActionEnum Fishing = new("fishing", Lang.S["GameTask_10679_34e96b"], ActionUseWaypointTypeEnum.Custom);
    public static readonly ActionEnum ExitAndRelogin = new("exit_and_relogin", Lang.S["GameTask_11160_3b9f23"], ActionUseWaypointTypeEnum.Custom);

    public static readonly ActionEnum EnterAndExitWonderland =
        new("wonderland_cycle", Lang.S["GameTask_11159_b2f188"], ActionUseWaypointTypeEnum.Custom);
    public static readonly ActionEnum SetTime = new("set_time", Lang.S["GameTask_11158_db659b"], ActionUseWaypointTypeEnum.Custom);
    public static readonly ActionEnum UseGadget = new("use_gadget", Lang.S["GameTask_11153_b4d881"], ActionUseWaypointTypeEnum.Custom);
    public static readonly ActionEnum PickUpCollect = new("pick_up_collect", Lang.S["GameTask_11140_6578f4"], ActionUseWaypointTypeEnum.Custom);

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
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;

public class MoveModeEnum(string code, string msg)
{
    public static readonly MoveModeEnum Walk = new("walk", "步行");
    public static readonly MoveModeEnum Run = new("run", "奔跑");
    public static readonly MoveModeEnum Dash = new("dash", "持续冲刺");
    public static readonly MoveModeEnum Climb = new("climb", "攀爬");
    public static readonly MoveModeEnum Fly = new("fly", "飞行");
    public static readonly MoveModeEnum Jump = new("jump", "跳跃");
    public static readonly MoveModeEnum Swim = new("swim", "游泳");

    public static IEnumerable<MoveModeEnum> Values
    {
        get
        {
            yield return Walk;
            yield return Fly;
            yield return Jump;
            yield return Swim;
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

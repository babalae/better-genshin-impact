using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;

public class WaypointType(string code, string msg)
{
    public static readonly WaypointType Path = new("path", "途径点");
    public static readonly WaypointType Target = new("target", "目标点");
    public static readonly WaypointType Teleport = new("teleport", "传送点");
    public static readonly WaypointType Orientation = new("Orientation", "方位点");

    public static IEnumerable<WaypointType> Values
    {
        get
        {
            yield return Path;
            yield return Target;
            yield return Teleport;
            yield return Orientation;
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

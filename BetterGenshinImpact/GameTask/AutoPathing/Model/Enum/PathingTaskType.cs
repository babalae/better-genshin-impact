using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;

public class PathingTaskType(string code, string msg)
{
    public static readonly PathingTaskType Collect = new("collect", "采集");
    public static readonly PathingTaskType Mining = new("mining", "挖矿");
    public static readonly PathingTaskType Farming = new("farming", "锄地");

    public static IEnumerable<PathingTaskType> Values
    {
        get
        {
            yield return Collect;
            yield return Mining;
            yield return Farming;
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

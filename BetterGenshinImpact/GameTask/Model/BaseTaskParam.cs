
using BetterGenshinImpact.GameTask.AutoFishing;
using System.Globalization;
using System.Threading;

namespace BetterGenshinImpact.GameTask.Model;

/// <summary>
/// 独立任务参数基类
/// </summary>
public class BaseTaskParam
{
    public CultureInfo? GameCultureInfo { get; set; }
    public BaseTaskParam()
    {
    }
    public BaseTaskParam(CultureInfo? gameCultureInfo)
    {
        GameCultureInfo = gameCultureInfo;
    }
}

using System.Globalization;

namespace BetterGenshinImpact.GameTask.Model;

/// <summary>
/// 独立任务参数基类
/// </summary>
public abstract class BaseTaskParam
{
    /// <summary>
    /// 游戏语言CultureInfo。游戏内识别文本通过 <see cref="I18n.GameTextService"/> 按 <c>GameCultureInfo.Name</c> 获取
    /// </summary>
    public CultureInfo GameCultureInfo { get; private set; }

    protected BaseTaskParam(CultureInfo? gameCultureInfo)
    {
        GameCultureInfo = gameCultureInfo ?? new CultureInfo(TaskContext.Instance().Config.OtherConfig.GameCultureInfoName);
    }
}

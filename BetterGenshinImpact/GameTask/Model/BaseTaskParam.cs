using Microsoft.Extensions.Localization;
using System;
using System.Globalization;

namespace BetterGenshinImpact.GameTask.Model;

/// <summary>
/// 独立任务参数基类
/// </summary>
public abstract class BaseTaskParam<T> where T : class
{
    /// <summary>
    /// 游戏语言CultureInfo
    /// </summary>
    public CultureInfo GameCultureInfo { get; private set; }

    /// <summary>
    /// 多语言StringLocalizer
    /// 用于读取与 <typeparamref name="T"/> 同名的.resx文件中的多语言信息
    /// </summary>
    public IStringLocalizer<T> StringLocalizer { get; private set; }
    public BaseTaskParam(CultureInfo? gameCultureInfo, IStringLocalizer<T>? stringLocalizer)
    {
        GameCultureInfo = gameCultureInfo ?? new CultureInfo(TaskContext.Instance().Config.OtherConfig.GameCultureInfoName);
        StringLocalizer = stringLocalizer ?? App.GetService<IStringLocalizer<T>>() ?? throw new Exception();
    }
}

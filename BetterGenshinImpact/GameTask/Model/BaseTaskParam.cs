using BetterGenshinImpact.Service;
using Microsoft.Extensions.Localization;
using System;
using System.Globalization;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.GameTask.Model;

/// <summary>
/// 独立任务参数基类
/// </summary>
public abstract class BaseTaskParam<T> where T : class
{
    /// <summary>
    /// 游戏语言CultureInfo。运行时依赖，不参与参数 JSON。
    /// </summary>
    [JsonIgnore]
    public CultureInfo GameCultureInfo { get; private set; }

    /// <summary>
    /// 多语言StringLocalizer。运行时依赖，不参与参数 JSON。
    /// 用于读取与 <typeparamref name="T"/> 同名的.resx文件中的多语言信息
    /// </summary>
    [JsonIgnore]
    public IStringLocalizer<T> StringLocalizer { get; private set; }

    public BaseTaskParam(CultureInfo? gameCultureInfo, IStringLocalizer<T>? stringLocalizer)
    {
        // 参数类可以由脚本直接 new，没有创建方传入配置，语言回退暂时读配置根（见 task-context-removal.md 第 5 节）
        GameCultureInfo = gameCultureInfo ?? new CultureInfo(ConfigService.Config!.OtherConfig.GameCultureInfoName);
        StringLocalizer = stringLocalizer ?? App.GetService<IStringLocalizer<T>>() ?? throw new Exception();
    }
}

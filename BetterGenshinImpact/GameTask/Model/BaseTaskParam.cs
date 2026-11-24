using BetterGenshinImpact.Service;
using System.Globalization;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.GameTask.Model;

/// <summary>
/// 独立任务参数基类
/// </summary>
public abstract class BaseTaskParam
{
    /// <summary>
    /// 游戏语言CultureInfo。运行时依赖，不参与参数 JSON。
    /// 游戏内识别文本通过 <see cref="I18n.GameTextService"/> 按 <c>GameCultureInfo.Name</c> 获取
    /// </summary>
    [JsonIgnore]
    public CultureInfo GameCultureInfo { get; private set; }

    protected BaseTaskParam(CultureInfo? gameCultureInfo)
    {
        // 参数类可以由脚本直接 new，没有创建方传入配置，语言回退暂时读配置根（见 task-context-removal.md 第 5 节）
        GameCultureInfo = gameCultureInfo ?? new CultureInfo(ConfigService.Config!.OtherConfig.GameCultureInfoName);
    }
}

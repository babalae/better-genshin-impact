using BetterGenshinImpact.GameTask.Model;

namespace BetterGenshinImpact.GameTask.AutoMusicGame;

/// <summary>
/// 自动音游与自动演奏专辑的任务参数，目前没有业务字段，难度等设置仍来自全局配置
/// </summary>
public class AutoMusicGameParam : BaseTaskParam<AutoMusicGameTask>
{
    /// <summary>
    /// 创建默认参数，供 JSON 反序列化和默认参数使用
    /// </summary>
    public AutoMusicGameParam() : base(null, null)
    {
    }
}

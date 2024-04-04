using BetterGenshinImpact.Model;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.Model;

/// <summary>
/// 游戏各类任务的素材基类
/// 必须继承自BaseAssets
/// 且必须晚于TaskContext初始化，也就是 TaskContext.Instance().IsInitialized = true;
/// </summary>
/// <typeparam name="T"></typeparam>
public class BaseAssets<T> : Singleton<T> where T : class
{
    protected SystemInfo Info => TaskContext.Instance().SystemInfo;
    protected RECT CaptureRect => TaskContext.Instance().SystemInfo.CaptureAreaRect;
    protected double AssetScale => TaskContext.Instance().SystemInfo.AssetScale;
}

using BetterGenshinImpact.Model;
using OpenCvSharp;
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
    protected Rect CaptureRect => TaskContext.Instance().SystemInfo.ScaleCaptureAreaRect;
    protected double AssetScale => TaskContext.Instance().SystemInfo.RealAssetScale;
}

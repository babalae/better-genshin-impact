using BetterGenshinImpact.Model;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Model;

/// <summary>
/// 游戏各类任务的素材基类
/// 必须继承自BaseAssets
/// 且必须晚于TaskContext初始化，也就是 TaskContext.Instance().IsInitialized = true;
/// 在整个任务生命周期开始时,必须先使用 DestroyInstance() 销毁实例,保证资源的类型正确引用
/// </summary>
/// <typeparam name="T"></typeparam>
public class BaseAssets<T> : Singleton<T> where T : class
{
    protected Rect CaptureRect => TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect;
    protected double AssetScale => TaskContext.Instance().SystemInfo.AssetScale;
}

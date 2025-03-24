using BetterGenshinImpact.Model;
using OpenCvSharp;
using System;
using System.Threading;

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
    protected readonly ISystemInfo systemInfo;

    protected BaseAssets()
    {
        this.systemInfo = TaskContext.Instance().SystemInfo;
    }

    protected BaseAssets(ISystemInfo systemInfo)
    {
        this.systemInfo = systemInfo;
    }

    protected Rect CaptureRect => systemInfo.ScaleMax1080PCaptureRect;
    protected double AssetScale => systemInfo.AssetScale;
    // private int _gameWidth;
    // private int _gameHeight;
    //
    // public new static T Instance
    // {
    //     get
    //     {
    //         // 统一在这里处理 重新生成实例
    //         if (_instance != null)
    //         {
    //             var r = TaskContext.Instance().SystemInfo.CaptureAreaRect;
    //             if (_instance is BaseAssets<T> baseAssets)
    //             {
    //                 if (baseAssets._gameWidth != r.Width || baseAssets._gameHeight != r.Height)
    //                 {
    //                     baseAssets._gameWidth = r.Width;
    //                     baseAssets._gameHeight = r.Height;
    //                     _instance = null;
    //                 }
    //             }
    //         }
    //         return LazyInitializer.EnsureInitialized(ref _instance, ref syncRoot, CreateInstance);
    //     }
    // }
}

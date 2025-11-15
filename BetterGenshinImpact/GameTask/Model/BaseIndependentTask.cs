using OpenCvSharp;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.Model;

public class BaseIndependentTask
{
    protected ISystemInfo Info => TaskContext.Instance().SystemInfo;
    protected Rect CaptureRect => TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect;
    protected double AssetScale => TaskContext.Instance().SystemInfo.AssetScale;
}

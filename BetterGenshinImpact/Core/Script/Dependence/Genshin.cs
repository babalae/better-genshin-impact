using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.Common.Job;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Script.Dependence;

public class Genshin
{
    private RECT captureAreaRect = TaskContext.Instance().SystemInfo.CaptureAreaRect;

    /// <summary>
    /// 游戏宽度
    /// </summary>
    public int Width => captureAreaRect.Width;

    /// <summary>
    /// 游戏高度
    /// </summary>
    public int Height => captureAreaRect.Height;

    /// <summary>
    /// 游戏窗口大小相比1080P的缩放比例
    /// </summary>
    public double ScaleTo1080PRatio { get; } = TaskContext.Instance().SystemInfo.ScaleTo1080PRatio;

    /// <summary>
    /// 系统屏幕的DPI缩放比例
    /// </summary>
    public double ScreenDpiScale => TaskContext.Instance().DpiScale;

    /// <summary>
    /// 传送到指定位置
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns></returns>
    public async Task Tp(double x, double y)
    {
        await new TpTask(CancellationContext.Instance.Cts.Token).Tp(x, y);
    }

    public async Task Tp(double x, double y, bool force)
    {
        await new TpTask(CancellationContext.Instance.Cts.Token).Tp(x, y, force);
    }

    /// <summary>
    /// 传送到指定位置
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns></returns>
    public async Task Tp(string x, string y)
    {
        double.TryParse(x, out var dx);
        double.TryParse(y, out var dy);
        await Tp(dx, dy);
    }

    public async Task Tp(string x, string y, bool force)
    {
        double.TryParse(x, out var dx);
        double.TryParse(y, out var dy);
        await Tp(dx, dy, force);
    }

    /// <summary>
    /// 切换队伍
    /// </summary>
    /// <param name="partyName">队伍界面自定义的队伍名称</param>
    /// <returns></returns>
    public async Task SwitchParty(string partyName)
    {
        await new SwitchPartyTask().Start(partyName, CancellationContext.Instance.Cts.Token);
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoSkip.Assets;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.View.Drawable;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

public class SetTimeTask
{
    // 圆心坐标
    private const double CenterX = 1441;
    private const double CenterY = 501.6;

    private readonly ReturnMainUiTask _returnMainUiTask = new();

    public async Task Start(int hour, int minute, CancellationToken ct, bool skipTimeAdjustmentAnimation = false)
    {
        try
        {
            await _returnMainUiTask.Start(ct);
            await DoOnce(hour, minute, ct, skipTimeAdjustmentAnimation);
        }
        catch (Exception e)
        {
            Logger.LogDebug(e, "设置时间异常");
            Logger.LogError("设置时间异常: {Msg}", e.Message);
        }
        finally
        {
            VisionContext.Instance().DrawContent.ClearAll();
        }
    }

    public async Task DoOnce(int hour, int minute, CancellationToken ct, bool skipTimeAdjustmentAnimation = false)
    {
        // 半径
        const int r1 = 30;
        const int r2 = 150;
        const int r3 = 300;
        const int stepDuration = 50;
        int h = (int)Math.Floor(hour + minute / 60.0);
        int m = hour * 60 + minute - h * 60;
        h = ((h % 24) + 24) % 24;
        Logger.LogInformation($"设置时间到 {h} 点 {m} 分");
        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
        await Delay(1000, ct);
        GameCaptureRegion.GameRegion1080PPosClick(50, 700);
        await Delay(2000, ct);
        await SetTime(h, m, r1, r2, r3, stepDuration, ct);
        await Delay(1000, ct);
        GameCaptureRegion.GameRegion1080PPosClick(1500, 1000); // 确认
        
        if (skipTimeAdjustmentAnimation)
        {
            // 跳过调整动画
            await Delay(1, ct);
            await CancelAnimation(ct);
            await Delay(1000, ct);
            GameCaptureRegion.GameRegion1080PPosClick(45, 715);
            await Delay(600, ct);
            await _returnMainUiTask.Start(ct);
        }
        else
        {
            await Delay(3000, ct);
            // 出现X的时候代表时间切换成功
            await NewRetry.WaitForAction(() => CaptureToRectArea().Find(ElementAssets.Instance.PageCloseWhiteRo).IsExist(), ct, 25);
            await _returnMainUiTask.Start(ct);
        }
    }

    // 取消动画函数
    private async Task CancelAnimation(CancellationToken ct)
    {
        GameCaptureRegion.GameRegion1080PPosMove(200, 200);
        Simulation.SendInput.Mouse.LeftButtonDown();
        await Delay(10, ct);
        Simulation.SendInput.Mouse.LeftButtonUp();
    }

    double[] GetPosition(double r, double index)
    {
        double angle = index * Math.PI / 720;
        return [CenterX + r * Math.Cos(angle), CenterY + r * Math.Sin(angle)];
    }

    async Task MouseClick(double x, double y, int stepDuration, CancellationToken ct)
    {
        GameCaptureRegion.GameRegion1080PPosMove(x, y);
        await Delay(50, ct);
        Simulation.SendInput.Mouse.LeftButtonDown();
        await Delay(50, ct);
        Simulation.SendInput.Mouse.LeftButtonUp();
        await Delay(stepDuration, ct);
    }

    async Task MouseClickAndMove(double x1, double y1, double x2, double y2, int stepDuration, CancellationToken ct)
    {
        GameCaptureRegion.GameRegion1080PPosMove(x1, y1);
        await Delay(50, ct);
        Simulation.SendInput.Mouse.LeftButtonDown();
        await Delay(50, ct);
        GameCaptureRegion.GameRegion1080PPosMove(x2, y2);
        await Delay(50, ct);
        Simulation.SendInput.Mouse.LeftButtonUp();
        await Delay(stepDuration, ct);
    }

    async Task SetTime(int hour, int minute, int r1, int r2, int r3, int stepDuration, CancellationToken ct)
    {
        int end = (hour + 6) * 60 + minute - 20;
        int n = 3;
        for (int i = -n + 1; i < 1; i++)
        {
            double[] position = GetPosition(r1, end + i * 1440.0 / n);
            await MouseClick(position[0], position[1], stepDuration, ct);
        }

        double[] position1 = GetPosition(r2, end + 5);
        double[] position2 = GetPosition(r3, end + 20 + 0.5);
        await MouseClickAndMove(position1[0], position1[1], position2[0], position2[1], stepDuration, ct);
    }
}
using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoMusicGame.Assets;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoMusicGame;

/// <summary>
/// 自动音乐专辑
/// </summary>
public class AutoAlbumTask(AutoMusicGameParam taskParam) : ISoloTask
{
    public string Name => "自动音游专辑";

    public async Task Start(CancellationToken ct)
    {
        try
        {
            AutoMusicGameTask.Init();
            Logger.LogInformation("开始自动演奏整个专辑未完成的音乐");
            await StartOneAlbum(ct);
        }
        catch (Exception e)
        {
            Logger.LogError("自动音乐专辑任务异常:{Msg}", e.Message);
        }
    }

    public async Task StartOneAlbum(CancellationToken ct)
    {
        // 12个音乐
        for (int i = 0; i < 13; i++)
        {
            using var iconRa = CaptureToRectArea().Find(AutoMusicAssets.Instance.UiLeftTopAlbumIcon);
            if (!iconRa.IsExist())
            {
                throw new Exception("当前未处于专辑界面，请在专辑界面运行本任务");
            }

            using var doneRa = CaptureToRectArea().Find(AutoMusicAssets.Instance.AlbumMusicComplate);
            if (doneRa.IsExist())
            {
                Logger.LogInformation("当前音乐{Num}所有奖励已领取，切换下一首", i + 1);
                GameCaptureRegion.GameRegion1080PPosClick(310, 220);
                await Delay(800, ct);
                continue;
            }

            Logger.LogInformation("当前音乐{Num}存在未领取奖励，前往演奏", i + 1);
            Bv.ClickWhiteConfirmButton(CaptureToRectArea());
            await Delay(800, ct);
            // 点击传说
            GameCaptureRegion.GameRegion1080PPosClick(1400, 600);
            await Delay(200, ct);
            // 演奏
            Bv.ClickWhiteConfirmButton(CaptureToRectArea());
            await Delay(500, ct);

            CancellationTokenSource cts = new();
            ct.Register(cts.Cancel);

            // 演奏结束检查任务
            var checkTask = Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    await Delay(5000, ct); // n秒检查一次
                    using var listRa = CaptureToRectArea().Find(AutoMusicAssets.Instance.BtnList);
                    if (listRa.IsExist())
                    {
                        listRa.Click();
                        return;
                    }
                }
            }, cts.Token);

            // 演奏任务
            var musicTask = new AutoMusicGameTask(taskParam).StartWithOutInit(cts.Token);

            // 等待任意一个任务完成
            await Task.WhenAny(checkTask, musicTask);
            Logger.LogInformation("当前音乐{Num}演奏结束", i + 1);
            await Delay(2000, ct);
        }
        Logger.LogInformation("当前专辑所有音乐演奏结束");
    }
}
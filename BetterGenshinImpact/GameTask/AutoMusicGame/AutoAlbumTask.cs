using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoMusicGame.Assets;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoMusicGame;

/// <summary>
/// 自动音乐专辑
/// </summary>
public class AutoAlbumTask(AutoMusicGameParam taskParam) : ISoloTask
{
    public string Name => "自动音游专辑";

    private AutoMusicGameTask _autoMusicGameTask = new AutoMusicGameTask(taskParam);

    public async Task Start(CancellationToken ct)
    {
        try
        {
            AutoMusicGameTask.Init();
            Notify.Event(NotificationEvent.AlbumStart).Success("自动音游专辑启动");
            Logger.LogInformation("开始自动演奏整个专辑未完成的音乐");
            await StartOneAlbum(ct);
            Notify.Event(NotificationEvent.AlbumEnd).Success("自动音游专辑结束");
        }
        catch (NormalEndException e)
        {
            Logger.LogError("手动取消任务 - {Msg}", e.Message);
        }
        catch (Exception e)
        {
            Logger.LogError("自动音乐专辑任务异常:{Msg}", e.Message);
            Logger.LogDebug(e, "自动音乐专辑任务异常详情");
            Notify.Event(NotificationEvent.AlbumError).Error("自动音游专辑异常", e);
        }
    }

    public async Task StartOneAlbum(CancellationToken ct)
    {
        using var ra1 = CaptureToRectArea();
        using var iconRa = ra1.Find(AutoMusicAssets.Instance.UiLeftTopAlbumIcon);
        if (!iconRa.IsExist())
        {
            throw new Exception("当前未处于主题专辑界面，请在专辑界面运行本任务。注意全部歌曲列表页面无法运行本任务！");
        }
        else
        {
            // OCR 后再次判断，区分是否是全部歌曲页面
            var ocrRes = ra1.DeriveCrop(iconRa.Right, iconRa.Top, ra1.Width * 0.16, iconRa.Height).FindMulti(RecognitionObject.OcrThis);
            if (ocrRes.Any(region => region.Text.Contains("全部")))
            {
                throw new Exception("当前在全部歌曲页面，此页面无法运行本任务。请返回到主界面选择专辑列表中以国家为主题的专辑页！");
            }
        }

        var musicLevel = TaskContext.Instance().Config.AutoMusicGameConfig.MusicLevel;
        if (string.IsNullOrEmpty(musicLevel))
        {
            musicLevel = "传说";
        }

        Logger.LogInformation("自动音游乐曲难度等级：{Text}", musicLevel);

        // 遍历4个难度等级
        var defaultDifficultyLevels = new[]
        {
            ("普通", 480, 600, AutoMusicAssets.Instance.MusicCanorusLevel1),
            ("困难", 800, 600, AutoMusicAssets.Instance.MusicCanorusLevel2),
            ("大师", 1150, 600, AutoMusicAssets.Instance.MusicCanorusLevel3),
            ("传说", 1400, 600, AutoMusicAssets.Instance.MusicCanorusLevel4)
        };

        var difficultyLevels = defaultDifficultyLevels;
        if (musicLevel != "所有")
        {
            difficultyLevels = [Array.Find(defaultDifficultyLevels, level => level.Item1 == musicLevel)];
        }

        foreach (var (difficultyName, xPos, yPos, canorusAsset) in difficultyLevels)
        {
            Logger.LogInformation("开始演奏{Difficulty}难度的乐曲", difficultyName);

            // 每个难度12首曲子
            for (int i = 0; i < 13; i++)
            {
                if (TaskContext.Instance().Config.AutoMusicGameConfig.MustCanorusLevel)
                {
                    using var canoraRa = CaptureToRectArea().Find(canorusAsset);
                    if (canoraRa.IsExist())
                    {
                        Logger.LogInformation("乐曲{Num} - {Difficulty}级别：已完成【大音天籁】，切换下一首", i + 1, difficultyName);
                        GameCaptureRegion.GameRegion1080PPosClick(310, 220);
                        await Delay(800, ct);
                        continue;
                    }

                    Logger.LogInformation("第{Num}首{Difficulty}难度的乐曲：{Message}", i + 1, difficultyName, "没有完成【大音天籁】");
                }
                else
                {
                    using var doneRa = CaptureToRectArea().Find(AutoMusicAssets.Instance.AlbumMusicComplate);
                    if (doneRa.IsExist())
                    {
                        Logger.LogInformation("当前乐曲{Num}所有奖励已领取，切换下一首", i + 1);
                        GameCaptureRegion.GameRegion1080PPosClick(310, 220);
                        await Delay(800, ct);
                        continue;
                    }

                    Logger.LogInformation("当前乐曲{Num}存在未领取奖励，前往演奏", i + 1);
                }


                // 点击确认按钮
                Bv.ClickWhiteConfirmButton(CaptureToRectArea());
                await Delay(800, ct);

                // 选择难度
                GameCaptureRegion.GameRegion1080PPosClick(xPos, yPos);
                await Delay(200, ct);

                // 开始演奏
                Bv.ClickWhiteConfirmButton(CaptureToRectArea());
                await Delay(500, ct);

                var cts = new CancellationTokenSource();
                ct.Register(cts.Cancel);

                // 演奏结束检查任务
                var checkTask = Task.Run(async () =>
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        await Delay(5000, ct); // 每5秒检查一次
                        using var listRa = CaptureToRectArea().Find(AutoMusicAssets.Instance.BtnList);
                        if (listRa.IsExist())
                        {
                            Logger.LogDebug("检测到返回列表按钮，演奏结束");
                            listRa.Click();
                            return;
                        }
                    }
                }, cts.Token);

                // 演奏任务
                var musicTask = _autoMusicGameTask.StartWithOutInit(cts.Token);

                // 等待任意一个任务完成
                await Task.WhenAny(checkTask, musicTask);
                await cts.CancelAsync();

                Logger.LogInformation("第{Num}首{Difficulty}难度乐曲演奏完成", i + 1, difficultyName);
                await Delay(2000, ct);

                await Bv.WaitUntilFound(AutoMusicAssets.Instance.UiLeftTopAlbumIcon, ct);
                Logger.LogDebug("切换到下一首乐曲");
                GameCaptureRegion.GameRegion1080PPosClick(310, 220);
                await Delay(800, ct);
            }

            Logger.LogInformation("完成{Difficulty}难度所有乐曲的演奏", difficultyName);
        }

        Logger.LogInformation("当前专辑所有乐曲演奏结束");
    }
}
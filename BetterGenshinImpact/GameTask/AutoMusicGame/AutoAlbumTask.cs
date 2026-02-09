using BetterGenshinImpact.Helpers;
﻿using System;
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
    public string Name => Lang.S["GameTask_10987_386c54"];

    private AutoMusicGameTask _autoMusicGameTask = new AutoMusicGameTask(taskParam);

    public async Task Start(CancellationToken ct)
    {
        try
        {
            AutoMusicGameTask.Init();
            Notify.Event(NotificationEvent.AlbumStart).Success(Lang.S["GameTask_10986_5cd6b1"]);
            Logger.LogInformation(Lang.S["GameTask_10985_e627df"]);
            await StartOneAlbum(ct);
            Notify.Event(NotificationEvent.AlbumEnd).Success(Lang.S["GameTask_10984_387998"]);
        }
        catch (NormalEndException e)
        {
            Logger.LogError(Lang.S["GameTask_10983_e0e768"], e.Message);
        }
        catch (Exception e)
        {
            Logger.LogError(Lang.S["GameTask_10982_8bf384"], e.Message);
            Logger.LogDebug(e, Lang.S["GameTask_10981_5ed576"]);
            Notify.Event(NotificationEvent.AlbumError).Error(Lang.S["GameTask_10980_760f64"], e);
        }
    }

    public async Task StartOneAlbum(CancellationToken ct)
    {
        using var ra1 = CaptureToRectArea();
        using var iconRa = ra1.Find(AutoMusicAssets.Instance.UiLeftTopAlbumIcon);
        if (!iconRa.IsExist())
        {
            throw new Exception(Lang.S["GameTask_10979_30d5b2"]);
        }
        else
        {
            // OCR 后再次判断，区分是否是全部歌曲页面
            var ocrRes = ra1.DeriveCrop(iconRa.Right, iconRa.Top, ra1.Width * 0.16, iconRa.Height).FindMulti(RecognitionObject.OcrThis);
            if (ocrRes.Any(region => region.Text.Contains(Lang.S["Gen_10024_a8b0c2"])))
            {
                throw new Exception(Lang.S["GameTask_10978_495e6f"]);
            }
        }

        var musicLevel = TaskContext.Instance().Config.AutoMusicGameConfig.MusicLevel;
        if (string.IsNullOrEmpty(musicLevel))
        {
            musicLevel = Lang.S["GameTask_10973_e84ebf"];
        }

        Logger.LogInformation(Lang.S["GameTask_10977_5c0044"], musicLevel);

        // 遍历4个难度等级
        var defaultDifficultyLevels = new[]
        {
            (Lang.S["GameTask_10976_35242c"], 480, 600, AutoMusicAssets.Instance.MusicCanorusLevel1),
            (Lang.S["GameTask_10975_d6f39f"], 800, 600, AutoMusicAssets.Instance.MusicCanorusLevel2),
            (Lang.S["GameTask_10974_a4e6a6"], 1150, 600, AutoMusicAssets.Instance.MusicCanorusLevel3),
            (Lang.S["GameTask_10973_e84ebf"], 1400, 600, AutoMusicAssets.Instance.MusicCanorusLevel4)
        };

        var difficultyLevels = defaultDifficultyLevels;
        if (musicLevel != Lang.S["GameTask_10972_9a7b52"])
        {
            difficultyLevels = [Array.Find(defaultDifficultyLevels, level => level.Item1 == musicLevel)];
        }

        foreach (var (difficultyName, xPos, yPos, canorusAsset) in difficultyLevels)
        {
            Logger.LogInformation(Lang.S["GameTask_10971_e92689"], difficultyName);

            // 每个难度12首曲子
            for (int i = 0; i < 13; i++)
            {
                if (TaskContext.Instance().Config.AutoMusicGameConfig.MustCanorusLevel)
                {
                    using var canoraRa = CaptureToRectArea().Find(canorusAsset);
                    if (canoraRa.IsExist())
                    {
                        Logger.LogInformation(Lang.S["GameTask_10970_3363ff"], i + 1, difficultyName);
                        GameCaptureRegion.GameRegion1080PPosClick(310, 220);
                        await Delay(800, ct);
                        continue;
                    }

                    Logger.LogInformation(Lang.S["GameTask_10968_283b10"], i + 1, difficultyName, "没有完成【大音天籁】");
                }
                else
                {
                    using var doneRa = CaptureToRectArea().Find(AutoMusicAssets.Instance.AlbumMusicComplate);
                    if (doneRa.IsExist())
                    {
                        Logger.LogInformation(Lang.S["GameTask_10967_c5dc63"], i + 1);
                        GameCaptureRegion.GameRegion1080PPosClick(310, 220);
                        await Delay(800, ct);
                        continue;
                    }

                    Logger.LogInformation(Lang.S["GameTask_10966_722aca"], i + 1);
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
                            Logger.LogDebug(Lang.S["GameTask_10965_0482bd"]);
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

                Logger.LogInformation(Lang.S["GameTask_10964_6844c4"], i + 1, difficultyName);
                await Delay(2000, ct);

                await Bv.WaitUntilFound(AutoMusicAssets.Instance.UiLeftTopAlbumIcon, ct);
                Logger.LogDebug(Lang.S["GameTask_10963_24eeb9"]);
                GameCaptureRegion.GameRegion1080PPosClick(310, 220);
                await Delay(800, ct);
            }

            Logger.LogInformation(Lang.S["GameTask_10962_452a60"], difficultyName);
        }

        Logger.LogInformation(Lang.S["GameTask_10961_14bd96"]);
    }
}
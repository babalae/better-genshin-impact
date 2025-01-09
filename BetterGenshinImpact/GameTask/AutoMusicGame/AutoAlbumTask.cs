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

    private AutoMusicGameTask _autoMusicGameTask = new AutoMusicGameTask(taskParam);

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
        using var iconRa = CaptureToRectArea().Find(AutoMusicAssets.Instance.UiLeftTopAlbumIcon);
        if (!iconRa.IsExist())
        {
            throw new Exception("当前未处于专辑界面，请在专辑界面运行本任务");
        }

        var modeName = TaskContext.Instance().Config.AutoMusicGameConfig.ModeName;
        if (string.IsNullOrEmpty(modeName))
        {
            modeName = AutoMusicGameConfig.MusicModelList[0];
        }
        Logger.LogInformation("自动音游模式：{Text}", modeName);
        
        var difficultyLevels = new[]
        {
            ("大师", 1150, 600, AutoMusicAssets.Instance.MusicCanorusLevel3)
        };
        if (modeName == AutoMusicGameConfig.MusicModelList[1])
        {
            // 遍历4个难度等级
            difficultyLevels =
            [
                ("普通", 480, 600, AutoMusicAssets.Instance.MusicCanorusLevel1),
                ("困难", 800, 600, AutoMusicAssets.Instance.MusicCanorusLevel2), 
                ("大师", 1150, 600, AutoMusicAssets.Instance.MusicCanorusLevel3),
                ("传说", 1400, 600, AutoMusicAssets.Instance.MusicCanorusLevel4)
            ];
        }

        foreach (var (difficultyName, xPos, yPos, canorusAsset) in difficultyLevels)
        {
            Logger.LogInformation("开始演奏{Difficulty}难度的乐曲", difficultyName);
            
            // 每个难度12首曲子
            for (int i = 0; i < 13; i++)
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
                
                // 点击确认按钮
                Bv.ClickWhiteConfirmButton(CaptureToRectArea());
                await Delay(800, ct);
                
                // 选择难度
                GameCaptureRegion.GameRegion1080PPosClick(xPos, yPos);
                await Delay(200, ct);
                
                // 开始演奏
                Bv.ClickWhiteConfirmButton(CaptureToRectArea());
                await Delay(500, ct);

                using var cts = new CancellationTokenSource();
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
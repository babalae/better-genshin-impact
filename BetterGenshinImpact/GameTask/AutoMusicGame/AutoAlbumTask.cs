using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoMusicGame;

/// <summary>
/// 自动音乐专辑：在主题专辑界面逐首演奏未完成的乐曲，演奏部分组合 <see cref="AutoMusicGameTask"/>
/// </summary>
[SoloTaskInfo(TaskId, "自动演奏专辑", Group = "小游戏", Order = 530, Scopes = SoloTaskScopes.Standalone,
    WaitForMainUi = false)]
public class AutoAlbumTask : ISoloTask<AutoMusicGameParam, NoData>
{
    /// <summary>
    /// 稳定任务标识
    /// </summary>
    public const string TaskId = "auto_album";

    /// <summary>
    /// 本次运行使用的运行环境：截图、等待与点击都只使用它
    /// </summary>
    private readonly GameRuntime _runtime;

    /// <summary>
    /// 配置根，由创建方传入
    /// </summary>
    private readonly AllConfig _config;

    /// <summary>
    /// 负责单首乐曲演奏的音游任务，每首曲子用本次参数调用一次 RunAsync
    /// </summary>
    private readonly AutoMusicGameTask _autoMusicGameTask;

    /// <summary>
    /// 本次运行的任务参数，在 <see cref="RunAsync"/> 开头赋值，演奏每首曲子时传给音游任务
    /// </summary>
    private AutoMusicGameParam _taskParam = null!;

    /// <summary>
    /// 创建自动音游专辑任务
    /// </summary>
    /// <param name="runtime">本次运行使用的运行环境</param>
    /// <param name="config">配置根</param>
    public AutoAlbumTask(GameRuntime runtime, AllConfig config)
    {
        _runtime = runtime;
        _config = config;
        _autoMusicGameTask = new AutoMusicGameTask(runtime, config);
    }

    /// <summary>
    /// 运行自动演奏专辑。不在主题专辑界面时返回失败；取消和运行环境停止时抛出异常
    /// </summary>
    /// <param name="param">任务参数，演奏每首曲子时原样传给音游任务</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>任务结果</returns>
    public async Task<SoloTaskResult<NoData>> RunAsync(AutoMusicGameParam param, CancellationToken ct)
    {
        _taskParam = param;
        try
        {
            AutoMusicGameTask.Init(_runtime);
            Notify.Event(NotificationEvent.AlbumStart).Success("自动音游专辑启动");
            Logger.LogInformation("开始自动演奏整个专辑未完成的音乐");
            var result = await StartOneAlbum(ct);
            if (result.Kind == SoloTaskResultKind.Failed)
            {
                // 保留原来的专辑异常通知
                Notify.Event(NotificationEvent.AlbumError).Error($"自动音游专辑异常：{result.Message}");
                return result;
            }

            Notify.Event(NotificationEvent.AlbumEnd).Success("自动音游专辑结束");
            return result;
        }
        catch (Exception e) when (e is not OperationCanceledException and not NormalEndException)
        {
            // 取消与运行环境停止直接向上抛出；其他异常保留专辑异常通知后交给调用方记录
            Logger.LogDebug(e, "自动音乐专辑任务异常详情");
            Notify.Event(NotificationEvent.AlbumError).Error("自动音游专辑异常", e);
            throw;
        }
    }

    /// <summary>
    /// 演奏当前专辑中未完成的乐曲
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>不在主题专辑界面时返回失败，全部演奏完毕返回未确认</returns>
    private async Task<SoloTaskResult<NoData>> StartOneAlbum(CancellationToken ct)
    {
        using var ra1 = _runtime.CaptureToRectArea();
        using var iconRa = ra1.Find(RecognitionAssets.Get("AutoMusicGame", "UiLeftTopAlbumIcon", ra1));
        if (!iconRa.IsExist())
        {
            return SoloTaskResult.Failed(SoloTaskReasons.PageNotReached,
                "当前未处于主题专辑界面，请在专辑界面运行本任务。注意全部歌曲列表页面无法运行本任务！");
        }
        else
        {
            // OCR 后再次判断，区分是否是全部歌曲页面
            using var ocrArea = ra1.DeriveCrop(iconRa.Right, iconRa.Top, ra1.Width * 0.16, iconRa.Height);
            var ocrRes = ocrArea.FindMulti(RecognitionObject.OcrThis);
            if (ocrRes.Any(region => region.Text.Contains("全部")))
            {
                return SoloTaskResult.Failed(SoloTaskReasons.PageNotReached,
                    "当前在全部歌曲页面，此页面无法运行本任务。请返回到主界面选择专辑列表中以国家为主题的专辑页！");
            }
        }

        var musicLevel = _config.AutoMusicGameConfig.MusicLevel;
        if (string.IsNullOrEmpty(musicLevel))
        {
            musicLevel = "传说";
        }

        Logger.LogInformation("自动音游乐曲难度等级：{Text}", musicLevel);

        // 遍历4个难度等级
        var defaultDifficultyLevels = new[]
        {
            ("普通", 480, 600, RecognitionAssets.Get("AutoMusicGame", "MusicCanorusLevel1", ra1)),
            ("困难", 800, 600, RecognitionAssets.Get("AutoMusicGame", "MusicCanorusLevel2", ra1)),
            ("大师", 1150, 600, RecognitionAssets.Get("AutoMusicGame", "MusicCanorusLevel3", ra1)),
            ("传说", 1400, 600, RecognitionAssets.Get("AutoMusicGame", "MusicCanorusLevel4", ra1))
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
                if (_config.AutoMusicGameConfig.MustCanorusLevel)
                {
                    using var canoraArea = _runtime.CaptureToRectArea();
                    using var canoraRa = canoraArea.Find(canorusAsset);
                    if (canoraRa.IsExist())
                    {
                        Logger.LogInformation("乐曲{Num} - {Difficulty}级别：已完成【大音天籁】，切换下一首", i + 1, difficultyName);
                        _runtime.GameRegion1080PPosClick(310, 220);
                        await _runtime.Delay(800, ct);
                        continue;
                    }

                    Logger.LogInformation("第{Num}首{Difficulty}难度的乐曲：{Message}", i + 1, difficultyName, "没有完成【大音天籁】");
                }
                else
                {
                    using var doneArea = _runtime.CaptureToRectArea();
                    using var doneRa = doneArea.Find(RecognitionAssets.Get("AutoMusicGame", "AlbumMusicComplate", doneArea));
                    if (doneRa.IsExist())
                    {
                        Logger.LogInformation("当前乐曲{Num}所有奖励已领取，切换下一首", i + 1);
                        _runtime.GameRegion1080PPosClick(310, 220);
                        await _runtime.Delay(800, ct);
                        continue;
                    }

                    Logger.LogInformation("当前乐曲{Num}存在未领取奖励，前往演奏", i + 1);
                }


                // 点击确认按钮
                using (var confirmArea = _runtime.CaptureToRectArea())
                {
                    Bv.ClickWhiteConfirmButton(confirmArea);
                }
                await _runtime.Delay(800, ct);

                // 选择难度
                _runtime.GameRegion1080PPosClick(xPos, yPos);
                await _runtime.Delay(200, ct);

                // 开始演奏
                using (var startArea = _runtime.CaptureToRectArea())
                {
                    Bv.ClickWhiteConfirmButton(startArea);
                }
                await _runtime.Delay(500, ct);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

                // 演奏结束检查任务
                var checkTask = Task.Run(async () =>
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        await _runtime.Delay(5000, cts.Token); // 每5秒检查一次
                        using var listArea = _runtime.CaptureToRectArea();
                        using var listRa = listArea.Find(RecognitionAssets.Get("AutoMusicGame", "BtnList", listArea));
                        if (listRa.IsExist())
                        {
                            Logger.LogDebug("检测到返回列表按钮，演奏结束");
                            listRa.Click();
                            return;
                        }
                    }
                }, cts.Token);

                // 演奏任务：组合调用音游任务，用本次参数运行，直到 cts 被取消
                var musicTask = _autoMusicGameTask.RunAsync(_taskParam, cts.Token);

                // 任意一侧先结束，就说明这一首歌已经可以开始收尾了。
                await Task.WhenAny(checkTask, musicTask);
                // 通知另一侧尚未结束的任务尽快退出。
                await cts.CancelAsync();

                try
                {
                    // 等待两个任务都真正结束，避免后台轮询或按键任务拖到下一首。
                    await Task.WhenAll(checkTask, musicTask);
                }
                catch (Exception ex) when (
                    (ex is OperationCanceledException || ex is NormalEndException)
                    && cts.IsCancellationRequested
                    && !ct.IsCancellationRequested)
                {
                    // 忽略主动停止另一侧任务时产生的取消或正常结束异常
                }

                Logger.LogInformation("第{Num}首{Difficulty}难度乐曲演奏完成", i + 1, difficultyName);
                await _runtime.Delay(2000, ct);

                await Bv.WaitUntilFound(_runtime, RecognitionAssets.Get("AutoMusicGame", "UiLeftTopAlbumIcon", _runtime.SystemInfo), ct);
                Logger.LogDebug("切换到下一首乐曲");
                _runtime.GameRegion1080PPosClick(310, 220);
                await _runtime.Delay(800, ct);
            }

            Logger.LogInformation("完成{Difficulty}难度所有乐曲的演奏", difficultyName);
        }

        Logger.LogInformation("当前专辑所有乐曲演奏结束");
        return SoloTaskResult.Unverified("当前专辑所有乐曲演奏结束");
    }
}

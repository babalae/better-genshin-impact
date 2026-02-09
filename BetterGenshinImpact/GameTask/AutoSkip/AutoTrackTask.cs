using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoSkip.Model;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.QuickTeleport.Assets;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.View.Drawable;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoSkip;

public class AutoTrackTask(AutoTrackParam param) : BaseIndependentTask
{
    // /// <summary>
    // /// 准备好前进了
    // /// </summary>
    // private bool _readyMoveForward = false;

    /// <summary>
    /// 任务距离
    /// </summary>
    private Rect _missionDistanceRect = default;

    private CancellationToken _ct;

    public async void Start()
    {
        var hasLock = false;
        try
        {
            hasLock = await TaskSemaphore.WaitAsync(0);
            if (!hasLock)
            {
                Logger.LogError(Lang.S["GameTask_11274_80b0bb"]);
                return;
            }

            SystemControl.ActivateWindow();

            Logger.LogInformation("→ {Text}", Lang.S["GameTask_11273_d2ad58"]);

            _ct = CancellationContext.Instance.Cts.Token;

            TrackMission();
        }
        catch (NormalEndException e)
        {
            Logger.LogInformation(Lang.S["GameTask_11272_c0666f"] + e.Message);
        }
        catch (Exception e)
        {
            Logger.LogError(e.Message);
            Logger.LogDebug(e.StackTrace);
        }
        finally
        {
            VisionContext.Instance().DrawContent.ClearAll();
            Logger.LogInformation("→ {Text}", Lang.S["GameTask_11271_8a1a01"]);

            if (hasLock)
            {
                TaskSemaphore.Release();
            }
        }
    }

    private void TrackMission()
    {
        // 确认在主界面才会执行跟随任务
        var ra = CaptureToRectArea();
        var paimonMenuRa = ra.Find(ElementAssets.Instance.PaimonMenuRo);
        if (!paimonMenuRa.IsExist())
        {
            Sleep(5000, _ct);
            return;
        }

        // 任务文字有动效，等待2s重新截图
        Simulation.SendInput.Mouse.MoveMouseBy(0, 7000);
        Sleep(2000, _ct);

        // OCR 任务文字 在小地图下方
        var textRaList = OcrMissionTextRaList(paimonMenuRa);
        if (textRaList.Count == 0)
        {
            Logger.LogInformation(Lang.S["GameTask_11270_164bd6"]);
            Sleep(5000, _ct);
            return;
        }

        // 从任务文字中提取距离
        var distance = GetDistanceFromMissionText(textRaList);
        Logger.LogInformation(Lang.S["GameTask_11258_42a0d0"], "距离" + distance + "m");
        if (distance >= 150)
        {
            // 距离大于150米，先传送到最近的传送点
            // J 打开任务 切换追踪打开地图 中心点就是任务点
            Simulation.SendInput.SimulateAction(GIActions.OpenQuestMenu);
            Sleep(800, _ct);
            // TODO 识别是否在任务界面
            // 切换追踪
            var btn = ra.Derive(CaptureRect.Width - 250, CaptureRect.Height - 60);
            btn.Click();
            Sleep(200, _ct);
            btn.Click();
            Sleep(1500, _ct);

            // 寻找所有传送点
            ra = CaptureToRectArea();
            var tpPointList = MatchTemplateHelper.MatchMultiPicForOnePic(ra.CacheGreyMat, QuickTeleportAssets.Instance.MapChooseIconGreyMatList);
            if (tpPointList.Count > 0)
            {
                // 选中离中心点最近的传送点
                var centerX = ra.Width / 2;
                var centerY = ra.Height / 2;
                var minDistance = double.MaxValue;
                Rect nearestRect = default;
                foreach (var tpPoint in tpPointList)
                {
                    var distanceTp = Math.Sqrt(Math.Pow(Math.Abs(tpPoint.X - centerX), 2) + Math.Pow(Math.Abs(tpPoint.Y - centerY), 2));
                    if (distanceTp < minDistance)
                    {
                        minDistance = distanceTp;
                        nearestRect = tpPoint;
                    }
                }

                ra.Derive(nearestRect).Click();
                // 等待自动传送完成
                Sleep(2000, _ct);

                if (Bv.IsInBigMapUi(CaptureToRectArea()))
                {
                    Logger.LogWarning(Lang.S["GameTask_11268_161ee3"]);
                }
                else
                {
                    Sleep(500, _ct);
                    NewRetry.Do(() =>
                    {
                        if (!Bv.IsInMainUi(CaptureToRectArea()))
                        {
                            Logger.LogInformation(Lang.S["GameTask_11267_8569d9"]);
                            throw new RetryException(Lang.S["GameTask_11266_491d78"]);
                        }
                    }, TimeSpan.FromSeconds(1), 100);
                    StartTrackPoint();
                }
            }
            else
            {
                Logger.LogWarning(Lang.S["GameTask_11265_83a590"]);
            }
        }
        else
        {
            StartTrackPoint();
        }
    }

    private void StartTrackPoint()
    {
        // V键直接追踪
        Simulation.SendInput.SimulateAction(GIActions.QuestNavigation);
        Sleep(3000, _ct);

        var ra = CaptureToRectArea();
        var blueTrackPointRa = ra.Find(ElementAssets.Instance.BlueTrackPoint);
        if (blueTrackPointRa.IsExist())
        {
            MakeBlueTrackPointDirectlyAbove();
        }
        else
        {
            Logger.LogWarning(Lang.S["GameTask_11264_2091bd"]);
        }
    }

    /// <summary>
    /// 找到追踪点并调整方向
    /// </summary>
    private void MakeBlueTrackPointDirectlyAbove()
    {
        // return new Task(() =>
        // {
        int prevMoveX = 0;
        bool wDown = false;
        while (!_ct.IsCancellationRequested)
        {
            var ra = CaptureToRectArea();
            var blueTrackPointRa = ra.Find(ElementAssets.Instance.BlueTrackPoint);
            if (blueTrackPointRa.IsExist())
            {
                // 使追踪点位于俯视角上方
                var centerY = blueTrackPointRa.Y + blueTrackPointRa.Height / 2;
                if (centerY > CaptureRect.Height / 2)
                {
                    Simulation.SendInput.Mouse.MoveMouseBy(-50, 0);
                    if (wDown)
                    {
                        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                        wDown = false;
                    }
                    Debug.WriteLine(Lang.S["GameTask_11263_99443d"]);
                    continue;
                }

                // 调整方向
                var centerX = blueTrackPointRa.X + blueTrackPointRa.Width / 2;
                var moveX = (centerX - CaptureRect.Width / 2) / 8;
                moveX = moveX switch
                {
                    > 0 and < 10 => 10,
                    > -10 and < 0 => -10,
                    _ => moveX
                };
                if (moveX != 0)
                {
                    Simulation.SendInput.Mouse.MoveMouseBy(moveX, 0);
                    Debug.WriteLine(Lang.S["GameTask_11262_47c067"] + moveX);
                }

                if (moveX == 0 || prevMoveX * moveX < 0)
                {
                    if (!wDown)
                    {
                        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                        wDown = true;
                    }
                }

                if (Math.Abs(moveX) < 50 && Math.Abs(centerY - CaptureRect.Height / 2) < 200)
                {
                    if (wDown)
                    {
                        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                        wDown = false;
                    }
                    // 识别距离
                    var text = OcrFactory.Paddle.OcrWithoutDetector(ra.CacheGreyMat[_missionDistanceRect]);
                    if (StringUtils.TryExtractPositiveInt(text) is > -1 and <= 3)
                    {
                        Logger.LogInformation(Lang.S["GameTask_11261_ce14ab"], text);
                        break;
                    }
                    Logger.LogInformation(Lang.S["GameTask_11260_4531ea"]);
                    break;
                }

                prevMoveX = moveX;
            }
            else
            {
                // 随机移动
                Logger.LogInformation(Lang.S["GameTask_11259_f7547c"]);
            }

            Simulation.SendInput.Mouse.MoveMouseBy(0, 500); // 保证俯视角
            Sleep(100);
        }
        // });
    }

    private int GetDistanceFromMissionText(List<Region> textRaList)
    {
        // 打印所有任务文字
        var text = textRaList.Aggregate(string.Empty, (current, textRa) => current + textRa.Text.Trim() + "|");
        Logger.LogInformation(Lang.S["GameTask_11258_42a0d0"], text);

        foreach (var textRa in textRaList)
        {
            if (textRa.Text.Length < 8 && textRa.Text.Contains('m', StringComparison.OrdinalIgnoreCase))
            {
                _missionDistanceRect = textRa.ConvertSelfPositionToGameCaptureRegion();
                return StringUtils.TryExtractPositiveInt(textRa.Text);
            }
        }

        return -1;
    }

    private List<Region> OcrMissionTextRaList(Region paimonMenuRa)
    {
        return CaptureToRectArea().FindMulti(new RecognitionObject
        {
            RecognitionType = RecognitionTypes.Ocr,
            RegionOfInterest = new Rect(paimonMenuRa.X, paimonMenuRa.Y - 15 + 210,
                (int)(300 * AssetScale), (int)(100 * AssetScale))
        });
    }
}

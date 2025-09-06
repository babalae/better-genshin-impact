using System;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Common.Element.Assets;

namespace BetterGenshinImpact.GameTask.AutoArtifactSalvage
{
    public sealed class InventoryDialogGuard
    {
        private static readonly object _gate = new();
        private static InventoryDialogGuard? _current;

        private readonly ILogger _logger;
        private readonly CancellationTokenSource _cts;
        private readonly CancellationToken _outerCt;

        private DateTime _lastAction = DateTime.MinValue;
        private int _detectedFrames = 0;
        private int _handledTimes = 0;

        // 触发关键词（物品过期窗口）
        private static readonly string[] TriggerKeys = { "过期","已过期","已失效","无法使用","expired","has expired","event ended" };

        private InventoryDialogGuard(ILogger logger, CancellationToken outerCt)
        {
            _logger = logger;
            _outerCt = outerCt;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
        }

        public static void Start(ILogger logger, CancellationToken ct)
        {
            lock (_gate)
            {
                if (_current != null) return;
                _current = new InventoryDialogGuard(logger, ct);
                _ = _current.RunAsync();
                logger.LogInformation("背包过期弹窗守护:开启");
            }
        }

        public static void Stop()
        {
            lock (_gate)
            {
                _current?._cts.Cancel();
                _current = null;
                _current?._logger.LogInformation("背包过期弹窗守护:关闭");
            }
        }

        private async Task RunAsync()
        {
            try
            {
                const int CHECK_MS = 250;
                const int COOLDOWN_MS = 900;	// 点击冷却
                const int NEED_FRAMES = 2;
                const int MAX_HANDLE_PER_SESSION = 3;	// 单次背包守护期间最多处理 3 次弹窗，避免异常场景下无限点击

                while (!_cts.IsCancellationRequested)
                {
                    try
                    {
                        using var ra = TaskControl.CaptureToRectArea();

                        // 离开背包 → 自停
                        if (!IsInInventory())
                        {
                            _logger.LogInformation("背包过期弹窗守护:检测到当前不在背包界面，背包过期弹窗守护自动关闭");
                            break;
                        }   

                        // 冷却
                        if ((DateTime.Now - _lastAction).TotalMilliseconds < COOLDOWN_MS)
                        {
                            await TaskControl.Delay(CHECK_MS, _cts.Token);
                            continue;
                        }

                        // OCR 中央区域
                        var ocrList = ra.FindMulti(RecognitionObject.Ocr(
                            ra.Width * 0.30, ra.Height * 0.20, ra.Width * 0.40, ra.Height * 0.60));

                        bool triggered = ContainsAny(ocrList, TriggerKeys);
                        if (!triggered)
                        {
                            _detectedFrames = 0;	// 清空“连续命中帧数”
                            await TaskControl.Delay(CHECK_MS, _cts.Token);
                            continue;
                        }

                        // 多帧一致
                        _detectedFrames++;
                        if (_detectedFrames < NEED_FRAMES)
                        {
                            await TaskControl.Delay(CHECK_MS, _cts.Token);
                            continue;
                        }
				        // 检测到过期弹窗
                        _logger.LogInformation("背包过期弹窗守护:检测到过期信息弹窗");
                        
                        // 点击确认按钮，使用 btnWhiteConfirm 识别
                        using var centerRegion = ra.DeriveCrop(
                            ra.Width * 0.30,
                            ra.Height * 0.20,
                            ra.Width * 0.40,
                            ra.Height * 0.60);
                        using var btnWhiteConfirmRa = centerRegion.Find(ElementAssets.Instance.BtnWhiteConfirm);

                        if (btnWhiteConfirmRa.IsExist())
                        {
                            btnWhiteConfirmRa.Click();
                            _logger.LogInformation("背包过期弹窗守护:点击了中央区域中的白色确认按钮");
                        }
                        else
                        {
                            _logger.LogInformation("背包过期弹窗守护:中央区域中未识别到白色确认按钮");
                        }

                        
                        _lastAction = DateTime.Now;
                        _handledTimes++;    // 不管点击是否成功，均将计数自增，避免处理失败时的循环处理
                        _detectedFrames = 0;

                        // 复核弹窗是否被清除掉（这段代码可以删掉，不影响功能）
                        await TaskControl.Delay(300, _cts.Token);
                        using (var ra2 = TaskControl.CaptureToRectArea())
                        {
                            if (!ContainsAny(ra2.FindMulti(RecognitionObject.Ocr(
                                    ra2.Width * 0.30, ra2.Height * 0.20, ra2.Width * 0.40, ra2.Height * 0.60)), TriggerKeys))
                            {
                                // 清掉了
                                _logger.LogInformation("背包过期弹窗守护:成功关闭一个过期信息弹窗");
                            }
                        }

                        if (_handledTimes >= MAX_HANDLE_PER_SESSION)
                        {
                            _logger.LogWarning("背包过期弹窗守护:处理次数超过限制，自动关闭");
                            break;
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "InventoryDialogGuard loop error");
                        await TaskControl.Delay(600, _cts.Token);
                    }

                    await TaskControl.Delay(CHECK_MS, _cts.Token);
                }
            }
            finally
            {
                lock (_gate) { _current = null; }
            }
        }

        private static bool ContainsAny(System.Collections.Generic.IEnumerable<Region> list, string[] keys)
        {
            foreach (var r in list)
                foreach (var k in keys)
                    if (r.Text.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
        
        // 判断当前是否在背包界面
        private static bool IsInInventory()
        {
            try
            {
                var icon = ElementAssets.Instance.InventoryIcon;
                var sceneMat = TaskControl.CaptureGameImage(TaskTriggerDispatcher.GlobalGameCapture);
                
                // 获取模板图
                var template = icon.Use3Channels ? icon.TemplateImageMat : icon.TemplateImageGreyMat;

                // 灰度化截图
                Mat sceneGray = sceneMat.Channels() == 1 ? sceneMat : new Mat();
                if (sceneMat.Channels() == 3)
                    Cv2.CvtColor(sceneMat, sceneGray, ColorConversionCodes.BGR2GRAY);
                else if (sceneMat.Channels() == 4)
                    Cv2.CvtColor(sceneMat, sceneGray, ColorConversionCodes.BGRA2GRAY);

                // 灰度化模板图
                Mat templateGray = template.Channels() == 1 ? template : new Mat();
                if (template.Channels() == 3)
                    Cv2.CvtColor(template, templateGray, ColorConversionCodes.BGR2GRAY);
                else if (template.Channels() == 4)
                    Cv2.CvtColor(template, templateGray, ColorConversionCodes.BGRA2GRAY);
                
                // 截取截图上方10%的区域（用于限制匹配区域）
                int cropHeight = (int)(sceneGray.Height * 0.1);
                sceneGray = new Mat(sceneGray, new OpenCvSharp.Rect(0, 0, sceneGray.Width, cropHeight));
                
                // 获取 ROI 区域
                OpenCvSharp.Rect roi = icon.RegionOfInterest;
                bool hasRoi = roi.Width > 0 && roi.Height > 0 &&
                              roi.X >= 0 && roi.Y >= 0 &&
                              roi.X + roi.Width <= sceneGray.Width &&
                              roi.Y + roi.Height <= sceneGray.Height;

                Mat roiScene = hasRoi ? new Mat(sceneGray, roi) : sceneGray;

                // 模板匹配
                Mat result = new Mat();
                Cv2.MatchTemplate(roiScene, templateGray, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out double minVal, out double maxVal, out OpenCvSharp.Point minLoc, out OpenCvSharp.Point maxLoc);


                // 判断是否匹配成功
                bool matched = maxVal >= icon.Threshold;
                return matched;
            }
            catch { return false; }
        }
    }
}
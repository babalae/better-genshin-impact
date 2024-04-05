using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoSkip.Assets;
using BetterGenshinImpact.GameTask.AutoSkip.Model;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.View.Drawable;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Sdcb.PaddleOCR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.AutoSkip;

/// <summary>
/// 自动剧情有选项点击
/// </summary>
public class AutoSkipTrigger : ITaskTrigger
{
    private readonly ILogger<AutoSkipTrigger> _logger = App.GetLogger<AutoSkipTrigger>();

    public string Name => "自动剧情";
    public bool IsEnabled { get; set; }
    public int Priority => 20;
    public bool IsExclusive => false;

    private readonly AutoSkipAssets _autoSkipAssets;

    private readonly AutoSkipConfig _config;

    /// <summary>
    /// 不自动点击的选项，优先级低于橙色文字点击
    /// </summary>
    private List<string> _defaultPauseList = new();

    /// <summary>
    /// 不自动点击的选项
    /// </summary>
    private List<string> _pauseList = new();

    /// <summary>
    /// 优先自动点击的选项
    /// </summary>
    private List<string> _selectList = new();

    public AutoSkipTrigger()
    {
        _autoSkipAssets = AutoSkipAssets.Instance;
        _config = TaskContext.Instance().Config.AutoSkipConfig;
    }

    public void Init()
    {
        IsEnabled = TaskContext.Instance().Config.AutoSkipConfig.Enabled;

        try
        {
            var defaultPauseListJson = Global.ReadAllTextIfExist(@"User\AutoSkip\default_pause_options.json");
            if (!string.IsNullOrEmpty(defaultPauseListJson))
            {
                _defaultPauseList = JsonSerializer.Deserialize<List<string>>(defaultPauseListJson, ConfigService.JsonOptions) ?? new List<string>();
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "读取自动剧情默认暂停点击关键词列表失败");
            MessageBox.Show("读取自动剧情默认暂停点击关键词列表失败，请确认修改后的自动剧情默认暂停点击关键词内容格式是否正确！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        try
        {
            var pauseListJson = Global.ReadAllTextIfExist(@"User\AutoSkip\pause_options.json");
            if (!string.IsNullOrEmpty(pauseListJson))
            {
                _pauseList = JsonSerializer.Deserialize<List<string>>(pauseListJson, ConfigService.JsonOptions) ?? new List<string>();
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "读取自动剧情暂停点击关键词列表失败");
            MessageBox.Show("读取自动剧情暂停点击关键词列表失败，请确认修改后的自动剧情暂停点击关键词内容格式是否正确！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        try
        {
            var selectListJson = Global.ReadAllTextIfExist(@"User\AutoSkip\select_options.json");
            if (!string.IsNullOrEmpty(selectListJson))
            {
                _selectList = JsonSerializer.Deserialize<List<string>>(selectListJson, ConfigService.JsonOptions) ?? new List<string>();
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "读取自动剧情优先点击选项列表失败");
            MessageBox.Show("读取自动剧情优先点击选项列表失败，请确认修改后的自动剧情优先点击选项内容格式是否正确！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// 上一次播放中的帧
    /// </summary>
    private DateTime _prevPlayingTime = DateTime.MinValue;

    private DateTime _prevExecute = DateTime.MinValue;
    private DateTime _prevHangoutExecute = DateTime.MinValue;

    private DateTime _prevGetDailyRewardsTime = DateTime.MinValue;

    private DateTime _prevClickTime = DateTime.MinValue;

    public void OnCapture(CaptureContent content)
    {
        if ((DateTime.Now - _prevExecute).TotalMilliseconds <= 200)
        {
            return;
        }

        _prevExecute = DateTime.Now;

        VisionContext.Instance().DrawContent.RemoveRect("HangoutIcon");

        GetDailyRewardsEsc(_config, content);

        // 找左上角剧情自动的按钮
        using var foundRectArea = content.CaptureRectArea.Find(_autoSkipAssets.StopAutoButtonRo);

        var isPlaying = !foundRectArea.IsEmpty(); // 播放中

        // 播放中图标消失3s内OCR判断文字
        if (!isPlaying && (DateTime.Now - _prevPlayingTime).TotalSeconds <= 5)
        {
            // 找播放中的文字
            content.CaptureRectArea.Find(_autoSkipAssets.PlayingTextRo, _ => { isPlaying = true; });
            if (!isPlaying)
            {
                var textRa = content.CaptureRectArea.Crop(_autoSkipAssets.PlayingTextRo.RegionOfInterest);
                // 过滤出白色
                var hsvFilterMat = OpenCvCommonHelper.InRangeHsv(textRa.SrcMat, new Scalar(0, 0, 170), new Scalar(255, 80, 245));
                var result = OcrFactory.Paddle.Ocr(hsvFilterMat);
                if (result.Contains("播") || result.Contains("番") || result.Contains("放") || result.Contains("中") || result.Contains("潘") || result.Contains("故"))
                {
                    VisionContext.Instance().DrawContent.PutRect("PlayingText", textRa.ConvertRelativePositionToCaptureArea().ToRectDrawable());
                    isPlaying = true;
                }
            }

            if (!isPlaying)
            {
                // 关闭弹出页
                ClosePopupPage(content);

                // 自动剧情点击3s内判断
                if ((DateTime.Now - _prevPlayingTime).TotalMilliseconds < 3000)
                {
                    // 提交物品
                    if (SubmitGoods(content))
                    {
                        return;
                    }
                }
            }
        }
        else
        {
            VisionContext.Instance().DrawContent.RemoveRect("PlayingText");
        }

        if (isPlaying)
        {
            _prevPlayingTime = DateTime.Now;
            if (TaskContext.Instance().Config.AutoSkipConfig.QuicklySkipConversationsEnabled)
            {
                Simulation.SendInputEx.Keyboard.KeyPress(User32.VK.VK_SPACE);
            }

            // 对话选项选择
            var hasOption = ChatOptionChoose(content);

            // 邀约选项选择 1s 1次
            if (_config.AutoHangoutEventEnabled && !hasOption)
            {
                if ((DateTime.Now - _prevHangoutExecute).TotalMilliseconds < 1000)
                {
                    return;
                }

                _prevHangoutExecute = DateTime.Now;
                HangoutOptionChoose(content);
            }
        }
        else
        {
            ClickBlackGameScreen(content);
        }
    }

    /// <summary>
    /// 黑屏点击判断
    /// </summary>
    /// <param name="content"></param>
    /// <returns></returns>
    private bool ClickBlackGameScreen(CaptureContent content)
    {
        // 黑屏剧情要点击鼠标（多次） 几乎全黑的时候不用点击
        using var grayMat = new Mat(content.CaptureRectArea.SrcGreyMat, new Rect(0, content.CaptureRectArea.SrcGreyMat.Height / 3, content.CaptureRectArea.SrcGreyMat.Width, content.CaptureRectArea.SrcGreyMat.Height / 3));
        var blackCount = OpenCvCommonHelper.CountGrayMatColor(grayMat, 0);
        var rate = blackCount * 1d / (grayMat.Width * grayMat.Height);
        if (rate is >= 0.5 and < 0.98999)
        {
            Simulation.SendInputEx.Mouse.LeftButtonClick();
            if ((DateTime.Now - _prevClickTime).TotalMilliseconds > 1000)
            {
                _logger.LogInformation("自动剧情：{Text} 比例 {Rate}", "点击黑屏", rate.ToString("F"));
            }

            _prevClickTime = DateTime.Now;
            return true;
        }
        return false;
    }

    private void HangoutOptionChoose(CaptureContent content)
    {
        var selectedRects = MatchTemplateHelper.MatchOnePicForOnePic(content.CaptureRectArea.SrcGreyMat, _autoSkipAssets.HangoutSelectedMat);
        var unselectedRects = MatchTemplateHelper.MatchOnePicForOnePic(content.CaptureRectArea.SrcGreyMat, _autoSkipAssets.HangoutUnselectedMat);
        if (selectedRects.Count > 0 || unselectedRects.Count > 0)
        {
            var captureArea = TaskContext.Instance().SystemInfo.CaptureAreaRect;
            var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
            var clickOffset = new ClickOffset(captureArea.X, captureArea.Y, assetScale);

            // 识别结果显示在遮罩上
            var drawList = selectedRects.Concat(unselectedRects).Select(rect => rect.ToRectDrawable()).ToList();
            VisionContext.Instance().DrawContent.PutOrRemoveRectList("HangoutIcon", drawList);

            List<HangoutOption> hangoutOptionList =
            [
                .. selectedRects.Select(selectedRect => new HangoutOption(selectedRect, true)),
                .. unselectedRects.Select(unselectedRect => new HangoutOption(unselectedRect, false)),
            ];
            // 只有一个选项直接点击
            // if (hangoutOptionList.Count == 1)
            // {
            //     hangoutOptionList[0].Click(clickOffset);
            //     AutoHangoutSkipLog("点击唯一邀约选项");
            //     return;
            // }

            hangoutOptionList = hangoutOptionList.Where(hangoutOption => hangoutOption.TextRect != Rect.Empty).ToList();
            if (hangoutOptionList.Count == 0)
            {
                return;
            }

            // OCR识别选项文字
            foreach (var hangoutOption in hangoutOptionList)
            {
                using var textMat = new Mat(content.CaptureRectArea.SrcGreyMat, hangoutOption.TextRect);
                var text = OcrFactory.Paddle.Ocr(textMat);
                hangoutOption.OptionTextSrc = StringUtils.RemoveAllEnter(text);
            }

            // todo 根据文字内容决定停留还是自动点击
            // 这个OCR好像不太准确

            // 没有停留的选项 优先选择未点击的的选项
            foreach (var hangoutOption in hangoutOptionList)
            {
                if (!hangoutOption.IsSelected)
                {
                    hangoutOption.Click(clickOffset);
                    AutoHangoutSkipLog(hangoutOption.OptionTextSrc);
                    return;
                }
            }

            // 没有未点击的选项 选择第一个已点击选项
            hangoutOptionList[0].Click(clickOffset);
            AutoHangoutSkipLog(hangoutOptionList[0].OptionTextSrc);
            VisionContext.Instance().DrawContent.RemoveRect("HangoutIcon");
        }
    }

    /// <summary>
    /// 获取橙色选项的文字
    /// </summary>
    /// <param name="captureMat"></param>
    /// <param name="foundIconRectArea"></param>
    /// <param name="chatOptionTextWidth"></param>
    /// <returns></returns>
    [Obsolete]
    private string GetOrangeOptionText(Mat captureMat, RectArea foundIconRectArea, int chatOptionTextWidth)
    {
        var textRect = new Rect(foundIconRectArea.X + foundIconRectArea.Width, foundIconRectArea.Y, chatOptionTextWidth, foundIconRectArea.Height);
        using var mat = new Mat(captureMat, textRect);
        // 只提取橙色
        using var bMat = OpenCvCommonHelper.Threshold(mat, new Scalar(247, 198, 50), new Scalar(255, 204, 54));
        // Cv2.ImWrite("log/每日委托.png", bMat);
        var whiteCount = OpenCvCommonHelper.CountGrayMatColor(bMat, 255);
        var rate = whiteCount * 1.0 / (bMat.Width * bMat.Height);
        if (rate < 0.06)
        {
            Debug.WriteLine($"识别到橙色文字区域占比:{rate}");
            return string.Empty;
        }

        var text = OcrFactory.Paddle.Ocr(bMat);
        return text;
    }

    private bool IsOrangeOption(Mat textMat)
    {
        // 只提取橙色
        // Cv2.ImWrite($"log/text{DateTime.Now:yyyyMMddHHmmssffff}.png", textMat);
        using var bMat = OpenCvCommonHelper.Threshold(textMat, new Scalar(247, 198, 50), new Scalar(255, 204, 54));
        var whiteCount = OpenCvCommonHelper.CountGrayMatColor(bMat, 255);
        var rate = whiteCount * 1.0 / (bMat.Width * bMat.Height);
        Debug.WriteLine($"识别到橙色文字区域占比:{rate}");
        if (rate > 0.06)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 领取每日委托奖励 后 10s 寻找原石是否出现，出现则按下esc
    /// </summary>
    private void GetDailyRewardsEsc(AutoSkipConfig config, CaptureContent content)
    {
        if (!config.AutoGetDailyRewardsEnabled)
        {
            return;
        }

        if ((DateTime.Now - _prevGetDailyRewardsTime).TotalSeconds > 10)
        {
            return;
        }

        content.CaptureRectArea.Find(_autoSkipAssets.PrimogemRo, primogemRa =>
        {
            Thread.Sleep(100);
            Simulation.SendInputEx.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
            _prevGetDailyRewardsTime = DateTime.MinValue;
            primogemRa.Dispose();
        });
    }

    private readonly Regex _enRegex = new(@"^[a-zA-Z]+$");

    /// <summary>
    /// 新的对话选项选择
    ///
    /// 返回 true 表示存在对话选项，但是不一定点击了
    /// </summary>
    private bool ChatOptionChoose(CaptureContent content)
    {
        var captureArea = TaskContext.Instance().SystemInfo.CaptureAreaRect;
        var assetScale = TaskContext.Instance().SystemInfo.AssetScale;

        // 感叹号识别 遇到直接点击
        var exclamationIconRa = content.CaptureRectArea.Find(_autoSkipAssets.ExclamationIconRo);
        if (!exclamationIconRa.IsEmpty())
        {
            TaskControl.Sleep(_config.AfterChooseOptionSleepDelay);
            exclamationIconRa.ClickCenter();
            AutoSkipLog("点击感叹号选项");
            exclamationIconRa.Dispose();
            return true;
        }

        // 气泡识别
        var chatOptionResultList = MatchTemplateHelper.MatchOnePicForOnePic(content.CaptureRectArea.SrcGreyMat[_autoSkipAssets.OptionRoi], _autoSkipAssets.OptionIconRo.TemplateImageGreyMat);
        if (chatOptionResultList.Count > 0)
        {
            // 第一个元素就是最下面的
            chatOptionResultList = chatOptionResultList.OrderByDescending(x => x.Y).ToList();

            // 通过最下面的气泡框来文字识别
            var lowest = chatOptionResultList[0];
            var ocrRect = new Rect(_autoSkipAssets.OptionRoi.X + (int)(lowest.X + lowest.Width + 8 * assetScale), 0,
                (int)(535 * assetScale), (int)(lowest.Y + lowest.Height + 30 * assetScale));
            using var ocrMat = new Mat(content.CaptureRectArea.SrcGreyMat, ocrRect);
            // Cv2.ImWrite("log/ocrMat.png", ocrMat);
            var ocrRes = OcrFactory.Paddle.OcrResult(ocrMat);

            // 删除为空的结果 和 纯英文的结果
            var rs = new List<PaddleOcrResultRegion>();
            foreach (var item in ocrRes.Regions)
            {
                if (string.IsNullOrEmpty(item.Text) || (item.Text.Length < 5 && _enRegex.IsMatch(item.Text)))
                {
                    continue;
                }

                rs.Add(item);
            }

            if (rs.Count > 0)
            {
                var clickOffset = new ClickOffset(captureArea.X + ocrRect.X, captureArea.Y + ocrRect.Y, assetScale);

                // 用户自定义关键词 匹配
                foreach (var item in rs)
                {
                    // 选择关键词
                    if (_selectList.Any(s => item.Text.Contains(s)))
                    {
                        ClickOcrRegion(clickOffset, item);
                        return true;
                    }

                    // 不选择关键词
                    if (_pauseList.Any(s => item.Text.Contains(s)))
                    {
                        return true;
                    }
                }

                // 橙色选项
                foreach (var item in rs)
                {
                    var textOcrRect = item.Rect.BoundingRect();
                    var textRect = new Rect(ocrRect.X + textOcrRect.X, ocrRect.Y + textOcrRect.Y, textOcrRect.Width, textOcrRect.Height);
                    if (textRect.X < 0 || textRect.Y < 0 || textRect.Width > content.CaptureRectArea.SrcMat.Width || textRect.Height > content.CaptureRectArea.SrcMat.Height)
                    {
                        Debug.WriteLine($"识别到的文字区域超出正常范围:{textOcrRect}");
                        _logger.LogDebug("识别到的文字区域超出正常范围:{TextOcrRect}", textOcrRect);
                        continue;
                    }

                    var textMat = new Mat(content.CaptureRectArea.SrcMat, textRect);
                    if (IsOrangeOption(textMat))
                    {
                        if (item.Text.Contains("每日委托"))
                        {
                            ClickOcrRegion(clickOffset, item);
                            _prevGetDailyRewardsTime = DateTime.Now; // 记录领取时间
                        }
                        else if (item.Text.Contains("探索派遣"))
                        {
                            ClickOcrRegion(clickOffset, item);
                            Thread.Sleep(800); // 等待探索派遣界面打开
                            new OneKeyExpeditionTask().Run(_autoSkipAssets);
                        }
                        else
                        {
                            ClickOcrRegion(clickOffset, item);
                        }

                        return true;
                    }
                }

                // 默认不选择关键词
                foreach (var item in rs)
                {
                    // 不选择关键词
                    if (_defaultPauseList.Any(s => item.Text.Contains(s)))
                    {
                        return true;
                    }
                }

                // 最后，选择默认选项
                var clickRegion = rs[^1];
                if (_config.ClickFirstOptionEnabled)
                {
                    clickRegion = rs[0];
                }

                ClickOcrRegion(clickOffset, clickRegion);
                AutoSkipLog(clickRegion.Text);
            }
            else
            {
                var clickRect = lowest;
                if (_config.ClickFirstOptionEnabled)
                {
                    clickRect = chatOptionResultList[^1];
                }

                // 没OCR到文字，直接选择气泡选项
                TaskControl.Sleep(_config.AfterChooseOptionSleepDelay);
                var clickOffset = new ClickOffset(captureArea.X + _autoSkipAssets.OptionRoi.X, captureArea.Y + _autoSkipAssets.OptionRoi.Y, assetScale);
                clickOffset.ClickWithoutScale(clickRect.X + clickRect.Width / 2, clickRect.Y + clickRect.Height / 2);
                var msg = _config.ClickFirstOptionEnabled ? "第一个" : "最后一个";
                AutoSkipLog($"点击{msg}气泡选项");
            }

            return true;
        }

        return false;
    }

    private void ClickOcrRegion(ClickOffset clickOffset, PaddleOcrResultRegion clickRegion)
    {
        TaskControl.Sleep(_config.AfterChooseOptionSleepDelay);
        clickOffset.ClickWithoutScale(clickRegion.Rect.Center.X, clickRegion.Rect.Center.Y);
        AutoSkipLog(clickRegion.Text);
    }

    private void AutoHangoutSkipLog(string text)
    {
        if ((DateTime.Now - _prevClickTime).TotalMilliseconds > 1000)
        {
            _logger.LogInformation("自动邀约：{Text}", text);
        }

        _prevClickTime = DateTime.Now;
    }

    private void AutoSkipLog(string text)
    {
        if (text.Contains("每日委托") || text.Contains("探索派遣"))
        {
            _logger.LogInformation("自动剧情：{Text}", text);
        }
        else if ((DateTime.Now - _prevClickTime).TotalMilliseconds > 1000)
        {
            _logger.LogInformation("自动剧情：{Text}", text);
        }

        _prevClickTime = DateTime.Now;
    }

    /// <summary>
    /// 关闭弹出页
    /// </summary>
    /// <param name="content"></param>
    private void ClosePopupPage(CaptureContent content)
    {
        content.CaptureRectArea.Find(_autoSkipAssets.PageCloseRo, pageCloseRoRa =>
        {
            pageCloseRoRa.ClickCenter();

            AutoSkipLog("关闭弹出页");
            pageCloseRoRa.Dispose();
        });
    }

    private bool SubmitGoods(CaptureContent content)
    {
        var exclamationRa = content.CaptureRectArea.Find(_autoSkipAssets.SubmitExclamationIconRo);
        if (!exclamationRa.IsEmpty())
        {
            // var rects = MatchTemplateHelper.MatchOnePicForOnePic(content.CaptureRectArea.SrcMat.CvtColor(ColorConversionCodes.BGRA2BGR),
            //     _autoSkipAssets.SubmitGoodsMat, TemplateMatchModes.SqDiffNormed, null, 0.9, 4);
            var rects = ContoursHelper.FindSpecifyColorRects(content.CaptureRectArea.SrcMat, new Scalar(233, 229, 220), 100, 20);
            if (rects.Count == 0)
            {
                return false;
            }

            // 画矩形并保存
            // foreach (var rect in rects)
            // {
            //     Cv2.Rectangle(content.CaptureRectArea.SrcMat, rect, Scalar.Red, 1);
            // }
            // Cv2.ImWrite("log/提交物品.png", content.CaptureRectArea.SrcMat);

            var captureArea = TaskContext.Instance().SystemInfo.CaptureAreaRect;
            var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
            var clickOffset = new ClickOffset(captureArea.X, captureArea.Y, assetScale);
            for (var i = 0; i < rects.Count; i++)
            {
                clickOffset.ClickWithoutScale(rects[i].X + rects[i].Width / 2, rects[i].Y + rects[i].Height / 2);
                _logger.LogInformation("提交物品：{Text}", "1. 选择物品" + i);
                TaskControl.Sleep(800);
                content = TaskControl.CaptureToContent();

                var btnBlackConfirmRa = content.CaptureRectArea.Find(ElementAssets.Instance.BtnBlackConfirm);
                if (!btnBlackConfirmRa.IsEmpty())
                {
                    btnBlackConfirmRa.ClickCenter();
                    _logger.LogInformation("提交物品：{Text}", "2. 放入" + i);
                    TaskControl.Sleep(200);
                }
            }

            TaskControl.Sleep(500);
            content = TaskControl.CaptureToContent();

            var btnWhiteConfirmRa = content.CaptureRectArea.Find(ElementAssets.Instance.BtnWhiteConfirm);
            if (!btnWhiteConfirmRa.IsEmpty())
            {
                btnWhiteConfirmRa.ClickCenter();
                _logger.LogInformation("提交物品：{Text}", "3. 交付");

                VisionContext.Instance().DrawContent.ClearAll();
            }

            // 最多4个物品 现在就支持一个
            // var prevGoodsRect = Rect.Empty;
            // for (var i = 1; i <= 4; i++)
            // {
            //     // 不断的截取出右边的物品
            //     TaskControl.Sleep(200);
            //     content = TaskControl.CaptureToContent();
            //     var gameArea = content.CaptureRectArea;
            //     if (prevGoodsRect != Rect.Empty)
            //     {
            //         var r = content.CaptureRectArea.ToRect();
            //         var newX = prevGoodsRect.X + prevGoodsRect.Width;
            //         gameArea = content.CaptureRectArea.Crop(new Rect(newX, 0, r.Width - newX, r.Height));
            //         Cv2.ImWrite($"log/物品{i}.png", gameArea.SrcMat);
            //     }
            //
            //     var goods = gameArea.Find(_autoSkipAssets.SubmitGoodsRo);
            //     if (!goods.IsEmpty())
            //     {
            //         prevGoodsRect = goods.ConvertRelativePositionToCaptureArea();
            //         goods.ClickCenter();
            //         _logger.LogInformation("提交物品：{Text}", "1. 选择物品" + i);
            //
            //         TaskControl.Sleep(800);
            //         content = TaskControl.CaptureToContent();
            //
            //         var btnBlackConfirmRa = content.CaptureRectArea.Find(ElementAssets.Instance().BtnBlackConfirm);
            //         if (!btnBlackConfirmRa.IsEmpty())
            //         {
            //             btnBlackConfirmRa.ClickCenter();
            //             _logger.LogInformation("提交物品：{Text}", "2. 放入" + i);
            //
            //             TaskControl.Sleep(800);
            //             content = TaskControl.CaptureToContent();
            //
            //             btnBlackConfirmRa = content.CaptureRectArea.Find(ElementAssets.Instance().BtnBlackConfirm);
            //             if (!btnBlackConfirmRa.IsEmpty())
            //             {
            //                 _logger.LogInformation("提交物品：{Text}", "2. 仍旧存在物品");
            //                 continue;
            //             }
            //             else
            //             {
            //                 var btnWhiteConfirmRa = content.CaptureRectArea.Find(ElementAssets.Instance().BtnWhiteConfirm);
            //                 if (!btnWhiteConfirmRa.IsEmpty())
            //                 {
            //                     btnWhiteConfirmRa.ClickCenter();
            //                     _logger.LogInformation("提交物品：{Text}", "3. 交付");
            //
            //                     VisionContext.Instance().DrawContent.ClearAll();
            //                     return true;
            //                 }
            //                 break;
            //             }
            //         }
            //     }
            //     else
            //     {
            //         break;
            //     }
            // }
        }

        return false;
    }
}

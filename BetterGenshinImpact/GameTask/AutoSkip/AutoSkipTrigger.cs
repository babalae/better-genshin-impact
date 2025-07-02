using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoSkip.Assets;
using BetterGenshinImpact.GameTask.AutoSkip.Model;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.View.Drawable;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using Vanara.PInvoke;
using Region = BetterGenshinImpact.GameTask.Model.Area.Region;

namespace BetterGenshinImpact.GameTask.AutoSkip;

/// <summary>
/// 自动剧情有选项点击
/// </summary>
public partial class AutoSkipTrigger : ITaskTrigger
{
    private readonly ILogger<AutoSkipTrigger> _logger = App.GetLogger<AutoSkipTrigger>();

    public string Name => "自动剧情";
    public bool IsEnabled { get; set; }
    public int Priority => 20;
    public bool IsExclusive => false;

    public bool IsBackgroundRunning { get; private set; }
    
    public bool UseBackgroundOperation { get; private set; }

    public bool IsUseInteractionKey { get; set; } = false;

    private readonly AutoSkipAssets _autoSkipAssets;

    private readonly AutoSkipConfig _config;

    /// <summary>
    /// 不自动点击的选项，优先级低于橙色文字点击
    /// </summary>
    private List<string> _defaultPauseList = [];

    /// <summary>
    /// 不自动点击的选项
    /// </summary>
    private List<string> _pauseList = [];

    /// <summary>
    /// 优先自动点击的选项
    /// </summary>
    private List<string> _selectList = [];

    private PostMessageSimulator? _postMessageSimulator;
    
    private readonly bool _isCustomConfiguration;

    public AutoSkipTrigger()
    {
        _autoSkipAssets = AutoSkipAssets.Instance;
        _config = TaskContext.Instance().Config.AutoSkipConfig;
    }
    
    /// <summary>
    /// 用于内部的其他方法调用
    /// </summary>
    /// <param name="config"></param>
    public AutoSkipTrigger(AutoSkipConfig config)
    {
        _autoSkipAssets = AutoSkipAssets.Instance;
        _config = config;
        _isCustomConfiguration = true;
    }

    public void Init()
    {
        IsEnabled = _config.Enabled;
        IsBackgroundRunning = _config.RunBackgroundEnabled;
        // IsUseInteractionKey = _config.SelectChatOptionType == SelectChatOptionTypes.UseInteractionKey;
        _postMessageSimulator = TaskContext.Instance().PostMessageSimulator;

        if (!_isCustomConfiguration)
        {
            InitKeyword();
        }
    }

    private void InitKeyword()
    {
        try
        {
            var defaultPauseListJson = Global.ReadAllTextIfExist(@"Assets\Config\Skip\default_pause_options.json");
            if (!string.IsNullOrEmpty(defaultPauseListJson))
            {
                _defaultPauseList = JsonSerializer.Deserialize<List<string>>(defaultPauseListJson, ConfigService.JsonOptions) ?? [];
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "读取自动剧情默认暂停点击关键词列表失败");
            MessageBox.Error("读取自动剧情默认暂停点击关键词列表失败，请确认修改后的自动剧情默认暂停点击关键词内容格式是否正确！");
        }

        try
        {
            var pauseListJson = Global.ReadAllTextIfExist(@"Assets\Config\Skip\pause_options.json");
            if (!string.IsNullOrEmpty(pauseListJson))
            {
                _pauseList = JsonSerializer.Deserialize<List<string>>(pauseListJson, ConfigService.JsonOptions) ?? [];
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "读取自动剧情暂停点击关键词列表失败");
            MessageBox.Error("读取自动剧情暂停点击关键词列表失败，请确认修改后的自动剧情暂停点击关键词内容格式是否正确！");
        }

        try
        {
            var selectListJson = Global.ReadAllTextIfExist(@"Assets\Config\Skip\select_options.json");
            if (!string.IsNullOrEmpty(selectListJson))
            {
                _selectList = JsonSerializer.Deserialize<List<string>>(selectListJson, ConfigService.JsonOptions) ?? [];
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "读取自动剧情优先点击选项列表失败");
            MessageBox.Error("读取自动剧情优先点击选项列表失败，请确认修改后的自动剧情优先点击选项内容格式是否正确！");
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
        UseBackgroundOperation = IsBackgroundRunning && !SystemControl.IsGenshinImpactActive();

        _prevExecute = DateTime.Now;

        GetDailyRewardsEsc(_config, content);

        // 找左上角剧情自动的按钮
        using var foundRectArea = content.CaptureRectArea.Find(_autoSkipAssets.DisabledUiButtonRo);

        var isPlaying = !foundRectArea.IsEmpty(); // 播放中

        if (!isPlaying && (DateTime.Now - _prevPlayingTime).TotalSeconds <= 5)
        {
            // 关闭弹出页
            if (_config.ClosePopupPagedEnabled)
            {
                ClosePopupPage(content);
            }

            // 自动剧情点击3s内判断
            if ((DateTime.Now - _prevPlayingTime).TotalMilliseconds < 3000)
            {
                if (!TaskContext.Instance().Config.AutoSkipConfig.SubmitGoodsEnabled)
                {
                    return;
                }

                // 提交物品
                if (SubmitGoods(content))
                {
                    return;
                }
            }
        }

        if (isPlaying)
        {
            _prevPlayingTime = DateTime.Now;
            if (TaskContext.Instance().Config.AutoSkipConfig.QuicklySkipConversationsEnabled)
            {
                if (IsUseInteractionKey)
                {
                    _postMessageSimulator? .SimulateActionBackground(GIActions.PickUpOrInteract); // 注意这里不是交互键 NOTE By Ayu0K: 这里确实是交互键
                }
                else
                {
                    _postMessageSimulator?.KeyPressBackground(User32.VK.VK_SPACE);
                }
            }

            // 对话选项选择
            bool hasOption;
            if (UseBackgroundOperation || IsUseInteractionKey)
            {
                hasOption = ChatOptionChooseUseKey(content.CaptureRectArea);
            }
            else
            {
                hasOption = ChatOptionChoose(content.CaptureRectArea);
            }


            // 邀约选项选择 1s 1次
            if (_config.AutoHangoutEventEnabled && !hasOption)
            {
                if ((DateTime.Now - _prevHangoutExecute).TotalMilliseconds < 1200)
                {
                    return;
                }

                _prevHangoutExecute = DateTime.Now;
                HangoutOptionChoose(content.CaptureRectArea);
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
        if ((DateTime.Now - _prevClickTime).TotalMilliseconds > 1200)
        {
            using var grayMat = new Mat(content.CaptureRectArea.CacheGreyMat, new Rect(0, content.CaptureRectArea.CacheGreyMat.Height / 3, content.CaptureRectArea.CacheGreyMat.Width, content.CaptureRectArea.CacheGreyMat.Height / 3));
            var blackCount = OpenCvCommonHelper.CountGrayMatColor(grayMat, 0);
            var rate = blackCount * 1d / (grayMat.Width * grayMat.Height);
            if (rate is >= 0.5 and < 0.98999)
            {
                if (UseBackgroundOperation)
                {
                    TaskContext.Instance().PostMessageSimulator?.LeftButtonClickBackground();
                }
                else
                {
                    Simulation.SendInput.Mouse.LeftButtonClick();
                }

                _logger.LogInformation("自动剧情：{Text} 比例 {Rate}", "点击黑屏", rate.ToString("F"));

                _prevClickTime = DateTime.Now;
                return true;
            }
        }

        return false;
    }

    private void HangoutOptionChoose(ImageRegion captureRegion)
    {
        var selectedRects = captureRegion.FindMulti(_autoSkipAssets.HangoutSelectedRo);
        var unselectedRects = captureRegion.FindMulti(_autoSkipAssets.HangoutUnselectedRo);
        if (selectedRects.Count > 0 || unselectedRects.Count > 0)
        {
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

            hangoutOptionList = hangoutOptionList.Where(hangoutOption => hangoutOption.TextRect != null).ToList();
            if (hangoutOptionList.Count == 0)
            {
                return;
            }

            // OCR识别选项文字
            foreach (var hangoutOption in hangoutOptionList)
            {
                var text = OcrFactory.Paddle.Ocr(hangoutOption.TextRect!.SrcMat);
                hangoutOption.OptionTextSrc = StringUtils.RemoveAllEnter(text);
            }

            // 优先选择分支选项
            if (!string.IsNullOrEmpty(_config.AutoHangoutEndChoose))
            {
                var chooseList = HangoutConfig.Instance.HangoutOptions[_config.AutoHangoutEndChoose];
                foreach (var hangoutOption in hangoutOptionList)
                {
                    foreach (var str in chooseList)
                    {
                        if (hangoutOption.OptionTextSrc.Contains(str))
                        {
                            HangoutOptionClick(hangoutOption);
                            _logger.LogInformation("邀约分支[{Text}]关键词[{Str}]命中", _config.AutoHangoutEndChoose, str);
                            AutoHangoutSkipLog(hangoutOption.OptionTextSrc);
                            VisionContext.Instance().DrawContent.RemoveRect("HangoutSelected");
                            VisionContext.Instance().DrawContent.RemoveRect("HangoutUnselected");
                            return;
                        }
                    }
                }
            }

            // 没有停留的选项 优先选择未点击的的选项
            foreach (var hangoutOption in hangoutOptionList)
            {
                if (!hangoutOption.IsSelected)
                {
                    HangoutOptionClick(hangoutOption);
                    AutoHangoutSkipLog(hangoutOption.OptionTextSrc);
                    VisionContext.Instance().DrawContent.RemoveRect("HangoutSelected");
                    VisionContext.Instance().DrawContent.RemoveRect("HangoutUnselected");
                    return;
                }
            }

            // 没有未点击的选项 选择第一个已点击选项
            HangoutOptionClick(hangoutOptionList[0]);
            AutoHangoutSkipLog(hangoutOptionList[0].OptionTextSrc);
            VisionContext.Instance().DrawContent.RemoveRect("HangoutSelected");
            VisionContext.Instance().DrawContent.RemoveRect("HangoutUnselected");
        }
        else
        {
            // 没有邀约选项 寻找跳过按钮
            if (_config.AutoHangoutPressSkipEnabled)
            {
                using var skipRa = captureRegion.Find(_autoSkipAssets.HangoutSkipRo);
                if (skipRa.IsExist())
                {
                    if (UseBackgroundOperation && !SystemControl.IsGenshinImpactActive())
                    {
                        skipRa.BackgroundClick();
                    }
                    else
                    {
                        skipRa.Click();
                    }

                    AutoHangoutSkipLog("点击跳过按钮");
                }
            }
        }
    }

    private bool IsOrangeOption(Mat textMat)
    {
        // 只提取橙色
        // Cv2.ImWrite($"log/text{DateTime.Now:yyyyMMddHHmmssffff}.png", textMat);
        using var bMat = OpenCvCommonHelper.Threshold(textMat, new Scalar(243, 195, 48), new Scalar(255, 205, 55));
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
            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
            _prevGetDailyRewardsTime = DateTime.MinValue;
            primogemRa.Dispose();
        });
    }

    [GeneratedRegex(@"^[a-zA-Z0-9]+$")]
    private static partial Regex EnOrNumRegex();

    /// <summary>
    /// 5.2 版本直接交互键就能使用的对话选择
    /// </summary>
    /// <param name="region"></param>
    /// <returns></returns>
    private bool ChatOptionChooseUseKey(ImageRegion region)
    {
        if (_config.IsClickNoneChatOption())
        {
            return false;
        }
        
        using var chatOptionResult = region.Find(_autoSkipAssets.OptionIconRo);
        var isInChat = false;
        isInChat = chatOptionResult.IsExist();
        if (!isInChat)
        {
            using var pickRa = region.Find(AutoPickAssets.Instance.ChatPickRo);
            isInChat = pickRa.IsExist();
        }

        if (isInChat)
        {
            var fKey = AutoPickAssets.Instance.PickVk;
            if (_config.IsClickFirstChatOption())
            {
                _postMessageSimulator?.KeyPressBackground(fKey);
            }
            else if (_config.IsClickRandomChatOption())
            {
                var random = new Random();
                // 随机 0~4 的数字
                var r = random.Next(0, 5);
                for (var j = 0; j < r; j++)
                {
                    _postMessageSimulator?.KeyPressBackground(User32.VK.VK_S);
                    Thread.Sleep(100);
                }

                Thread.Sleep(50);
                _postMessageSimulator?.KeyPressBackground(fKey);
            }
            else
            {
                _postMessageSimulator?.KeyPressBackground(User32.VK.VK_W);
                Thread.Sleep(100);
                _postMessageSimulator?.KeyPressBackground(fKey);
            }
            
            AutoSkipLog("交互键点击(后台)");

            return true;
        }

        return false;
    }

    /// <summary>
    /// 新的对话选项选择
    ///
    /// 返回 true 表示存在对话选项，但是不一定点击了
    /// </summary>
    private bool ChatOptionChoose(ImageRegion region)
    {
        if (_config.IsClickNoneChatOption())
        {
            return false;
        }

        var assetScale = TaskContext.Instance().SystemInfo.AssetScale;

        // 感叹号识别 遇到直接点击
        using var exclamationIconRa = region.Find(_autoSkipAssets.ExclamationIconRo);
        if (!exclamationIconRa.IsEmpty())
        {
            Thread.Sleep(_config.AfterChooseOptionSleepDelay);
            exclamationIconRa.Click();
            AutoSkipLog("点击感叹号选项");
            return true;
        }

        // 气泡识别
        var chatOptionResultList = region.FindMulti(_autoSkipAssets.OptionIconRo);
        if (chatOptionResultList.Count > 0)
        {
            // 第一个元素就是最下面的
            chatOptionResultList = [.. chatOptionResultList.OrderByDescending(r => r.Y)];

            // 通过最下面的气泡框来文字识别
            var lowest = chatOptionResultList[0];
            var ocrRect = new Rect((int)(lowest.X + lowest.Width + 8 * assetScale), region.Height / 12,
                (int)(535 * assetScale), (int)(lowest.Y + lowest.Height + 30 * assetScale - region.Height / 12d));
            var ocrResList = region.FindMulti(new RecognitionObject
            {
                RecognitionType = RecognitionTypes.Ocr,
                RegionOfInterest = ocrRect
            });
            //using var ocrMat = new Mat(region.SrcGreyMat, ocrRect);
            //// Cv2.ImWrite("log/ocrMat.png", ocrMat);
            //var ocrRes = OcrFactory.Paddle.OcrResult(ocrMat);

            // 删除为空的结果 和 纯英文的结果
            var rs = new List<Region>();
            // 按照y坐标排序
            ocrResList = [.. ocrResList.OrderBy(r => r.Y)];
            for (var i = 0; i < ocrResList.Count; i++)
            {
                var item = ocrResList[i];
                if (string.IsNullOrEmpty(item.Text) || (item.Text.Length < 5 && EnOrNumRegex().IsMatch(item.Text)))
                {
                    continue;
                }

                if (i != ocrResList.Count - 1)
                {
                    if (ocrResList[i + 1].Y - ocrResList[i].Y > 150)
                    {
                        Debug.WriteLine($"存在Y轴偏差过大的结果，忽略:{item.Text}");
                        continue;
                    }
                }

                rs.Add(item);
            }

            if (rs.Count > 0)
            {
                // 用户自定义关键词 匹配
                foreach (var item in rs)
                {
                    // 选择关键词
                    if (_selectList.Any(s => item.Text.Contains(s)))
                    {
                        ClickOcrRegion(item);
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
                    var textMat = item.ToImageRegion().SrcMat;
                    if (IsOrangeOption(textMat))
                    {
                        if (_config.AutoGetDailyRewardsEnabled && (item.Text.Contains("每日") || item.Text.Contains("委托")))
                        {
                            ClickOcrRegion(item, "每日委托");
                            _prevGetDailyRewardsTime = DateTime.Now; // 记录领取时间
                        }
                        else if (_config.AutoReExploreEnabled && (item.Text.Contains("探索") || item.Text.Contains("派遣")))
                        {
                            ClickOcrRegion(item, "探索派遣");
                            Thread.Sleep(800); // 等待探索派遣界面打开
                            new OneKeyExpeditionTask().Run(_autoSkipAssets);
                        }
                        else
                        {
                            ClickOcrRegion(item);
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
                if (_config.IsClickFirstChatOption())
                {
                    clickRegion = rs[0];
                }
                else if (_config.IsClickRandomChatOption())
                {
                    var random = new Random();
                    clickRegion = rs[random.Next(0, rs.Count)];
                }

                ClickOcrRegion(clickRegion);
                AutoSkipLog(clickRegion.Text);
            }
            else
            {
                var clickRect = lowest;
                if (_config.IsClickFirstChatOption())
                {
                    clickRect = chatOptionResultList[^1];
                }

                // 没OCR到文字，直接选择气泡选项
                Thread.Sleep(_config.AfterChooseOptionSleepDelay);
                ClickOcrRegion(clickRect);
                var msg = _config.IsClickFirstChatOption() ? "第一个" : "最后一个";
                AutoSkipLog($"点击{msg}气泡选项");
            }

            return true;
        }

        return false;
    }

    private void ClickOcrRegion(Region region, string optionType = "")
    {
        if (string.IsNullOrEmpty(optionType))
        {
            Thread.Sleep(_config.AfterChooseOptionSleepDelay);
        }

        if (UseBackgroundOperation && !SystemControl.IsGenshinImpactActive())
        {
            region.BackgroundClick();
        }
        else
        {
            region.Click();
        }

        AutoSkipLog(region.Text);
    }

    private void HangoutOptionClick(HangoutOption option)
    {
        if (_config.AutoHangoutChooseOptionSleepDelay > 0)
        {
            Thread.Sleep(_config.AutoHangoutChooseOptionSleepDelay);
        }

        if (UseBackgroundOperation && !SystemControl.IsGenshinImpactActive())
        {
            option.BackgroundClick();
        }
        else
        {
            option.Click();
        }
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
        if (!_config.ClosePopupPagedEnabled)
        {
            return;
        }
        
        content.CaptureRectArea.Find(_autoSkipAssets.PageCloseRo, pageCloseRoRa =>
        {
            if (!Bv.IsInBigMapUi(content.CaptureRectArea))
            {
                TaskContext.Instance().PostMessageSimulator.KeyPress(User32.VK.VK_ESCAPE);

                AutoSkipLog("关闭弹出页");
                pageCloseRoRa.Dispose();
            }
        });
    }

    private bool SubmitGoods(CaptureContent content)
    {
        using var exclamationRa = content.CaptureRectArea.Find(_autoSkipAssets.SubmitExclamationIconRo);
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

            for (var i = 0; i < rects.Count; i++)
            {
                content.CaptureRectArea.Derive(rects[i]).Click();
                _logger.LogInformation("提交物品：{Text}", "1. 选择物品" + i);
                TaskControl.Sleep(800);

                var btnBlackConfirmRa = TaskControl.CaptureToRectArea(forceNew: true).Find(ElementAssets.Instance.BtnBlackConfirm);
                if (!btnBlackConfirmRa.IsEmpty())
                {
                    btnBlackConfirmRa.Click();
                    _logger.LogInformation("提交物品：{Text}", "2. 放入" + i);
                    TaskControl.Sleep(200);
                }
            }

            TaskControl.Sleep(500);

            using var ra = TaskControl.CaptureToRectArea(forceNew: true);
            using var btnWhiteConfirmRa = ra.Find(ElementAssets.Instance.BtnWhiteConfirm);
            if (!btnWhiteConfirmRa.IsEmpty())
            {
                btnWhiteConfirmRa.Click();
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

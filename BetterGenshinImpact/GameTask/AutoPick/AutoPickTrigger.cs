﻿using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.ONNX.SVTR;
using BetterGenshinImpact.Core.Script.Dependence.Model.TimerConfig;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using BetterGenshinImpact.GameTask.Model.Area;

namespace BetterGenshinImpact.GameTask.AutoPick;

public partial class AutoPickTrigger : ITaskTrigger
{
    private readonly ILogger<AutoPickTrigger> _logger = App.GetLogger<AutoPickTrigger>();
    private readonly ITextInference _pickTextInference = TextInferenceFactory.Pick;

    public string Name => "自动拾取";
    public bool IsEnabled { get; set; }
    public int Priority => 30;
    public bool IsExclusive => false;

    private readonly AutoPickAssets _autoPickAssets;

    /// <summary>
    /// 拾取黑名单
    /// </summary>
    private HashSet<string> _blackList = [];

    /// <summary>
    /// 拾取白名单
    /// </summary>
    private HashSet<string> _whiteList = [];

    private RecognitionObject _pickRo;

    // 外部配置
    private AutoPickExternalConfig? _externalConfig;

    public AutoPickTrigger()
    {
        _autoPickAssets = AutoPickAssets.Instance;
        _pickRo = _autoPickAssets.PickRo;
    }

    public AutoPickTrigger(AutoPickExternalConfig? config) : this()
    {
        _externalConfig = config;
    }

    public void Init()
    {
        var config = TaskContext.Instance().Config.AutoPickConfig;
        IsEnabled = config.Enabled;

        if (config.BlackListEnabled)
        {
            _blackList = ReadJson(@"Assets\Config\Pick\default_pick_black_lists.json");
            var userBlackList = ReadText(@"User\pick_black_lists.txt");
            if (userBlackList.Count > 0)
            {
                _blackList.UnionWith(userBlackList);
            }
        }

        if (config.WhiteListEnabled)
        {
            _whiteList = ReadText(@"User\pick_white_lists.txt");
        }
    }

    private HashSet<string> ReadJson(string jsonFilePath)
    {
        try
        {
            var json = Global.ReadAllTextIfExist(jsonFilePath);
            if (!string.IsNullOrEmpty(json))
            {
                return JsonSerializer.Deserialize<HashSet<string>>(json, ConfigService.JsonOptions) ?? [];
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "读取拾取黑/白名单失败");
            MessageBox.Error("读取拾取黑/白名单失败，请确认修改后的拾取黑/白名单内容格式是否正确！");
        }

        return [];
    }

    private HashSet<string> ReadText(string textFilePath)
    {
        try
        {
            var txt = Global.ReadAllTextIfExist(textFilePath);
            if (!string.IsNullOrEmpty(txt))
            {
                return [..txt.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)];
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "读取拾取黑/白名单失败");
            MessageBox.Error("读取拾取黑/白名单失败，请确认修改后的拾取黑/白名单内容格式是否正确！");
        }

        return [];
    }


    /// <summary>
    /// 用于日志只输出一次
    /// </summary>
    private string _lastText = string.Empty;

    /// <summary>
    /// 用于日志只输出一次
    /// </summary>
    private int _prevClickFrameIndex = -1;

    //private int _fastModePickCount = 0;

    public void OnCapture(CaptureContent content)
    {
        while (RunnerContext.Instance.AutoPickTriggerStopCount > 0)
        {
            Thread.Sleep(1000);
        }

        var speedTimer = new SpeedTimer();

        using var foundRectArea = content.CaptureRectArea.Find(_pickRo);

        if (foundRectArea.IsEmpty())
        {
            // 没有识别到F键，先判断是否有滚轮图标信息
            if (HasScrollIcon(content.CaptureRectArea))
            {
                // 滚轮下
                Simulation.SendInput.Mouse.VerticalScroll(2);
                Thread.Sleep(50);
            }

            return;
        }

        speedTimer.Record($"识别到拾取键");

        if (_externalConfig is { ForceInteraction: true })
        {
            LogPick(content, "直接拾取");
            Simulation.SendInput.Keyboard.KeyPress(AutoPickAssets.Instance.PickVk);
            return;
        }

        var scale = TaskContext.Instance().SystemInfo.AssetScale;
        var config = TaskContext.Instance().Config.AutoPickConfig;

        // 识别到拾取键，开始识别物品图标
        var isExcludeIcon = false;
        _autoPickAssets.ChatIconRo.RegionOfInterest = new Rect(
            foundRectArea.X + (int)(config.ItemIconLeftOffset * scale), foundRectArea.Y,
            (int)((config.ItemTextLeftOffset - config.ItemIconLeftOffset) * scale), foundRectArea.Height);
        var chatIconRa = content.CaptureRectArea.Find(_autoPickAssets.ChatIconRo);
        speedTimer.Record("识别聊天图标");
        if (!chatIconRa.IsEmpty())
        {
            // 物品图标是聊天气泡，一般是NPC对话，文字不在白名单不拾取
            isExcludeIcon = true;
        }
        else
        {
            _autoPickAssets.SettingsIconRo.RegionOfInterest = _autoPickAssets.ChatIconRo.RegionOfInterest;
            var settingsIconRa = content.CaptureRectArea.Find(_autoPickAssets.SettingsIconRo);
            speedTimer.Record("识别设置图标");
            if (!settingsIconRa.IsEmpty())
            {
                // 物品图标是设置图标，一般是解谜、活动、电梯等
                isExcludeIcon = true;
            }
        }

        if (!config.WhiteListEnabled && isExcludeIcon)
        {
            // 默认不拾取且没有白名单直接放弃OCR
            return;
        }

        if (!config.WhiteListEnabled && !config.BlackListEnabled && !isExcludeIcon)
        {
            // 没有黑白名单直接拾取
            Simulation.SendInput.Keyboard.KeyPress(AutoPickAssets.Instance.PickVk);
            LogPick(content, "黑名单未启用，直接拾取");
        }

        //if (config.FastModeEnabled && !isExcludeIcon)
        //{
        //    _fastModePickCount++;
        //    if (_fastModePickCount > 2)
        //    {
        //        _fastModePickCount = 0;
        //        LogPick(content, "急速拾取");
        //        Simulation.SendInput.Keyboard.KeyPress(VirtualKeyCode.VK_F);
        //    }
        //    return;
        //}

        // 这类文字识别比较特殊，都是针对某个场景的文字识别，所以暂时未抽象到识别对象中
        // 计算出文字区域
        var textRect = new Rect(foundRectArea.X + (int)(config.ItemTextLeftOffset * scale), foundRectArea.Y,
            (int)((config.ItemTextRightOffset - config.ItemTextLeftOffset) * scale), foundRectArea.Height);
        if (textRect.X + textRect.Width > content.CaptureRectArea.CacheGreyMat.Width
            || textRect.Y + textRect.Height > content.CaptureRectArea.CacheGreyMat.Height)
        {
            Debug.WriteLine("AutoPickTrigger: 文字区域 out of range");
            return;
        }

        // var textMat = new Mat(content.CaptureRectArea.SrcGreyMat, textRect);
        var gradMat = new Mat(content.CaptureRectArea.CacheGreyMat,
            new Rect(textRect.X, textRect.Y, textRect.Width, Math.Min(textRect.Height, 3)));
        var avgGrad = gradMat.Sobel(MatType.CV_32F, 1, 0).Mean().Val0;
        if (avgGrad < -3)
        {
            Debug.WriteLine($"AutoPickTrigger: 已在拾取中，跳过本次拾取 {avgGrad}");
            return;
        }

        string text;
        if (config.OcrEngine == nameof(PickOcrEngineEnum.Yap))
        {
            var textMat = new Mat(content.CaptureRectArea.CacheGreyMat, textRect);
            text = _pickTextInference.Inference(textMat);
        }
        else
        {
            var textMat = new Mat(content.CaptureRectArea.SrcMat, textRect);
            var boundingRect = GetWhiteTextBoundingRect(textMat);
            // 如果找到有效区域
            if (boundingRect.Width > 5 && boundingRect.Height > 5)
            {
                // 截取只包含文字的区域
                var textOnlyMat = new Mat(textMat, new Rect(0, 0,
                    boundingRect.Right + 3 < textMat.Width ? boundingRect.Right + 3 : textMat.Width, textMat.Height));
                text = OcrFactory.Paddle.OcrWithoutDetector(textOnlyMat);
            }
            else
            {
                text = OcrFactory.Paddle.Ocr(textMat);
            }
        }

        speedTimer.Record("文字识别");
        if (!string.IsNullOrEmpty(text))
        {
            // 唯一一个动态拾取项，特殊处理，不拾取
            if (text.Contains("长时间"))
            {
                return;
            }

            // 纳塔部落中文名特殊处理，不拾取
            if (text.Contains("我在") && (text.Contains("声望") || text.Contains("回声") || text.Contains("悬木人") ||
                                        text.Contains("流泉")))
            {
                return;
            }

            // 单个字符不拾取
            var simpleText = PunctuationAndSpacesRegex().Replace(text, "");
            if (simpleText.Length <= 1)
            {
                return;
            }

            if (config.WhiteListEnabled && (_whiteList.Contains(text) || _whiteList.Contains(simpleText)))
            {
                LogPick(content, text);
                Simulation.SendInput.Keyboard.KeyPress(AutoPickAssets.Instance.PickVk);
                return;
            }

            speedTimer.Record("白名单判断");

            if (isExcludeIcon)
            {
                //Debug.WriteLine("AutoPickTrigger: 物品图标是聊天气泡，一般是NPC对话，不拾取");
                return;
            }

            if (config.BlackListEnabled && (_blackList.Contains(text) || _blackList.Contains(simpleText)))
            {
                return;
            }

            speedTimer.Record("黑名单判断");

            LogPick(content, text);
            Simulation.SendInput.Keyboard.KeyPress(AutoPickAssets.Instance.PickVk);
        }

        speedTimer.DebugPrint();
    }

    public static Rect GetWhiteTextBoundingRect(Mat textMat)
    {
        // 预处理提取纯白色文字
        var processedMat = new Mat();
        // 提取白色文字 (255,255,255)
        Cv2.InRange(textMat, new Scalar(254, 254, 254), new Scalar(255, 255, 255), processedMat);
        // 形态学操作，先腐蚀后膨胀，去除噪点并保持文字完整
        var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(2, 2));
        Cv2.MorphologyEx(processedMat, processedMat, MorphTypes.Open, kernel, iterations: 1);
        Cv2.Dilate(processedMat, processedMat, kernel, iterations: 1);
        // 寻找非零区域，即文字区域
        Rect boundingRect = Cv2.BoundingRect(processedMat);
        return boundingRect;
    }


    private bool HasScrollIcon(ImageRegion captureRectArea)
    {
        // 固定区域颜色判断
        // (1062,537)  (255,233,44) 黄色
        // (1062,524)  (255,255,255) 白色
        // (1062,583)  (255,255,255) 白色
        var mat = captureRectArea.SrcMat;
        var color1 = mat.At<Vec3b>(537, 1062);
        var color2 = mat.At<Vec3b>(524, 1062);
        var color3 = mat.At<Vec3b>(554, 1062);
        // BGR 的格式
        if (color1.Item2 == 255 && color1.Item1 == 233 && color1.Item0 == 44
            && color2.Item2 == 255 && color2.Item1 == 255 && color2.Item0 == 255
            && color3.Item2 == 255 && color3.Item1 == 255 && color3.Item0 == 255)
        {
            return true;
        }

        return false;
    }


    /// <summary>
    /// 相同文字前后3帧内只输出一次
    /// </summary>
    /// <param name="content"></param>
    /// <param name="text"></param>
    private void LogPick(CaptureContent content, string text)
    {
        if (_lastText != text || (_lastText == text && Math.Abs(content.FrameIndex - _prevClickFrameIndex) >= 5))
        {
            _logger.LogInformation("交互或拾取：{Text}", text);
        }

        _lastText = text;
        _prevClickFrameIndex = content.FrameIndex;
    }

    [GeneratedRegex(@"^[\p{P} ]+|[\p{P} ]+$")]
    private static partial Regex PunctuationAndSpacesRegex();
}
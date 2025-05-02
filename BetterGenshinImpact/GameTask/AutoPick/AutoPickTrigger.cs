using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.ONNX.SVTR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
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
using Vanara.PInvoke;

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
    private List<string> _blackList = [];

    /// <summary>
    /// 拾取白名单
    /// </summary>
    private List<string> _whiteList = [];

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
        IsEnabled = TaskContext.Instance().Config.AutoPickConfig.Enabled;
        try
        {
            var blackListJson = Global.ReadAllTextIfExist(@"User\pick_black_lists.json");
            if (!string.IsNullOrEmpty(blackListJson))
            {
                _blackList = JsonSerializer.Deserialize<List<string>>(blackListJson, ConfigService.JsonOptions) ?? [];
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "读取拾取黑名单失败");
            MessageBox.Error("读取拾取黑名单失败，请确认修改后的拾取黑名单内容格式是否正确！");
        }

        try
        {
            var whiteListJson = Global.ReadAllTextIfExist(@"User\pick_white_lists.json");
            if (!string.IsNullOrEmpty(whiteListJson))
            {
                _whiteList = JsonSerializer.Deserialize<List<string>>(whiteListJson, ConfigService.JsonOptions) ?? [];
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "读取拾取白名单失败");
            MessageBox.Error("读取拾取白名单失败，请确认修改后的拾取白名单内容格式是否正确！");
        }
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
        if (textRect.X + textRect.Width > content.CaptureRectArea.SrcGreyMat.Width
            || textRect.Y + textRect.Height > content.CaptureRectArea.SrcGreyMat.Height)
        {
            Debug.WriteLine("AutoPickTrigger: 文字区域 out of range");
            return;
        }

        var textMat = new Mat(content.CaptureRectArea.SrcGreyMat, textRect);
        var gradMat = new Mat(textMat, new Rect(0, 0, textRect.Width, Math.Min(textRect.Height, 3)));
        var avgGrad = gradMat.Sobel(MatType.CV_32F, 1, 0).Mean().Val0;
        if (avgGrad < -3)
        {
            Debug.WriteLine($"AutoPickTrigger: 已在拾取中，跳过本次拾取 {avgGrad}");
            return;
        }

        string text;
        if (config.OcrEngine == PickOcrEngineEnum.Yap.ToString())
        {
            var paddedMat = PreProcessForInference(textMat);
            text = _pickTextInference.Inference(paddedMat);
        }
        else
        {
            text = OcrFactory.Paddle.Ocr(textMat);
        }

        speedTimer.Record("文字识别");
        if (!string.IsNullOrEmpty(text))
        {
            text = PunctuationAndSpacesRegex().Replace(text, "");
            // 唯一一个动态拾取项，特殊处理，不拾取
            if (text.Contains("生长时间"))
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
            if (text.Length <= 1)
            {
                return;
            }

            if (_whiteList.Contains(text))
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

            if (_blackList.Contains(text))
            {
                return;
            }

            speedTimer.Record("黑名单判断");

            LogPick(content, text);
            Simulation.SendInput.Keyboard.KeyPress(AutoPickAssets.Instance.PickVk);
        }

        speedTimer.DebugPrint();
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


    private Mat PreProcessForInference(Mat mat)
    {
        // Yap 已经改用灰度图了 https://github.com/Alex-Beng/Yap/commit/c2ad1e7b1442aaf2d80782a032e00876cd1c6c84
        // 二值化
        // Cv2.Threshold(mat, mat, 0, 255, ThresholdTypes.Otsu | ThresholdTypes.Binary);
        //Cv2.AdaptiveThreshold(mat, mat, 255, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.Binary, 31, 3); // 效果不错 但是和模型不搭
        //mat = OpenCvCommonHelper.Threshold(mat, Scalar.FromRgb(235, 235, 235), Scalar.FromRgb(255, 255, 255)); // 识别物品不太行
        // 不知道为什么要强制拉伸到 221x32
        mat = ResizeHelper.ResizeTo(mat, 221, 32);
        // 填充到 384x32
        var padded = new Mat(new Size(384, 32), MatType.CV_8UC1, Scalar.Black);
        padded[new Rect(0, 0, mat.Width, mat.Height)] = mat;
        //Cv2.ImWrite(Global.Absolute("padded.png"), padded);
        return padded;
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
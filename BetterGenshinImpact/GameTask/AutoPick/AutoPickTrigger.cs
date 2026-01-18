using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.ONNX.SVTR;
using BetterGenshinImpact.Core.Script.Dependence.Model.TimerConfig;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.View.Windows;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    /// 拾取黑名单(模糊匹配)
    /// </summary>
    private List<string> _fuzzyBlackList = [];

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

            _fuzzyBlackList = ReadTextList(@"User\pick_fuzzy_black_lists.txt");
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
            ThemedMessageBox.Error("读取拾取黑/白名单失败，请确认修改后的拾取黑/白名单内容格式是否正确！");
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
                // 明确指定使用 char[] 重载版本
                return new HashSet<string>(txt.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "读取拾取黑/白名单失败");
            ThemedMessageBox.Error("读取拾取黑/白名单失败，请确认修改后的拾取黑/白名单内容格式是否正确！");
        }

        return [];
    }

    private List<string> ReadTextList(string textFilePath)
    {
        try
        {
            var txt = Global.ReadAllTextIfExist(textFilePath);
            if (!string.IsNullOrEmpty(txt))
            {
                // 明确指定使用 char[] 重载版本
                return [..txt.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)];
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "读取拾取黑/白名单失败");
            ThemedMessageBox.Error("读取拾取黑/白名单失败，请确认修改后的拾取黑/白名单内容格式是否正确！");
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

        // 存在 L 键位是千星奇遇，无需拾取
        using var lKeyRa = content.CaptureRectArea.Find(_autoPickAssets.LRo);
        if (lKeyRa.IsExist())
        {
            return;
        }

        // 识别到拾取键，开始识别物品图标
        var isExcludeIcon = false;
        _autoPickAssets.ChatIconRo.RegionOfInterest = new Rect(
            foundRectArea.X + (int)(config.ItemIconLeftOffset * scale), foundRectArea.Y,
            (int)((config.ItemTextLeftOffset - config.ItemIconLeftOffset) * scale), foundRectArea.Height);
        using var chatIconRa = content.CaptureRectArea.Find(_autoPickAssets.ChatIconRo);
        speedTimer.Record("识别聊天图标");
        if (!chatIconRa.IsEmpty())
        {
            // 物品图标是聊天气泡，一般是NPC对话，文字不在白名单不拾取
            isExcludeIcon = true;
        }
        else
        {
            _autoPickAssets.SettingsIconRo.RegionOfInterest = _autoPickAssets.ChatIconRo.RegionOfInterest;
            using var settingsIconRa = content.CaptureRectArea.Find(_autoPickAssets.SettingsIconRo);
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

        using var gradMat = new Mat(content.CaptureRectArea.CacheGreyMat,
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
            using var textMat = new Mat(content.CaptureRectArea.SrcMat, textRect);
            var boundingRect = TextRectExtractor.GetTextBoundingRect(textMat);
            // var boundingRect = new Rect(); // 不使用自己写的文字区域提取
            // 如果找到有效区域
            if (boundingRect.X < 20 && boundingRect.Width > 5 && boundingRect.Height > 5)
            {
                // 截取只包含文字的区域
                using var textOnlyMat = new Mat(textMat, new Rect(0, 0,
                    boundingRect.Right + 5 < textMat.Width ? boundingRect.Right + 5 : textMat.Width, textMat.Height));
                text = OcrFactory.Paddle.OcrWithoutDetector(textOnlyMat);

                // if (RuntimeHelper.IsDebug)
                // {
                //     // 如果不等于正确文字，则保存图片
                //     if (text != "烹饪")
                //     {
                //         var path = Global.Absolute("log/pick");
                //         Directory.CreateDirectory(path);
                //         var str = $"{DateTime.Now:yyyyMMddHHmmssfff}";
                //         // textMat.SaveImage(Path.Combine(path, $"pick_ocr_ori_{str}.png"));
                //         // 画上 boundingRect
                //         Cv2.Rectangle(textMat, boundingRect, new Scalar(0, 0, 255), 1);
                //         textMat.SaveImage(Path.Combine(path, $"pick_ocr_rect_{str}.png"));
                //         bin.SaveImage(Path.Combine(path, $"bin_{str}.png"));
                //     }
                // }
            }
            else
            {
                Debug.WriteLine("-- 无法识别到有效文字区域，尝试直接OCR DET");
                text = OcrFactory.Paddle.Ocr(textMat);
            }
        }

        speedTimer.Record("文字识别");
        if (!string.IsNullOrEmpty(text))
        {
            // 处理OCR识别结果，清理无效字符并确保引号配对
            text = ProcessOcrText(text);

            if (DoNotPick(text))
            {
                return;
            }

            // 单个字符不拾取
            if (text.Length <= 1)
            {
                return;
            }

            if (config.WhiteListEnabled && _whiteList.Contains(text))
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

            if (config.BlackListEnabled)
            {
                if (_blackList.Contains(text))
                {
                    return;
                }

                if (_fuzzyBlackList.Count > 0)
                {
                    if (_fuzzyBlackList.Any(item => text.Contains(item)))
                    {
                        return;
                    }
                }
            }

            speedTimer.Record("黑名单判断");

            LogPick(content, text);
            Simulation.SendInput.Keyboard.KeyPress(AutoPickAssets.Instance.PickVk);
        }

        speedTimer.DebugPrint();
    }

    private bool DoNotPick(string text)
    {
        // 唯一一个动态拾取项，特殊处理，不拾取
        if (text.Contains("长时间"))
        {
            return true;
        }

        // 纳塔部落中文名特殊处理，不拾取
        if (text.Contains("我在") && (text.Contains("声望") || text.Contains("回声") || text.Contains("悬木人") ||
                                    text.Contains("流泉")))
        {
            return true;
        }

        // 挪德卡莱聚所中文名特殊处理，不拾取
        if (text.Contains("聚所"))
        {
            return true;
        }

        if (text.Contains("霜月") && text.Contains("坊"))
        {
            return true;
        }

        if (text.Contains("叮铃") || text.Contains("眶螂") || (text.Contains("蛋卷") && text.Contains("坊")))
        {
            return true;
        }

        if (text.Contains("西风成垒") || text.Contains("望崖营壁") || text.Contains("魔女的花园"))
        {
            return true;
        }

        return false;
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

    /// <summary>
    /// 高性能处理OCR识别的文字结果
    /// 1. 替换【、[ 为「，替换】、] 为」
    /// 2. 清理左边非「字符和中文的字符
    /// 3. 清理右边非」字符和中文的字符  
    /// 4. 确保引号配对：有「必有」，有」必有「
    /// </summary>
    /// <param name="text">OCR识别的原始文字</param>
    /// <returns>处理后的文字</returns>
    private static string ProcessOcrText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // 0. 首先替换相似的括号字符并删除换行符、空格，使用Span<char>进行原地替换以获得最佳性能
        Span<char> chars = stackalloc char[text.Length];
        text.AsSpan().CopyTo(chars);

        int writeIndex = 0;
        bool hasChanges = false;

        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];

            // 跳过换行符、回车符、空格、制表符等空白字符
            if (char.IsWhiteSpace(c))
            {
                hasChanges = true;
                continue;
            }

            // 替换括号字符
            if (c == '【' || c == '[')
            {
                chars[writeIndex++] = '「';
                hasChanges = true;
            }
            else if (c == '】' || c == ']')
            {
                chars[writeIndex++] = '」';
                hasChanges = true;
            }
            else
            {
                chars[writeIndex++] = c;
            }
        }

        // 如果有变化，使用处理后的字符；否则使用原字符串的Span
        ReadOnlySpan<char> span = hasChanges ? chars.Slice(0, writeIndex) : text.AsSpan();
        int start = 0;
        int end = span.Length - 1;

        // 1. 从左边开始，删除非「字符和中文的字符
        while (start <= end)
        {
            char c = span[start];
            if (c == '「' || (c >= 0x4E00 && c <= 0x9FFF)) // 「字符或中文字符
                break;
            start++;
        }

        // 2. 从右边开始，删除非」字符和中文的字符
        while (end >= start)
        {
            char c = span[end];
            if (c == '」' || c == '！' || (c >= 0x4E00 && c <= 0x9FFF)) // 」字符或中文字符
                break;
            end--;
        }

        // 如果所有字符都被删除了
        if (start > end)
            return string.Empty;

        // 获取清理后的文字
        var cleanedSpan = span.Slice(start, end - start + 1);

        // 3. 检查并补充引号配对
        bool hasLeftQuote = false;
        bool hasRightQuote = false;

        // 快速扫描是否存在引号
        for (int i = 0; i < cleanedSpan.Length; i++)
        {
            if (cleanedSpan[i] == '「')
                hasLeftQuote = true;
            else if (cleanedSpan[i] == '」')
                hasRightQuote = true;
        }

        // 根据引号配对规则补充
        if (hasLeftQuote && !hasRightQuote)
        {
            // 有「但没有」，在末尾补充」
            Debug.WriteLine("补充缺失的右引号");
            return string.Concat(cleanedSpan, "」");
        }
        else if (hasRightQuote && !hasLeftQuote)
        {
            // 有」但没有「，在开头补充「
            Debug.WriteLine("补充缺失的左引号");
            return string.Concat("「", cleanedSpan);
        }

        return cleanedSpan.ToString();
    }
}
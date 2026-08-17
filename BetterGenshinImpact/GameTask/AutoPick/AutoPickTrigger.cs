using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.ONNX.SVTR;
using BetterGenshinImpact.Core.Script.Dependence.Model.TimerConfig;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.GameTask.Common.BgiVision;
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

    public string Name => "自动拾取";
    public bool IsEnabled { get; set; }
    public int Priority => 30;
    public bool IsExclusive => false;

    private AutoPickAssets _autoPickAssets = null!;

    /// <summary>
    /// 黑名单模式的不拾取列表
    /// </summary>
    private HashSet<string> _blackList = [];

    /// <summary>
    /// 黑名单模式的不拾取列表(模糊匹配)
    /// </summary>
    private List<string> _fuzzyBlackList = [];

    /// <summary>
    /// 黑名单模式的拾取列表
    /// </summary>
    private HashSet<string> _whiteList = [];

    /// <summary>
    /// 白名单模式最终需要拾取的列表
    /// </summary>
    private HashSet<string> _whitelistModeFinalPickList = [];

    private RecognitionObject _pickRo = null!;

    // 外部配置
    private AutoPickExternalConfig? _externalConfig;

    public AutoPickTrigger()
    {
    }

    public AutoPickTrigger(AutoPickExternalConfig? config) : this()
    {
        _externalConfig = config;
    }

    public void Init()
    {
        var config = TaskContext.Instance().Config.AutoPickConfig;
        IsEnabled = config.Enabled;

        var blackList = new HashSet<string>();
        var fuzzyBlackList = new List<string>();
        var whiteList = new HashSet<string>();
        var whitelistModeFinalPickList = new HashSet<string>();

        if (config.Mode == AutoPickMode.Blacklist)
        {
            blackList = ReadJson(@"Assets\Config\Pick\default_pick_black_lists.json");
            blackList.UnionWith(ReadText(@"User\pick_black_lists.txt"));
            fuzzyBlackList = ReadTextList(@"User\pick_fuzzy_black_lists.txt");

            if (config.BlacklistModePickEnabled)
            {
                whiteList = ReadText(@"User\pick_white_lists.txt");
            }
        }
        else
        {
            whitelistModeFinalPickList = ReadJson(@"Assets\Config\Pick\default_pick_white_lists.json");
            whitelistModeFinalPickList.UnionWith(ReadText(@"User\pick_whitelist_mode_pick_lists.txt"));
            if (config.WhitelistModeDoNotPickEnabled)
            {
                whitelistModeFinalPickList.ExceptWith(ReadText(@"User\pick_whitelist_mode_do_not_pick_lists.txt"));
            }
        }

        // 使用完整的新集合替换旧集合，防止关闭规则或切换模式后残留旧数据。
        _blackList = blackList;
        _fuzzyBlackList = fuzzyBlackList;
        _whiteList = whiteList;
        _whitelistModeFinalPickList = whitelistModeFinalPickList;
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
            _logger.LogError(e, "读取拾取名单配置失败");
            ThemedMessageBox.Error("读取拾取名单配置失败，请确认修改后的名单内容格式是否正确！");
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
            _logger.LogError(e, "读取拾取名单配置失败");
            ThemedMessageBox.Error("读取拾取名单配置失败，请确认修改后的名单内容格式是否正确！");
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
                return [..txt.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)];
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "读取拾取名单配置失败");
            ThemedMessageBox.Error("读取拾取名单配置失败，请确认修改后的名单内容格式是否正确！");
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

    private const int ControllerYDialogueBackoffMilliseconds = 1200;
    private DateTime _controllerYBackoffUntil = DateTime.MinValue;

    internal enum PickListDecision
    {
        Allow,
        EmptyText,
        DoNotPick,
        TooShort,
        NotInWhitelist,
        ExcludeIcon,
        BlackList,
        FuzzyBlackList
    }

    //private int _fastModePickCount = 0;

    public void OnCapture(CaptureContent content)
    {
        _autoPickAssets = AutoPickAssets.Get(content.CaptureRectArea, TaskContext.Instance().Config.AutoPickConfig.PickKey);
        _pickRo = _autoPickAssets.PickRo;
        while (RunnerContext.Instance.AutoPickTriggerStopCount > 0)
        {
            Thread.Sleep(1000);
        }

        var speedTimer = new SpeedTimer();
        var forceInteraction = _externalConfig is { ForceInteraction: true };

        if (!forceInteraction && IsControllerYBackoffActive())
        {
            return;
        }

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

        if (forceInteraction)
        {
            LogPick(content, "直接拾取");
            _autoPickAssets.PressPickKey();
            return;
        }

        var scale = TaskContext.Instance().SystemInfo.AssetScale;
        var config = TaskContext.Instance().Config.AutoPickConfig;

        if (ShouldBackOffControllerYForTalkUi(content))
        {
            return;
        }

        // 存在 L 键位是千星奇遇，无需拾取
        using var lKeyRa = content.CaptureRectArea.Find(RecognitionAssets.Get("AutoPick", "L", content.CaptureRectArea));
        if (lKeyRa.IsExist())
        {
            return;
        }

        if (_autoPickAssets.UseControllerY &&
            HasControllerIconBlacklistTemplate(content.CaptureRectArea,
                CreateControllerPromptIconSearchRect(foundRectArea, content.CaptureRectArea, config, scale),
                _autoPickAssets.ControllerIconBlacklistRos))
        {
            speedTimer.Record("识别手柄图标黑名单");
            BackOffControllerY("图标黑名单");
            return;
        }

        // 识别到拾取键，开始识别物品图标
        var isExcludeIcon = false;
        if (HasControllerDialoguePromptIcon(foundRectArea, content.CaptureRectArea, config, scale))
        {
            isExcludeIcon = true;
            speedTimer.Record("识别手柄聊天图标");
        }
        else
        {
            var iconRoi = CreatePromptIconSearchRect(foundRectArea, content.CaptureRectArea, config, scale);
            var chatIconRo = RecognitionAssets.Get("AutoSkip", "ChatIcon", content.CaptureRectArea).Clone();
            chatIconRo.RegionOfInterest = iconRoi;
            using var chatIconRa = content.CaptureRectArea.Find(chatIconRo);
            speedTimer.Record("识别聊天图标");
            if (!chatIconRa.IsEmpty())
            {
                // 物品图标是聊天气泡，一般是NPC对话，文字不在白名单不拾取
                isExcludeIcon = true;
            }
            else
            {
                var settingsIconRo = RecognitionAssets.Get("AutoPick", "SettingsIcon", content.CaptureRectArea).Clone();
                settingsIconRo.RegionOfInterest = iconRoi;
                using var settingsIconRa = content.CaptureRectArea.Find(settingsIconRo);
                speedTimer.Record("识别设置图标");
                if (!settingsIconRa.IsEmpty())
                {
                    // 物品图标是设置图标，一般是解谜、活动、电梯等
                    isExcludeIcon = true;
                }
            }
        }

        if (config.Mode == AutoPickMode.Whitelist)
        {
            // 白名单模式下，安全图标排除优先于拾取列表。
            if (isExcludeIcon)
            {
                BackOffControllerY("NPC/设置交互图标");
                return;
            }
        }
        else if (!config.BlacklistModePickEnabled && isExcludeIcon)
        {
            // 默认不拾取且没有拾取规则直接放弃OCR
            BackOffControllerY("NPC/设置交互图标");
            return;
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
        using var sobelMat = gradMat.Sobel(MatType.CV_32F, 1, 0);
        var avgGrad = sobelMat.Mean().Val0;
        if (avgGrad < -3)
        {
            Debug.WriteLine($"AutoPickTrigger: 已在拾取中，跳过本次拾取 {avgGrad}");
            return;
        }

        var text = RecognizePickText(content.CaptureRectArea, textRect, config);
        speedTimer.Record("文字识别");
        if (config.OcrEngine == nameof(PickOcrEngineEnum.Yap) && ShouldFallbackToPaddleOcr(text))
        {
            var yapText = text;
            text = RecognizePickTextByPaddle(content.CaptureRectArea, textRect);
            _logger.LogDebug("自动拾取：Yap识别结果不可用({YapText})，Paddle兜底结果：{PaddleText}", yapText, text);
            speedTimer.Record("Paddle兜底识别");
        }

        if (IsAllowedByPickLists(text, isExcludeIcon, config, out var pickText))
        {
            speedTimer.Record("白名单判断");
            speedTimer.Record("黑名单判断");
            LogPick(content, pickText);
            _autoPickAssets.PressPickKey();
        }

        speedTimer.DebugPrint();
    }

    private static string RecognizePickText(ImageRegion captureRectArea, Rect textRect, AutoPickConfig config)
    {
        if (config.OcrEngine == nameof(PickOcrEngineEnum.Yap))
        {
            using var textMat = new Mat(captureRectArea.CacheGreyMat, textRect);
            return TextInferenceFactory.Pick.Value.Inference(textMat);
        }

        return RecognizePickTextByPaddle(captureRectArea, textRect);
    }

    private static string RecognizePickTextByPaddle(ImageRegion captureRectArea, Rect textRect)
    {
        using var textMat = new Mat(captureRectArea.SrcMat, textRect);
        var boundingRect = TextRectExtractor.GetTextBoundingRect(textMat);
        // var boundingRect = new Rect(); // 不使用自己写的文字区域提取
        // 如果找到有效区域
        if (boundingRect.X < 20 && boundingRect.Width > 5 && boundingRect.Height > 5)
        {
            // 截取只包含文字的区域
            using var textOnlyMat = new Mat(textMat, new Rect(0, 0,
                boundingRect.Right + 5 < textMat.Width ? boundingRect.Right + 5 : textMat.Width, textMat.Height));
            return OcrFactory.Paddle.OcrWithoutDetector(textOnlyMat);
        }

        Debug.WriteLine("-- 无法识别到有效文字区域，尝试直接OCR DET");
        return OcrFactory.Paddle.Ocr(textMat);
    }

    internal static bool ShouldFallbackToPaddleOcr(string? rawText)
    {
        return ProcessOcrText(rawText ?? string.Empty).Length <= 1;
    }

    private bool IsControllerYBackoffActive()
    {
        return _autoPickAssets.UseControllerY && DateTime.Now < _controllerYBackoffUntil;
    }

    private bool ShouldBackOffControllerYForTalkUi(CaptureContent content)
    {
        if (!_autoPickAssets.UseControllerY)
        {
            return false;
        }

        if (content.CurrentGameUiCategory != GameUiCategory.Talk && !Bv.IsInTalkUi(content.CaptureRectArea))
        {
            return false;
        }

        BackOffControllerY("对话界面");
        return true;
    }

    private void BackOffControllerY(string reason)
    {
        if (!_autoPickAssets.UseControllerY)
        {
            return;
        }

        _controllerYBackoffUntil = DateTime.Now.AddMilliseconds(ControllerYDialogueBackoffMilliseconds);
        _logger.LogDebug("自动拾取：手柄Y检测到{Reason}，退避{Milliseconds}ms", reason, ControllerYDialogueBackoffMilliseconds);
    }

    private static Rect CreatePromptIconSearchRect(Region foundRectArea, ImageRegion captureRectArea, AutoPickConfig config, double scale)
    {
        var legacyLeft = foundRectArea.X + (int)(config.ItemIconLeftOffset * scale);
        var keyRightLeft = foundRectArea.Right + Math.Max((int)(8 * scale), 1);
        var left = Math.Min(legacyLeft, keyRightLeft);
        var right = foundRectArea.X + (int)(config.ItemTextLeftOffset * scale);
        var top = foundRectArea.Y - Math.Max((int)(4 * scale), 1);
        var bottom = foundRectArea.Bottom + Math.Max((int)(4 * scale), 1);

        return ClampToCaptureRect(new Rect(left, top, Math.Max(right - left, foundRectArea.Width), bottom - top), captureRectArea);
    }

    private bool HasControllerDialoguePromptIcon(Region foundRectArea, ImageRegion captureRectArea, AutoPickConfig config, double scale)
    {
        if (!_autoPickAssets.UseControllerY)
        {
            return false;
        }

        var rect = CreateControllerPromptIconSearchRect(foundRectArea, captureRectArea, config, scale);
        using var iconMat = new Mat(captureRectArea.SrcMat, rect);
        if (iconMat.Empty())
        {
            return false;
        }

        using var hsvMat = new Mat();
        Cv2.CvtColor(iconMat, hsvMat, ColorConversionCodes.BGR2HSV);
        using var whiteMask = new Mat();
        Cv2.InRange(hsvMat, new Scalar(0, 0, 205), new Scalar(180, 80, 255), whiteMask);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect,
            new Size(Math.Max((int)(2 * scale), 1), Math.Max((int)(2 * scale), 1)));
        Cv2.MorphologyEx(whiteMask, whiteMask, MorphTypes.Open, kernel);

        Cv2.FindContours(whiteMask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        var minWidth = Math.Max((int)(24 * scale), 18);
        var minHeight = Math.Max((int)(18 * scale), 14);
        var maxWidth = Math.Max((int)(42 * scale), 28);
        var maxHeight = Math.Max((int)(34 * scale), 22);
        var minArea = Math.Max(300d * scale * scale, 150d);
        var minLeft = Math.Max((int)(24 * scale), 16);
        var maxLeft = Math.Max((int)(80 * scale), 50);
        const double minFillRatio = 0.55;

        foreach (var contour in contours)
        {
            var boundingRect = Cv2.BoundingRect(contour);
            if (boundingRect.Width < minWidth || boundingRect.Height < minHeight ||
                boundingRect.Width > maxWidth || boundingRect.Height > maxHeight ||
                boundingRect.X < minLeft || boundingRect.X > maxLeft)
            {
                continue;
            }

            var area = Cv2.ContourArea(contour);
            var fillRatio = area / (boundingRect.Width * boundingRect.Height);
            if (area >= minArea && fillRatio >= minFillRatio)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool HasControllerIconBlacklistTemplate(ImageRegion captureRectArea, Rect searchRect, IReadOnlyList<RecognitionObject> templates)
    {
        if (templates.Count == 0 || searchRect.Width <= 0 || searchRect.Height <= 0)
        {
            return false;
        }

        foreach (var template in templates)
        {
            var templateMat = template.TemplateImageGreyMat ?? template.TemplateImageMat;
            if (templateMat is null || templateMat.Empty() ||
                templateMat.Width > searchRect.Width || templateMat.Height > searchRect.Height)
            {
                continue;
            }

            var previousRegionOfInterest = template.RegionOfInterest;
            template.RegionOfInterest = searchRect;
            try
            {
                using var match = captureRectArea.Find(template);
                if (match.IsExist())
                {
                    return true;
                }
            }
            finally
            {
                template.RegionOfInterest = previousRegionOfInterest;
            }
        }

        return false;
    }

    private static Rect CreateControllerPromptIconSearchRect(Region foundRectArea, ImageRegion captureRectArea, AutoPickConfig config, double scale)
    {
        var left = foundRectArea.Right - Math.Max((int)(2 * scale), 1);
        var right = foundRectArea.X + (int)(config.ItemTextLeftOffset * scale) - Math.Max((int)(6 * scale), 1);
        var top = foundRectArea.Y - Math.Max((int)(8 * scale), 1);
        var bottom = foundRectArea.Bottom + Math.Max((int)(10 * scale), 1);

        return ClampToCaptureRect(new Rect(left, top, Math.Max(right - left, foundRectArea.Width), bottom - top), captureRectArea);
    }

    private static Rect ClampToCaptureRect(Rect rect, ImageRegion captureRectArea)
    {
        var x = Math.Clamp(rect.X, 0, Math.Max(captureRectArea.Width - 1, 0));
        var y = Math.Clamp(rect.Y, 0, Math.Max(captureRectArea.Height - 1, 0));
        var right = Math.Clamp(rect.X + rect.Width, x + 1, captureRectArea.Width);
        var bottom = Math.Clamp(rect.Y + rect.Height, y + 1, captureRectArea.Height);

        return new Rect(x, y, right - x, bottom - y);
    }

    private bool IsAllowedByPickLists(string rawText, bool isExcludeIcon, AutoPickConfig config, out string text)
    {
        var pickList = config.Mode == AutoPickMode.Whitelist
            ? _whitelistModeFinalPickList
            : _whiteList;
        var decision = EvaluatePickLists(rawText, isExcludeIcon, config, _blackList, _fuzzyBlackList, pickList, out text);
        if (ShouldBackOffControllerYForPickListDecision(decision))
        {
            BackOffControllerY(GetControllerYBackoffReason(decision));
        }

        return decision == PickListDecision.Allow;
    }

    internal static PickListDecision EvaluatePickLists(
        string rawText,
        bool isExcludeIcon,
        AutoPickConfig config,
        IReadOnlySet<string> blackList,
        IReadOnlyCollection<string> fuzzyBlackList,
        IReadOnlySet<string> whiteList,
        out string text)
    {
        text = string.Empty;
        if (string.IsNullOrEmpty(rawText))
        {
            return PickListDecision.EmptyText;
        }

        // 处理OCR识别结果，清理无效字符并确保引号配对
        text = ProcessOcrText(rawText);
        var normalizedText = text;
        if (DoNotPick(normalizedText))
        {
            return PickListDecision.DoNotPick;
        }

        // 单个字符不拾取
        if (normalizedText.Length <= 1)
        {
            return PickListDecision.TooShort;
        }

        if (config.Mode == AutoPickMode.Whitelist)
        {
            if (isExcludeIcon)
            {
                return PickListDecision.ExcludeIcon;
            }

            return whiteList.Contains(normalizedText)
                ? PickListDecision.Allow
                : PickListDecision.NotInWhitelist;
        }

        if (config.BlacklistModePickEnabled && whiteList.Contains(normalizedText))
        {
            return PickListDecision.Allow;
        }

        if (isExcludeIcon)
        {
            // 物品图标是聊天气泡或设置图标，一般是NPC对话、解谜、活动、电梯等。
            return PickListDecision.ExcludeIcon;
        }

        if (blackList.Contains(normalizedText))
        {
            return PickListDecision.BlackList;
        }

        if (fuzzyBlackList.Count > 0 && fuzzyBlackList.Any(item => normalizedText.Contains(item)))
        {
            return PickListDecision.FuzzyBlackList;
        }

        return PickListDecision.Allow;
    }

    internal static bool ShouldBackOffControllerYForPickListDecision(PickListDecision decision)
    {
        return decision is PickListDecision.DoNotPick
            or PickListDecision.NotInWhitelist
            or PickListDecision.ExcludeIcon
            or PickListDecision.BlackList
            or PickListDecision.FuzzyBlackList;
    }

    private static string GetControllerYBackoffReason(PickListDecision decision)
    {
        return decision switch
        {
            PickListDecision.DoNotPick => "内置黑名单",
            PickListDecision.NotInWhitelist => "不在白名单",
            PickListDecision.ExcludeIcon => "NPC/设置交互图标",
            PickListDecision.BlackList => "黑名单",
            PickListDecision.FuzzyBlackList => "模糊黑名单",
            _ => "不可拾取项"
        };
    }

    private static bool DoNotPick(string text)
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
        
        if (text.Contains("月谕圣牌"))
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

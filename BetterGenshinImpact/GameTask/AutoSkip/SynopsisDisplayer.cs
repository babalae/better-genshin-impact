using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Mask;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers.Ui;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.IO;
using System.Diagnostics;
using System.Windows.Media;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using Region = BetterGenshinImpact.GameTask.Model.Area.Region;
using BetterGenshinImpact.GameTask.Runtime;

namespace BetterGenshinImpact.GameTask.AutoSkip;

/*
文件说明

一、功能介绍。
    当触发对话时，将本轮对话的剧情简介在游戏画面上展示出来。

二、实现方式。
    1、此功能完全离线，前期已通过大语言模型将原神绝大部分剧情简介生成好了，结果存放在./Assets/DialogueSynopsis.json中。（具体实现方式和工具已开源在 https://github.com/hrz-x/genshin-impact-synopsis） 
    2、DialogueSynopsis.json文件中每一项代表一段完整对话。key值是对话的ID（从0到n-1），value存放着此对话的剧情简介，以及这段对话的部分台词hash。
    3、SynopsisDisplayer（本文件）做的事情便是不断识别对话内容，与台词hash做匹配，直到匹配上某hash，便利用 MaskWindow 将简介显示在画面上。 

三、台词hash的产生。
    1、OCR提取对话文本。分为2种情况：其一是对话框的文本，提取的是<讲述者>+<对话内容>; 其二是玩家的回答（即选项），提取的是所有回答选项，将其拼接成一条。
    2、剔除无关字符。将除了汉字、数字、大小写英文以外的字符全部剔除。这是为了更好的进行匹配，以及兼容部分ocr识别失误。
    3、将重复出现的文本剔除。为了保证匹配的唯一性，仅保留出现一次的文本，此步骤将所有无意义文本都剔除了，例如“...”、“嗯！”。此步骤将剔除~10%台词，保留~90%高质量有意义台词。
    4、对保留的文本进行hash。选取的算法是MD5，取前12位16进制字符。用此方法保证了全部20w+台词没有重复hash值，且尽可能缩小了json文件大小和内存占用。
    5、根据对话台词数，保留对话最开始5~20条台词hash，越长的对话给的hash条数越多。
    
四、性能开销。
    1、离线下来的剧情简介文件约5MB，全部内容加载时间~100ms。
    2、OCR识别分两类：1）对话内容识别在OnCapture函数内`if(isPlaying)`分支下进行，频率控制为0.5s一次，识别范围仅为游戏下方的对话框；2）回答选项识别复用已经实现的功能，无额外开销。
    3、已经匹配到对话的情况下，在本轮对话不再进行匹配。（绝大部分对话在前5句能匹配成功）
    4、md5和hash碰撞耗时极短，为微妙级。
    5、本功能可选择性开启，关闭时无性能开销。
    6、非对话场景无额外开销。（触发器没有调用OnCapture）
    7、如果存在连续15次（如果当前不处于快速跳过对话，放宽到40次）失败的OCR（即执行了OCR但没匹配到台词hash），为了避免持续占用资源，将在本轮对话锁住此功能
        说明：这种情况往往发生在[玩家在正确匹配到台词之前，在对话框停留太久]。如果开启了快速跳过剧情，一般不会发生。这是为了防止过度性能占用而牺牲一小部分体验的折中方案。可在配置里设置【性能层级】项来由玩家自己进行取舍。
    8、可能存在的异常行为：
        i）  如果对话关键人物的人名是生僻字的话，OCR大概率识别不出来，就很可能匹配不上。
        ii)  如果游戏语言为非简体中文，请不要开启本功能，否则占用不必要的OCR资源。
        iii) 分辨率过低时（低于1280 * 720），讲述者的称谓（部分角色存在）字体会过小，导致对这类角色台词识别效果不好。

五、补充说明。
    1、原神完整对话文本爬取自【https://gi.yatta.moe/chs/archive/quest】，此网站持续维护中，此功能后续将随版本更新持续同步。
    2、当前包含【7.2】版本及之前的所有魔神任务、传说任务、世界任务、活动任务及委托任务。（极少数内容可能由于特殊情况没有覆盖到，遇到欢迎提issue指出）
    3、当前采用的大模型型号为【DeepSeek-V4.1-Flash-1017】。
    4、目前包含2个版本【正经版】和【搞笑版】，由不同风格的提示词生成，多周目玩家可以体验搞笑版。
    5、目前仅支持游戏语言为【中文】以及展示语言为【中文】。
    6、适配绝大部分游戏画面分辨率，不限制16:9比例。（如果有不适配的，请联系我 https://github.com/hrz-x）


*/

/// <summary>
/// 展示剧情简介
/// </summary>
public partial class SynopsisDisplayer
{
    private readonly ILogger<SynopsisDisplayer> _logger = App.GetLogger<SynopsisDisplayer>();
    private readonly AutoSkipConfig _config;

    /// <summary>
    /// 简介风格
    /// 【正经风格】SynopsisStyleType.Serious 
    /// 【搞笑风格】SynopsisStyleType.Funny
    /// </summary>
    public string SynopsisStyle { get; }
    private static readonly Dictionary<string, string> AssetsFilePathDict = new()
    {
        [nameof(SynopsisStyleType.Serious)] = Global.Absolute(@"GameTask\AutoSkip\Assets\DialogueSynopsis.json"),
        [nameof(SynopsisStyleType.Funny)] = Global.Absolute(@"GameTask\AutoSkip\Assets\DialogueSynopsisFunny.json"),
    };

    /// <summary>
    /// 存放最近 MaxRecentMatchCount 次的匹配文本，避免重复匹配
    /// </summary>
    private readonly Queue<string> _recentMatchTexts = new();
    private const int MaxRecentMatchCount = 10;

    /// <summary>
    /// 最大连续捕获失败次数。快速跳过时对话推进快，15 次足够；否则放宽到 40 次。
    /// 直接由当前配置派生，运行中切换开关可立即生效。
    /// </summary>
    private int MaxFailedCatchCount
    {
        get
        {   
            switch (_config.SynopsisPerformance)
            {
                case "低":
                    return _config.QuicklySkipConversationsEnabled ? 10 : 20;
                case "中":
                    return _config.QuicklySkipConversationsEnabled ? 15 : 40;
                case "高":
                    return _config.QuicklySkipConversationsEnabled ? 30 : 60;
                default:
                    return _config.QuicklySkipConversationsEnabled ? 15 : 40;
            }
           
        }
    }
    /// <summary>
    /// 当前连续捕获失败次数
    /// </summary>
    private int _failedCatchCount = 0;
    public void ResetFailedCatchCount() => _failedCatchCount = 0;
    /// <summary>
    /// 最小连续捕获时间间隔
    /// </summary>
    private int MinCatchInterval
    {
        get
        {   
            switch (_config.SynopsisPerformance)
            {
                case "低":
                    return 800;
                case "中":
                    return 500;
                case "高":
                    return 200;
                default:
                    return 500;
            }
           
        }
    }
    private DateTime _prevCatchTime = DateTime.MinValue;


    // 剧情摘要列表，下标为对话ID
    private List<string> _synopsis = [];
    // 台词hash到对话ID的映射
    private Dictionary<string, int> _hashToID = new Dictionary<string, int>();
    

    // 下面几项是用来控制展示的参数
    private const string OverlayKey = "Synopsis";
    private readonly GameRuntime _runtime;
    private DateTime _prevShowTime = DateTime.MinValue;
    
    private bool _isShowing = false;
    private bool _hasSpedUp = false;
    
    /// <summary>
    /// 本轮对话是否已经展示过简介。展示结束（或首次展示）后置 true，
    /// 用于阻止同一对话周期内的重复识别与重复展示。
    /// </summary>
    private bool _shownThisDialogue = false;
    public void ResetDialogueShownFlag() => _shownThisDialogue = false;

    // 默认的文字和背景颜色
    private static readonly Color DefaultTextColor = Color.FromRgb(0x33, 0xE8, 0xD6);
    private static readonly Color DefaultBackgroundColor = Color.FromArgb(0x33, 0, 0, 0);
    

    public SynopsisDisplayer(AutoSkipConfig  config, GameRuntime runtime)
    {
        _runtime = runtime;
        _config = config;
        SynopsisStyle = _config.SynopsisStyle;
        
        LoadAssets();
        _logger.LogInformation("剧情简介已开启！【{Style}】", SynopsisStyle);
    }

    /// <summary>
    /// 从文件中加载事先生成的对话简介数据
    /// </summary>
    private void LoadAssets()
    {
        var sw = Stopwatch.StartNew();

        var filePath = AssetsFilePathDict[SynopsisStyle];
        string json = File.ReadAllText(filePath);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        var fileContent = JsonSerializer.Deserialize<Dictionary<string, SynopsisNode>>(json, options)
                          ?? throw new Exception("DialogueSynopsis.json deserialize failed");

        _synopsis = [];
        _hashToID = new Dictionary<string, int>();

        var ordered = fileContent
            .OrderBy(kv => int.Parse(kv.Key))
            .ToList();

        foreach (var kv in ordered)
        {
            int id = int.Parse(kv.Key);
            var node = kv.Value;

            while (_synopsis.Count <= id)
            {
                _synopsis.Add(string.Empty);
            }
            _synopsis[id] = node.Summary ?? string.Empty;

            if (node.Hash != null)
            {
                foreach (var hash in node.Hash)
                {
                    if (string.IsNullOrEmpty(hash))
                        continue;

                    if (!_hashToID.ContainsKey(hash))
                    {
                        _hashToID[hash] = id;
                    }
                    else
                    {
                        _logger.LogWarning("重复的 hash：{Hash}，原 ID={OldId}，新 ID={NewId}，已忽略",
                            hash, _hashToID[hash], id);
                    }
                }
            }
        }

        sw.Stop();
        _logger.LogInformation(
            "剧情简介加载完成，耗时 {ElapsedMs} ms，共 {Count} 段对话，{HashCount} 条台词 hash",
            sw.ElapsedMilliseconds, _synopsis.Count, _hashToID.Count);
    }

    /// <summary>
    /// 对文本计算 MD5，取前 12 位 16 进制字符（小写）
    /// </summary>
    private static string Md5Hash12(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        byte[] inputBytes = Encoding.UTF8.GetBytes(text);
        byte[] hashBytes = MD5.HashData(inputBytes);

        // 转 16 进制小写字符串
        var sb = new StringBuilder(32);
        foreach (var b in hashBytes)
        {
            sb.Append(b.ToString("x2"));
        }

        return sb.ToString(0, 12);
    }
    
    /// <summary>
    /// 剔除所有汉字、数字、大小写英文字母以外的其他字符
    /// </summary>
    private static string FilterNonAlphanumericChinese(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var sb = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (IsAllowedChar(c))
                sb.Append(c);
        }
        return sb.ToString();
    }

    private static bool IsAllowedChar(char c)
    {
        // 数字 0-9
        if (c >= '0' && c <= '9') return true;
        // 小写英文字母 a-z
        if (c >= 'a' && c <= 'z') return true;
        // 大写英文字母 A-Z
        if (c >= 'A' && c <= 'Z') return true;
        // 汉字（CJK 统一表意文字基本区 U+4E00–U+9FFF）
        if (c >= '\u4E00' && c <= '\u9FFF') return true;

        return false;
    }
    
    public void OffShow()
    {
        _runtime.MaskWindowDrawingBoard.Clear(OverlayKey);
        _isShowing = false;
        _hasSpedUp = false;
    }

    public void UpdateShow()
    {
        if (!_isShowing)
        {
            return;
        }
        if ((DateTime.Now - _prevShowTime).TotalMilliseconds > _config.SynopsisDisplayDurationTimeSecond * 1000)
        {
            OffShow();
        }
    }

    public void SpeedUp()
    {
        // 退出对话时加速展示：将展示起始时间往前推，让剩余展示时间相应缩短
        if (!_isShowing || _hasSpedUp || _prevShowTime == DateTime.MinValue)
        {
            return;
        }

        _hasSpedUp = true;
        
        _prevShowTime = _prevShowTime.AddSeconds(-_config.SynopsisDisplaySpeedUpSecond);
    }

    private void Show(string synopsis)
    {
        // 控制展示样式的参数，默认值在基本所有分辨率下都有较好的展示效果
        var baseFontSize1080P = _config.SynopsisFontSize;
        var maxCharsPerLine = _config.SynopsisMaxCharsPerLine;
        var topX1080P = _config.SynopsisLeftTopX;
        var topY1080P = _config.SynopsisLeftTopY;
        
        
        var fontSize = baseFontSize1080P * _runtime.SystemInfo.ScaleTo1080PRatio;
        var textStyle = new MaskWindowDrawingTextStyle(
            OverlayStyleHelper.ParseRgbaHexColor(_config.SynopsisTextColor) ?? DefaultTextColor,
            fontSize,
            OverlayStyleHelper.ParseRgbaHexColor(_config.SynopsisBackgroundColor) ?? DefaultBackgroundColor
            );
        
        // LLM生成的简介是包含html标签的，这里没用上
        synopsis = synopsis.Replace("<b>", "").Replace("</b>", "");

        // 简单折行
        const int tabWidth = 4;   
        var sb = new StringBuilder(synopsis?.Length ?? 0);
        sb.Append('\t');

        var col = tabWidth;       
        if (!string.IsNullOrEmpty(synopsis))
        {
            foreach (char c in synopsis)
            {
                if (col >= maxCharsPerLine)
                {
                    sb.Append('\n');
                    col = 0;
                }
                sb.Append(c);
                col++;
            }
        }
        sb.Append('\t');
        string wrapped = sb.ToString();

        // double factor = (double)_runtime.SystemInfo.GameScreenSize.Width / _runtime.SystemInfo.ScaleMax1080PCaptureRect.Width;
        double factorW = (double)_runtime.SystemInfo.GameScreenSize.Width / 1920;
        double factorH = (double)_runtime.SystemInfo.GameScreenSize.Height / 1080;

        var text = new MaskWindowDrawingText(wrapped, new System.Windows.Point(topX1080P * factorW, topY1080P * factorH), textStyle);
        _runtime.MaskWindowDrawingBoard.Set(new MaskWindowDrawingGroup(OverlayKey, MaskWindowDrawingKind.Feature), [text]);

        _prevShowTime = DateTime.Now;
        _hasSpedUp = false;
        _isShowing = true;
        _shownThisDialogue = true; 
    }

    private void Match(string keyText)
    {
        keyText = FilterNonAlphanumericChinese(keyText);

        if (_recentMatchTexts.Contains(keyText))
            return;

        _recentMatchTexts.Enqueue(keyText);
        while (_recentMatchTexts.Count > MaxRecentMatchCount)
            _recentMatchTexts.Dequeue();

        var hash = Md5Hash12(keyText);
        // _logger.LogDebug("匹配文本：{Text}，hash：{Hash}", keyText, hash);

        if (_hashToID.TryGetValue(hash, out int id))
        {
            var summary = id < _synopsis.Count ? _synopsis[id] : null;
            if (!string.IsNullOrEmpty(summary))
            {
                _logger.LogDebug("命中对话 ID={Id}，OCR尝试次数：{Num} 简介：{Summary}", id, _failedCatchCount, summary);
                _failedCatchCount = 0;

                Show(summary);
            }
            else
            {
                _logger.LogWarning("命中 hash 但简介为空，ID={Id}", id);
            }
        }
        else
        {
            // _logger.LogDebug("未命中任何 hash");
        }
    }

    public void CatchResponse(List<string> options)
    {
        if (_shownThisDialogue) return; 
        if (_isShowing) return;  
        
        // _logger.LogDebug("识别到回答选项：\n{Options}", string.Join("\n", options));
        if (options.Count == 1)
        {
            // 当回答选项只有一条时，数据来源（https://gi.yatta.moe）将这种情况直接视为 Speaker为‘旅人’的台词
            Match("旅人" + options[0]);
        }
        else
        {
            Match(string.Join("\n", options));
        }
    }
    
    /// <summary>
    /// 捕获当前对话窗口内容
    /// </summary>
    public void CatchDialogue(ImageRegion region)
    {
        if (_shownThisDialogue) return;   
        if (_isShowing) return;
        
        // 频率控制
        if ((DateTime.Now - _prevCatchTime).TotalMilliseconds < MinCatchInterval)
        {
            return;
        }
        _prevCatchTime = DateTime.Now;
        
        if (_failedCatchCount >= MaxFailedCatchCount)
        {
        //    _logger.LogDebug("当前对话已达到最大OCR识别允许次数，不再进行识别 【{Number}次】", MaxFailedCatchCount); 
           return;
        }
        _failedCatchCount += 1;

        // 下述代码是为了获取对话文本框的位置，对所有分辨率（任意比例）适用
        var dialogueBoxPixel1080P = new
        {
            Left = 200,
            Right = 1920 - 200,
            Top = 840,
            Bottom = 1040,
        };
        var scaleX = region.Width / 1920d;
        var scaleY = region.Height / 1080d;
        var rectHeightFixedPixel = (1080 - dialogueBoxPixel1080P.Top) *  Math.Min(scaleX, 1.0);

        // 当前的对话框
        var ocrRect = new Rect(
            (int)(dialogueBoxPixel1080P.Left * scaleX), 
            (int)(region.Height - rectHeightFixedPixel), 
            (int)((dialogueBoxPixel1080P.Right - dialogueBoxPixel1080P.Left) * scaleX), 
            (int)(dialogueBoxPixel1080P.Bottom  * scaleY - (region.Height - rectHeightFixedPixel)) 
        );
        // new Region(ocrRect, region,
        //         new BetterGenshinImpact.GameTask.Model.Area.Converter.TranslationConverter(ocrRect.X,
        //             ocrRect.Y))
        //     .DrawSelf("test");

        // 下述代码是为了获取对话讲述者的识别框位置，识别讲述者没必要用那么大的框，节约OCR资源
        var speakerBoxPixel1080P = new
        {
            Left = 700,
            Right = 1920 - 700,
            Height = 82
        };

        // 当前的对话框
        var ocrRectSpeaker = new Rect(
            (int)(speakerBoxPixel1080P.Left * scaleX), 
            (int)(region.Height - rectHeightFixedPixel), // TopY和ocrRect是一样的
            (int)((speakerBoxPixel1080P.Right - speakerBoxPixel1080P.Left) * scaleX), 
            (int)(speakerBoxPixel1080P.Height  * scaleY) 
        );
        // new Region(ocrRectSpeaker, region,
        //         new BetterGenshinImpact.GameTask.Model.Area.Converter.TranslationConverter(ocrRectSpeaker.X,
        //             ocrRectSpeaker.Y))
        //     .DrawSelf("test");

        // 用黄色匹配讲述者
        var dialogueSpeaker = region.Find(new RecognitionObject
        {
            RecognitionType = RecognitionTypes.ColorRangeAndOcr,
            ColorConversionCode = ColorConversionCodes.BGR2HLS,
            LowerColor = new Scalar(20, 100, 150), // H: 偏黄, S: 足够饱和, L: 偏亮
            UpperColor = new Scalar(40, 255, 255), // H: 黄到黄绿, S/L: 拉满
            RegionOfInterest = ocrRectSpeaker
        }).Text;
        // _logger.LogDebug(dialogueSpeaker);

        // 匹配完整台词
        var dialogueContent = region.Find(new RecognitionObject
        {
            RecognitionType = RecognitionTypes.Ocr,
            RegionOfInterest = ocrRect
        }).Text;
        // _logger.LogDebug(dialogueContent);
        
        // 用下面的代码剔除掉讲述者的称谓
        string keyText;  
        if (dialogueSpeaker.Contains("\n"))// 如果存在称谓，那么 dialogueSpeaker 就是两行
        {
            // 当讲述者存在称谓时，神奇的OCR经常将称谓左边的一朵小花识别为“心”字，在这里特殊处理一下，不过问题就是会让本来就是“心”开头的称谓丢掉这个字
            dialogueSpeaker = dialogueSpeaker.Replace("\n心", "\n");
            int idx = dialogueContent.IndexOf("\n心", StringComparison.Ordinal);
            if (idx >= 0)
            {
                dialogueContent = dialogueContent.Remove(idx, 2).Insert(idx, "\n");
            }
        }
        if (FilterNonAlphanumericChinese(dialogueContent).StartsWith(FilterNonAlphanumericChinese(dialogueSpeaker)))
        {
            keyText = dialogueSpeaker.Split('\n')[0] + FilterNonAlphanumericChinese(dialogueContent).Substring(FilterNonAlphanumericChinese(dialogueSpeaker).Length);
        }
        else
        {
            keyText = dialogueContent;
        }
        
        // 用<讲述者>+<对话内容>进行匹配
        Match(keyText);
    }
}

internal sealed class SynopsisNode
{
    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("hash")]
    public List<string> Hash { get; set; } = new();
}
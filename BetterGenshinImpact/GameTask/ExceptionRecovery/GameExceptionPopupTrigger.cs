using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Timers;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Infrastructure.NetworkRecovery;
using Microsoft.Extensions.Logging;
using Timer = System.Timers.Timer;

namespace BetterGenshinImpact.GameTask.ExceptionRecovery;

/// <summary>
/// 游戏异常弹窗（更新通知 / 连接已断开 / 连接超时 / 网络错误）自动点掉。
/// 常驻触发器；处理期间 <see cref="IsExclusive"/> 为真，调度器只运行本触发器。
/// </summary>
public class GameExceptionPopupTrigger : ITaskTrigger
{
    /// <summary>两次点击之间的最小间隔（秒）</summary>
    private const int ClickIntervalSeconds = 2;

    /// <summary>界面可用性判定的最小间隔（毫秒）。每帧做多个全屏模板匹配代价过高。</summary>
    private const int UiCheckIntervalMs = 1000;

    /// <summary>弹窗检测关键词。只放异常弹窗专有文案，避免命中正常界面的提示语。</summary>
    /// <remarks>"点击进入" 是门屏按钮，只用于 <see cref="PresentKeywords"/> 与 <see cref="SubstringButtonText"/>：
    /// 若放进检测词表，空闲停在门屏也会被点掉，绕过自动进游戏开关。</remarks>
    private static readonly string[] DetectKeywords =
    [
        "连接已断开",
        "连接超时",
        "网络错误",
        "无法登录服务器",
        "更新通知",
    ];

    /// <summary>已进入处理后再判定"弹窗还在不在"，用更宽的词表。</summary>
    private static readonly string[] PresentKeywords =
    [
        "连接已断开",
        "连接超时",
        "网络错误",
        "无法登录服务器",
        "更新通知",
        "通知",
        "提示",
        "点击进入",
    ];

    /// <summary>按钮文案：整段精确匹配</summary>
    private static readonly string[] ExactButtonTexts = ["确认", "取消"];

    /// <summary>按钮文案：包含即算</summary>
    private const string SubstringButtonText = "点击进入";

    private readonly ILogger<GameExceptionPopupTrigger> _logger = App.GetLogger<GameExceptionPopupTrigger>();
    private readonly IPopupPauseGate? _popupPauseGate = App.GetService<IPopupPauseGate>();
    private readonly IRecoverySession? _recoverySession = App.GetService<IRecoverySession>();
    private readonly Timer _watchdog;

    /// <summary>让"点击授权"与"结束处理"互斥：看门狗线程可能在 OCR 期间已放行脚本。</summary>
    private readonly object _actionSync = new();

    private long _lastCheckTimestamp;
    private long _lastClickTimestamp;
    private long _lastUiCheckTimestamp;
    private long _recoveryStartTimestamp;

    /// <summary>处理中。调度器与看门狗会无锁读取它，必须保持 volatile。</summary>
    private volatile bool _recovering;

    public GameExceptionPopupTrigger()
    {
        _watchdog = new Timer { AutoReset = true, Interval = 1000 };
        _watchdog.Elapsed += (_, _) => CheckRecoveryTimeout();
    }

    public string Name => "异常弹窗处理";

    public bool IsEnabled { get; set; }

    public int Priority => 810;

    /// <summary>处理期间独占，使调度器跳过其它触发器（含钓鱼/传送等独占触发器）</summary>
    public bool IsExclusive => _recovering;

    public bool AlwaysActive => true;

    private static PopupRecoveryConfig Config => TaskContext.Instance().Config.PopupRecoveryConfig;

    public void Init()
    {
        IsEnabled = Config.Enabled;
        if (!IsEnabled)
        {
            EndRecovery("功能已关闭");
        }
    }

    public void OnCapture(CaptureContent content)
    {
        if (!Config.Enabled)
        {
            IsEnabled = false;
            return;
        }

        IsEnabled = true;

        var ra = content.CaptureRectArea;

        // 断网登录恢复也在点同一批按钮（登录适配器的 ConfirmNetworkErrorAsync）；两种恢复必须互斥。
        // 让位期间保持 _recovering（弹窗暂停与看门狗仍在），登录恢复结束后本流程继续。
        if (_recoverySession?.IsRecovering == true)
        {
            return;
        }

        if (_recovering)
        {
            StepRecovery(ra);
            return;
        }

        var interval = Math.Max(1, Config.CheckIntervalSeconds) * 1000L;
        if (Stopwatch.GetElapsedTime(_lastCheckTimestamp).TotalMilliseconds < interval)
        {
            return;
        }

        _lastCheckTimestamp = Stopwatch.GetTimestamp();

        if (!ContainsAnyKeyword(ra, DetectKeywords))
        {
            return;
        }

        BeginRecovery();
        StepRecovery(ra);
    }

    private void BeginRecovery()
    {
        lock (_actionSync)
        {
            _recovering = true;
        }

        _recoveryStartTimestamp = Stopwatch.GetTimestamp();
        _lastUiCheckTimestamp = 0;
        _popupPauseGate?.EnterPopupPause("检测到游戏异常弹窗");
        _watchdog.Start();
        _logger.LogWarning("检测到游戏异常弹窗，已暂停脚本并接管实时触发器");
    }

    private void StepRecovery(ImageRegion ra)
    {
        if (!ContainsAnyKeyword(ra, PresentKeywords))
        {
            // 弹窗文字消失不代表界面可用（游戏可能停在门屏或加载中）
            if (!IsInRunnableUi(ra))
            {
                return;
            }

            EndRecovery("弹窗已消失，界面已可用");
            return;
        }

        if (Stopwatch.GetElapsedTime(_lastClickTimestamp).TotalSeconds < ClickIntervalSeconds)
        {
            return;
        }

        // 点击授权与"结束处理"必须在同一临界区：看门狗线程可能已在本次 OCR 期间结束处理并放行脚本，
        // 此时按旧截图点击会和已恢复的脚本抢鼠标。
        lock (_actionSync)
        {
            if (!_recovering)
            {
                return;
            }

            _lastClickTimestamp = Stopwatch.GetTimestamp();

            if (TryClickPopupButton(ra, out var text))
            {
                _logger.LogInformation("已点击弹窗按钮：{Text}", text);
            }
        }
    }

    /// <summary>节流后的界面可用性判定。节流期间返回 false，即"尚不确定"，继续持有挂起。</summary>
    private bool IsInRunnableUi(ImageRegion ra)
    {
        if (Stopwatch.GetElapsedTime(_lastUiCheckTimestamp).TotalMilliseconds < UiCheckIntervalMs)
        {
            return false;
        }

        _lastUiCheckTimestamp = Stopwatch.GetTimestamp();
        return Bv.IsInTaskRunnableUi(ra);
    }

    /// <summary>独立于截图调度线程的归还者，处理超时的兜底。</summary>
    private void CheckRecoveryTimeout()
    {
        if (!_recovering)
        {
            return;
        }

        if (Stopwatch.GetElapsedTime(_recoveryStartTimestamp).TotalSeconds >= Math.Max(1, Config.MaxRecoverySeconds))
        {
            EndRecovery("处理超时");
        }
    }

    private void EndRecovery(string reason)
    {
        bool recovering;
        lock (_actionSync)
        {
            // 先失效再放行：与"点击授权"同一临界区，放行之后不会再发出本轮输入
            recovering = _recovering;
            _recovering = false;
        }

        _watchdog.Stop();
        _popupPauseGate?.ClearPopupPause();

        // 重置检测节流，使下一次接管至少间隔一个检测周期
        _lastCheckTimestamp = Stopwatch.GetTimestamp();

        if (recovering)
        {
            _logger.LogInformation("异常弹窗处理结束：{Reason}", reason);
        }
    }

    private static bool ContainsAnyKeyword(ImageRegion ra, string[] baseKeywords)
    {
        var extra = ParseKeywords(Config.ExtraKeywords);
        var regions = ra.FindMulti(BuildOcrRo(ra));
        try
        {
            foreach (var region in regions)
            {
                foreach (var keyword in baseKeywords)
                {
                    if (region.Text.Contains(keyword, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                foreach (var keyword in extra)
                {
                    if (region.Text.Contains(keyword, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        finally
        {
            DisposeAll(regions);
        }
    }

    /// <summary>先按 OCR 文案点，文案没命中时回退到确认按钮模板。</summary>
    private static bool TryClickPopupButton(ImageRegion ra, out string text)
    {
        text = string.Empty;
        var regions = ra.FindMulti(BuildOcrRo(ra));
        try
        {
            var button = regions.FirstOrDefault(r =>
                ExactButtonTexts.Contains(r.Text.Trim()) ||
                r.Text.Contains(SubstringButtonText, StringComparison.Ordinal));

            if (button != null)
            {
                text = button.Text;
                button.Click();
                return true;
            }
        }
        finally
        {
            DisposeAll(regions);
        }

        if (Bv.ClickConfirmButton(ra))
        {
            text = "(模板)";
            return true;
        }

        return false;
    }

    private static void DisposeAll(List<Region> regions)
    {
        foreach (var region in regions)
        {
            region.Dispose();
        }
    }

    /// <summary>OCR 只扫画面中部，降低开销与误命中。</summary>
    private static RecognitionObject BuildOcrRo(ImageRegion ra)
    {
        var x = (int)(ra.Width * 0.3);
        var y = (int)(ra.Height * 0.1);
        var w = (int)(ra.Width * 0.65);
        var h = (int)(ra.Height * 0.87);
        return RecognitionObject.Ocr(x, y, w, h);
    }

    private static string[] ParseKeywords(string? raw)
    {
        return string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split([',', '，', ';', '；', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

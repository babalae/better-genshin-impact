using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.GameLoading;

public class GameLoadingTrigger : ITaskTrigger
{
    public string Name => "自动开门";

    /// <summary>
    /// 开启了自动进入游戏，并且本次截图会话中还没有完成（进入主界面或超过 5 分钟后自停）
    /// </summary>
    public bool IsEnabledByConfig => _config.AutoEnterGameEnabled && !_finished;

    public int Priority => 999;

    public bool IsExclusive => false;

    public bool IsBiliJudged = false;
    public bool IsBili = false;

    public bool IsBackgroundRunning => true;

    private readonly GenshinStartConfig _config = TaskContext.Instance().Config.GenshinStartConfig;
    private static ILogger<GameLoadingTrigger> _logger = App.GetLogger<GameLoadingTrigger>();


    // private int _enterGameClickCount = 0;
    // private int _welkinMoonClickCount = 0;
    // private int _noneClickCount, _wmNoneClickCount;

    private DateTime _prevExecuteTime = DateTime.MinValue;

    /// <summary>
    /// 实例在启动截图器时创建，5 分钟计时从这里开始
    /// </summary>
    private readonly DateTime _triggerStartTime = DateTime.Now;

    /// <summary>
    /// 已经进入游戏或超时，本次截图会话内不再运行
    /// </summary>
    private volatile bool _finished;

    private bool biliLoginClicked = false;
    private (double x1080, double y1080)? lastAgreementClickPos = null;
    private DateTime _prevAgePromptOcrTime = DateTime.MinValue;
    private bool _agePromptTextMatched = false;
    private List<Region> _latestLoadingOcrRegions = [];

    public GameLoadingTrigger()
    {
    }

    public void OnCapture(CaptureContent content)
    {
        // 2s 一次
        if ((DateTime.Now - _prevExecuteTime).TotalMilliseconds <= 2000)
        {
            return;
        }

        _prevExecuteTime = DateTime.Now;
        // 5min 后自动停止
        if ((DateTime.Now - _triggerStartTime).TotalMinutes >= 5)
        {
            _finished = true;
            return;
        }
        
        // 成功进入游戏判断    
        if (Bv.IsInMainUi(content.CaptureRectArea) || Bv.IsInAnyClosableUi(content.CaptureRectArea) || Bv.IsInDomain(content.CaptureRectArea))
        {
            // _logger.LogInformation("当前在游戏主界面");
            _finished = true;
            return;
        }

        if ((DateTime.Now - _prevAgePromptOcrTime).TotalMilliseconds >= 1000)
        {
            _prevAgePromptOcrTime = DateTime.Now;
            _latestLoadingOcrRegions = content.CaptureRectArea.FindMulti(RecognitionObject.OcrThis);
            if (_latestLoadingOcrRegions.Any(region =>
                    region.Text.Contains("适龄") || region.Text.Contains("监护")))
            {
                // 适龄提示窗口自动关闭
                var agePopup = content.CaptureRectArea.Find(ElementRecognition.Get("BtnWhiteConfirm", content.CaptureRectArea));
                if (!agePopup.IsEmpty())
                {
                    agePopup.Click();
                    _logger.LogInformation("检测到适龄提示，自动点击确认");
                }
            }
        }
        


        // B服判断
        if (!IsBiliJudged)
        {
            try
            {
                var exePath = _config.InstallPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    var configIni = Path.Combine(Path.GetDirectoryName(exePath)!, "config.ini");
                    if (File.Exists(configIni))
                    {
                        var lines = File.ReadAllLines(configIni);
                        foreach (var line in lines)
                        {
                            var kv = line.Trim();
                            if (kv.StartsWith("channel=") && kv.EndsWith("14"))
                            {
                                IsBili = true;
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TaskControl.Logger.LogWarning("B服判断异常: " + ex.Message);
            }
            IsBiliJudged = true;
        }

        // 官服流程：先识别并点击顶号或切号的后一次“进入游戏”弹窗按钮
        if (!IsBili)
        {
            var extraEnterGameBtn = content.CaptureRectArea.Find(RecognitionAssets.Get("GameLoading", "ChooseEnterGame", content.CaptureRectArea));
            if (!extraEnterGameBtn.IsEmpty())
            {
                extraEnterGameBtn.Click();
                return;
            }
        }

        // 点击进入游戏按钮
        var ra = content.CaptureRectArea.Find(RecognitionAssets.Get("GameLoading", "EnterGame", content.CaptureRectArea));

        if (!ra.IsEmpty())
        {
            InputHub.Background.Mouse.LeftButtonClick();
            biliLoginClicked = true;
            return;
        }

        // 只有在"进入游戏"按钮未出现时，才进行B服登录处理
        if (IsBili && !biliLoginClicked)
        {
            // B服流程：处理登录窗口
            var process = Process.GetProcessesByName("YuanShen").FirstOrDefault();
            var (loginWindow, windowType) = GetBiliLoginWindow(process);

            if (process != null && loginWindow != IntPtr.Zero)
            {
                var dpiScale = TaskContext.Instance().DpiScale;
                if (windowType.Contains("协议"))
                {
                    GameCaptureRegion.GameRegion1080PPosClick(960 + 70 * dpiScale, 540 + 75 * dpiScale);
                }

                if (windowType.Contains("登录"))
                {
                    Thread.Sleep(2000);
                    GameCaptureRegion.GameRegion1080PPosClick(960, 540 + 90 * dpiScale);
                    Thread.Sleep(2000);

                    // 检查窗口是否还存在
                    var (remainingWindow, remainingType) = GetBiliLoginWindow(process);
                    if (remainingWindow == IntPtr.Zero)
                    {
                        _logger.LogInformation("B服登录完成，准备进入游戏");
                        // 添加延时确保窗口完全消失
                        Thread.Sleep(2000);
                        // 点击屏幕尝试找回焦点
                        InputHub.Background.Mouse.LeftButtonClick();
                        biliLoginClicked = true;
                    }
                }
            }
        }

        if (Bv.IsInBlessingOfTheWelkinMoon(content.CaptureRectArea))
        {
            GameCaptureRegion.GameRegion1080PPosMove(100, 100);
            InputHub.Background.Mouse.LeftButtonClick();
            Debug.WriteLine("[GameLoading] Click blessing of the welkin moon");
            // TaskControl.Logger.LogInformation("自动点击月卡");
            return;
        }

        // 原石
        var ysRa = content.CaptureRectArea.Find(ElementRecognition.Get("Primogem", content.CaptureRectArea));
        if (!ysRa.IsEmpty())
        {
            GameCaptureRegion.GameRegion1080PPosMove(100, 100);
            InputHub.Background.Mouse.LeftButtonClick();
            Debug.WriteLine("[GameLoading] 跳过原石");
            return;
        }
    }

    // B服登录窗口检测
    private static (IntPtr windowHandle, string windowType) GetBiliLoginWindow(Process process)
    {
        IntPtr bHWnd = IntPtr.Zero;
        string windowType = "";

        User32.EnumWindows((hWnd, lParam) =>
        {
            try
            {
                // 获取窗口标题
                int titleLength = User32.GetWindowTextLength(hWnd);
                if (titleLength > 0)
                {
                    StringBuilder title = new StringBuilder(titleLength + 1);
                    User32.GetWindowText(hWnd, title, title.Capacity);

                    string titleText = title.ToString();

                    // 检查是否是B服登录窗口（通过标题匹配）
                    if (titleText.Contains("bilibili", StringComparison.OrdinalIgnoreCase))
                    {
                        // 检查窗口所有者是否是原神进程
                        var owner = User32.GetWindow(hWnd, User32.GetWindowCmd.GW_OWNER);
                        if (owner != IntPtr.Zero)
                        {
                            User32.GetWindowThreadProcessId(owner, out uint ownerPid);
                            if (ownerPid == process.Id)
                            {
                                // 检查窗口是否可见和启用
                                bool isVisible = User32.IsWindowVisible(hWnd);
                                bool isEnabled = User32.IsWindowEnabled(hWnd);

                                // 检查协议窗口
                                if (titleText.Contains("协议", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (isEnabled)
                                    {
                                        bHWnd = hWnd.DangerousGetHandle();
                                        windowType = "协议";
                                        return false;
                                    }
                                }

                                // 检查登录窗口
                                if (titleText.Contains("登录", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (isEnabled)
                                    {
                                        bHWnd = hWnd.DangerousGetHandle();
                                        windowType = "登录";
                                        return false;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"枚举窗口时出错: {ex.Message}");
            }

            return true;
        }, IntPtr.Zero);

        return (bHWnd, windowType);
    }
};

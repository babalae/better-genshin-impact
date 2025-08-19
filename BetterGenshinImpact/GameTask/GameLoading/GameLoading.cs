using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.GameLoading.Assets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using BetterGenshinImpact.Genshin.Paths;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.Win32;
using System.Windows.Documents;
using System.Linq;
using System.Threading;
using System.Runtime.InteropServices;
using System.Text;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Helpers.Extensions;
using Vanara.PInvoke;
using BetterGenshinImpact.Core.Recognition;
using Fischless.GameCapture;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.GameLoading;

public class GameLoadingTrigger : ITaskTrigger
{
    public string Name => "自动开门";

    public bool IsEnabled { get; set; }

    public int Priority => 999;

    public bool IsExclusive => false;

    public bool IsBackgroundRunning => true;

    private readonly GameLoadingAssets _assets;

    private readonly GenshinStartConfig _config = TaskContext.Instance().Config.GenshinStartConfig;
    private static ILogger<GameLoadingTrigger> _logger = App.GetLogger<GameLoadingTrigger>();


    // private int _enterGameClickCount = 0;
    // private int _welkinMoonClickCount = 0;
    // private int _noneClickCount, _wmNoneClickCount;

    private DateTime _prevExecuteTime = DateTime.MinValue;

    private DateTime _triggerStartTime = DateTime.Now;

    private string GameServer = "";

    private string channelValue = "";

    private string FileName = "";

    private bool biliLoginClicked = false;
    private (double x1080, double y1080)? lastAgreementClickPos = null;

    public GameLoadingTrigger()
    {
        GameLoadingAssets.DestroyInstance();
        _assets = GameLoadingAssets.Instance;
    }

    public void Init()
    {
        IsEnabled = _config.AutoEnterGameEnabled;

        // // 前面没有联动启动原神，这个任务也不用启动
        // if ((DateTime.Now - TaskContext.Instance().LinkedStartGenshinTime).TotalMinutes >= 5)
        // {
        //     IsEnabled = false;
        // }
        if (_config.RecordGameTimeEnabled)
        {
            FileName = Path.GetFileName(_config.InstallPath);
            if (FileName == "GenshinImpact.exe")
            {
                GameServer = "hk4e_global";
                StartStarward();
            }

            if (FileName == "YuanShen.exe")
            {
                string iniPath = Path.GetDirectoryName(_config.InstallPath) + "//config.ini";
                string iniContent;
                string pattern = @"
            ^\s*\[General\]\s*$
            (?:(?!\[).|\r?\n)*
            ^\s*channel=(\S+)
        ";

                try
                {
                    iniContent = File.ReadAllText(iniPath);
                    Regex regex = new Regex(pattern,
                        RegexOptions.Multiline | RegexOptions.IgnorePatternWhitespace | RegexOptions.IgnoreCase);
                    Match match = regex.Match(iniContent);
                    channelValue = match.Success ? match.Groups[1].Value : "";
                }
                catch (Exception e)
                {
                }

                // channelValue = 1 ： 官服
                // channelValue = 14 ： B服
                if (channelValue == "1")
                {
                    GameServer = "hk4e_cn";
                    StartStarward();
                }

                if (channelValue == "14")
                {
                    GameServer = "hk4e_bilibili";
                    StartStarward();
                }


                Debug.WriteLine($"[GameLoading] 从文件读取到游戏区服：{GameServer}");
                // 这里注册表的优先级要比读取文件低，因为使用starward安装原神不会写入注册表
                if (GameServer == null)
                {
                    GameServer = GetGameServerRegistry();
                    Debug.WriteLine($"[GameLoading] 从注册表读取到游戏区服：{GameServer}");
                    StartStarward();
                }
            }
        }
    }

    public bool StartStarward()
    {
        try
        {
            Debug.WriteLine($"[GameLoading] 服务器：{GameServer}");
            if (IsStarwardProtocolRegistered())
            {
                Process.Start(new ProcessStartInfo($"starward://playtime/{GameServer}") { UseShellExecute = true });
                return true;
            }
            else
            {
                TaskControl.Logger.LogWarning("没有检测到 Starward 协议注册，请查看帮助文档！");
                return false;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[GameLoading] Starward记录时间失败");
            return false;
        }
    }

    public string GetGameServerRegistry()
    {
        try
        {
            var cn =
                Registry.GetValue($@"HKEY_CURRENT_USER\Software\miHoYo\HYP\1_1\hk4e_cn", "GameInstallPath",
                    null) as string;
            if (!string.IsNullOrEmpty(cn))
            {
                var filePath = Path.Combine(cn, "YuanShen.exe");
                GameServer = "hk4e_cn";
                return GameServer;
            }

            var global = Registry.GetValue($@"HKEY_CURRENT_USER\Software\Cognosphere\HYP\1_0\hk4e_global",
                "GameInstallPath", null) as string;
            if (!string.IsNullOrEmpty(global))
            {
                var filePath = Path.Combine(global, "GenshinImpact.exe");
                GameServer = "hk4e_global";
                return GameServer;
            }

            var bilibili =
                Registry.GetValue($@"HKEY_CURRENT_USER\Software\miHoYo\HYP\standalone\14_0\hk4e_cn\umfgRO5gh5\hk4e_cn",
                    "GameInstallPath", null) as string;
            if (!string.IsNullOrEmpty(bilibili))
            {
                var filePath = Path.Combine(bilibili, "YuanShen.exe");
                GameServer = "hk4e_bilibili";
                return GameServer;
            }
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogDebug(e, "获取服务器失败");
        }

        return "";
    }

    public bool IsStarwardProtocolRegistered()
    {
        try
        {
            // 打开注册表路径 HKEY_CLASSES_ROOT\starward
            using (RegistryKey key = Registry.ClassesRoot.OpenSubKey("starward"))
            {
                // 如果键存在
                if (key != null)
                {
                    // 检查是否存在 URL Protocol 值
                    object urlProtocol = key.GetValue("URL Protocol");
                    // 如果 URL Protocol 存在且值为空字符串（标准配置），认为协议已注册
                    if (urlProtocol != null && urlProtocol.ToString() == "")
                    {
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // 如果访问注册表时发生错误，记录调试信息
            Debug.WriteLine($"[GameLoading] 检查 Starward 协议时发生错误: {ex.Message}");
        }

        // 如果键不存在或不符合条件，返回 false
        return false;
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
            IsEnabled = false;
            return;
        }

        if (Bv.IsInMainUi(content.CaptureRectArea) || Bv.IsInAnyClosableUi(content.CaptureRectArea))
        {
            IsEnabled = false;
            return;
        }

        // B服判断逻辑
        bool isBili = false;
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
                            isBili = true;
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

        // 官服流程：先识别并点击顶号或切号的后一次“进入游戏”弹窗按钮
        if (!isBili)
        {
            var extraEnterGameBtn = content.CaptureRectArea.Find(_assets.ChooseEnterGameRo);
            if (!extraEnterGameBtn.IsEmpty())
            {
                extraEnterGameBtn.Click();
                return;
            }
        }

        // 官服流程：点击进入游戏按钮（作为外层包装）
        var ra = content.CaptureRectArea.Find(_assets.EnterGameRo);

        if (!ra.IsEmpty())
        {
            TaskContext.Instance().PostMessageSimulator.LeftButtonClickBackground();
            biliLoginClicked = true;
            return;
        }

        // 只有在"进入游戏"按钮未出现时，才进行B服登录处理
        if (isBili && !biliLoginClicked)
        {
            // B服流程：处理登录窗口
            var process = Process.GetProcessesByName("YuanShen").FirstOrDefault();
            var (loginWindow, windowType) = GetBiliLoginWindow(process);

            if (process != null && loginWindow != IntPtr.Zero)
            {
                if (windowType.Contains("协议"))
                {
                    ImageRegion screen;
                    try
                    {
                        screen = CaptureWindowToRectArea(loginWindow);
                    }
                    catch
                    {
                        screen = TaskControl.CaptureToRectArea();
                    }

                    var ocrList = screen.FindMulti(RecognitionObject.OcrThis);
                    var agreeRegion = ocrList.FirstOrDefault(r =>
                        r.Text.Contains("同意") && !r.Text.Contains("不同意"));
                    if (agreeRegion != null)
                    {
                        ClickRegionCenterBy1080(agreeRegion);
                        // 记录协议窗口点击位置
                        var (centerDesktopX, centerDesktopY) =
                            agreeRegion.ConvertPositionToDesktopRegion(agreeRegion.Width / 2,
                                agreeRegion.Height / 2);
                        var captureRect = TaskContext.Instance().SystemInfo.CaptureAreaRect;
                        var inCaptureX = centerDesktopX - captureRect.X;
                        var inCaptureY = centerDesktopY - captureRect.Y;
                        var scale = TaskContext.Instance().SystemInfo.ScaleTo1080PRatio;
                        lastAgreementClickPos = (inCaptureX / scale, inCaptureY / scale);
                        SystemControl.FocusWindow(TaskContext.Instance().GameHandle);
                    }

                    Thread.Sleep(2000);
                }

                if (windowType.Contains("登录"))
                {
                    Thread.Sleep(2000);
                    // 使用协议窗口坐标或默认坐标点击登录
                    if (lastAgreementClickPos.HasValue)
                    {
                        GameCaptureRegion.GameRegion1080PPosClick(lastAgreementClickPos.Value.x1080,
                            lastAgreementClickPos.Value.y1080);
                    }
                    else
                    {
                        GameCaptureRegion.GameRegion1080PPosClick(960, 620);
                    }

                    Thread.Sleep(2000);

                    // 检查窗口是否还存在
                    var (remainingWindow, remainingType) = GetBiliLoginWindow(process);
                    if (remainingWindow == IntPtr.Zero)
                    {
                        biliLoginClicked = true;
                    }
                }
            }

            // 在B服登录过程中，每次循环都检查是否出现"进入游戏"按钮
            ra = content.CaptureRectArea.Find(_assets.EnterGameRo);
            if (!ra.IsEmpty())
            {
                _logger.LogInformation("检测到进入游戏按钮，直接点击");
                TaskContext.Instance().PostMessageSimulator.LeftButtonClickBackground();
                biliLoginClicked = true;
                return;
            }

            Thread.Sleep(1000);


            // 检查是否成功登录
            if (biliLoginClicked)
            {
                _logger.LogInformation("B服登录完成，等待后尝试点击进入游戏");
                Thread.Sleep(5000);
                ClickEnterGameButton();
                return;
            }
        }
        else if (!isBili)
        {
            // 官服流程：直接点击进入游戏按钮
            ClickEnterGameButton();
            return;
        }

        var wmRa = content.CaptureRectArea.Find(_assets.WelkinMoonRo);
        if (!wmRa.IsEmpty())
        {
            TaskContext.Instance().PostMessageSimulator.LeftButtonClickBackground();
            Debug.WriteLine("[GameLoading] Click blessing of the welkin moon");
            // TaskControl.Logger.LogInformation("自动点击月卡");
            return;
        }

        // 原石
        var ysRa = content.CaptureRectArea.Find(ElementAssets.Instance.PrimogemRo);
        if (!ysRa.IsEmpty())
        {
            GameCaptureRegion.GameRegion1080PPosMove(10, 10);
            TaskContext.Instance().PostMessageSimulator.LeftButtonClickBackground();
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

    private void ClickEnterGameButton()
    {
        TaskContext.Instance().PostMessageSimulator.LeftButtonClickBackground();
    }

    private ImageRegion CaptureWindowToRectArea(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            throw new ArgumentException("无效的窗口句柄", nameof(hWnd));
        }

        // BitBlt 方式
        try
        {
            using var bitblt = GameCaptureFactory.Create(CaptureModes.BitBlt);
            bitblt.Start(hWnd, new Dictionary<string, object> { { "autoFixWin11BitBlt", true } });
            var img = GrabOneFrame(bitblt);
            if (img == null) throw new Exception("BitBlt 无帧");
            _logger.LogDebug("BitBlt 捕获窗口成功，尺寸 {W}x{H}", img.Width, img.Height);
            return BuildWindowClientRegion(hWnd, img);
        }
        catch (Exception e)
        {
            _logger.LogWarning("BitBlt 捕获失败: {Msg}", e.Message);
        }

        // DwmSharedSurface方式
        try
        {
            using var dwm = GameCaptureFactory.Create(CaptureModes.DwmGetDxSharedSurface);
            dwm.Start(hWnd);
            var img = GrabOneFrame(dwm);
            if (img == null) throw new Exception("Dwm 无帧");
            _logger.LogDebug("DwmSharedSurface 捕获窗口成功，尺寸 {W}x{H}", img.Width, img.Height);
            return BuildWindowClientRegion(hWnd, img);
        }
        catch (Exception e)
        {
            _logger.LogDebug("DwmSharedSurface 捕获失败，准备回退: {Msg}", e.Message);
        }

        // 全部失败，抛出异常由调用方回退到游戏截图
        throw new Exception("针对句柄的窗口截图失败");
    }

    private ImageRegion BuildWindowClientRegion(IntPtr hWnd, Mat img)
    {
        // 以窗口客户区的屏幕坐标为锚点，构造相对桌面的区域，使 Region.Click 映射到正确位置
        if (!User32.GetClientRect(hWnd, out var clientRect))
        {
            // 回退：用窗口矩形
            User32.GetWindowRect(hWnd, out var wr);
            return TaskContext.Instance().SystemInfo.DesktopRectArea.Derive(img, wr.left, wr.top);
        }

        POINT pt = default;
        User32.ClientToScreen(hWnd, ref pt);
        return TaskContext.Instance().SystemInfo.DesktopRectArea.Derive(img, pt.X, pt.Y);
    }

    private Mat? GrabOneFrame(IGameCapture capture, int retry = 8, int delayMs = 40)
    {
        for (int i = 0; i < retry; i++)
        {
            var img = capture.Capture();
            if (img != null)
            {
                return img;
            }

            Thread.Sleep(delayMs);
        }

        return null;
    }

    /// <summary>
    /// 将 Region 中心点映射到 1080P 坐标系并点击。
    /// </summary>
    private void ClickRegionCenterBy1080(Region region)
    {
        // 计算区域中心的桌面坐标
        var (centerDesktopX, centerDesktopY) =
            region.ConvertPositionToDesktopRegion(region.Width / 2, region.Height / 2);

        // 转换到游戏捕获区域坐标
        var captureRect = TaskContext.Instance().SystemInfo.CaptureAreaRect;
        var inCaptureX = centerDesktopX - captureRect.X;
        var inCaptureY = centerDesktopY - captureRect.Y;
        if (inCaptureX < 0 || inCaptureY < 0)
        {
            // 不在游戏捕获区域内，直接回退为桌面点击
            DesktopRegion.DesktopRegionClick(centerDesktopX, centerDesktopY);
            return;
        }

        // 映射为 1080P 坐标
        var scale = TaskContext.Instance().SystemInfo.ScaleTo1080PRatio;
        var x1080 = inCaptureX / scale;
        var y1080 = inCaptureY / scale;
        GameCaptureRegion.GameRegion1080PPosClick(x1080, y1080);
    }
};
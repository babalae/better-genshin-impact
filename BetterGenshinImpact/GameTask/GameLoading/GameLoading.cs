using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.GameLoading.Assets;
using System;
using System.Diagnostics;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using System.Linq;
using System.Threading;
using System.Text;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.GameLoading;

public class GameLoadingTrigger : ITaskTrigger
{
    public static bool GlobalEnabled = true;
    
    public string Name => "自动开门";

    public bool IsEnabled { get => GlobalEnabled; set {} }

    public int Priority => 999;

    public bool IsExclusive => false;

    public bool IsBiliJudged = false;
    public bool IsBili = false;

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

    public void InnerSetEnabled(bool enabled)
    {
        GlobalEnabled = enabled;
    }

    public void Init()
    {
        if (!_config.AutoEnterGameEnabled)
        {
            InnerSetEnabled(false);
        }

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
            InnerSetEnabled(false);
            return;
        }
        // 成功进入游戏判断    
        if (Bv.IsInMainUi(content.CaptureRectArea) || Bv.IsInAnyClosableUi(content.CaptureRectArea) || Bv.IsInDomain(content.CaptureRectArea))
        {
            _logger.LogInformation("当前在游戏主界面");
            InnerSetEnabled(false);
            return;
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
            var extraEnterGameBtn = content.CaptureRectArea.Find(_assets.ChooseEnterGameRo);
            if (!extraEnterGameBtn.IsEmpty())
            {
                extraEnterGameBtn.Click();
                return;
            }
        }

        // 点击进入游戏按钮
        var ra = content.CaptureRectArea.Find(_assets.EnterGameRo);

        if (!ra.IsEmpty())
        {
            TaskContext.Instance().PostMessageSimulator.LeftButtonClickBackground();
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
                if (windowType.Contains("协议"))
                {
                    GameCaptureRegion.GameRegion1080PPosClick(1030, 615);
                }

                if (windowType.Contains("登录"))
                {
                    Thread.Sleep(2000);
                    GameCaptureRegion.GameRegion1080PPosClick(960, 630);
                    Thread.Sleep(2000);

                    // 检查窗口是否还存在
                    var (remainingWindow, remainingType) = GetBiliLoginWindow(process);
                    if (remainingWindow == IntPtr.Zero)
                    {
                        _logger.LogInformation("B服登录完成，准备进入游戏");
                        // 添加延时确保窗口完全消失
                        Thread.Sleep(2000);
                        // 点击屏幕尝试找回焦点
                        TaskContext.Instance().PostMessageSimulator.LeftButtonClickBackground();
                        biliLoginClicked = true;
                    }
                }
            }
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
};
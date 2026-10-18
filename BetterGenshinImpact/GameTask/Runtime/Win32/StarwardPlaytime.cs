using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.Runtime.Win32;

/// <summary>
/// 通知 Starward 记录游戏时长（首页"使用 Starward 记录游戏时间"）。
/// <para>
/// 记录的是游戏进程的时长，与触发器无关：绑定游戏窗口后调用，同一个游戏进程只通知一次。
/// 调用带上 pid，Starward 直接按进程记录，不再按进程名轮询。
/// 协议说明见 https://github.com/Scighost/Starward/blob/main/docs/UrlProtocol.md
/// </para>
/// </summary>
internal static partial class StarwardPlaytime
{
    /// <summary>
    /// 上一次通知过的游戏进程 ID。任务结束、截图器重启都不会重复通知；游戏重启后进程 ID 变化，会再通知一次
    /// </summary>
    private static int _notifiedProcessId;

    /// <summary>
    /// 在 Win32RuntimeProvider.AttachTo 组装好运行环境之后调用（UI 线程）。启动 Starward 的操作放到线程池执行
    /// </summary>
    public static void TryRecord(IGameWindow window, GenshinStartConfig config)
    {
        if (!config.RecordGameTimeEnabled || !IsProtocolRegistered())
        {
            return;
        }

        var pid = window.ProcessId;
        if (Interlocked.Exchange(ref _notifiedProcessId, pid) == pid)
        {
            return;
        }

        var installPath = config.InstallPath;
        _ = Task.Run(() => Record(pid, installPath));
    }

    private static void Record(int pid, string? installPath)
    {
        try
        {
            var exePath = GetProcessImagePath(pid);
            if (string.IsNullOrEmpty(exePath))
            {
                exePath = installPath;
            }

            var gameBiz = ResolveGameBiz(exePath);
            if (string.IsNullOrEmpty(gameBiz))
            {
                TaskControl.Logger.LogDebug("[Starward] 无法识别游戏区服，不记录游戏时长：{Path}", exePath);
                return;
            }

            Process.Start(new ProcessStartInfo($"starward://playtime/{gameBiz}?pid={pid}") { UseShellExecute = true });
            TaskControl.Logger.LogDebug("[Starward] 已通知记录游戏时长：{Biz}，进程 {Pid}", gameBiz, pid);
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogDebug(e, "[Starward] 通知记录游戏时长失败");
        }
    }

    /// <summary>
    /// GenshinImpact.exe 为国际服；YuanShen.exe 读同目录 config.ini 的 channel（1 官服、14 B 服），
    /// 仍无法判断时读注册表。其他 exe（例如云原神）不记录
    /// </summary>
    private static string? ResolveGameBiz(string? exePath)
    {
        var fileName = Path.GetFileName(exePath);
        if (string.Equals(fileName, "GenshinImpact.exe", StringComparison.OrdinalIgnoreCase))
        {
            return "hk4e_global";
        }

        if (!string.Equals(fileName, "YuanShen.exe", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var channel = ReadChannel(Path.Combine(Path.GetDirectoryName(exePath)!, "config.ini"));
        return channel switch
        {
            "1" => "hk4e_cn",
            "14" => "hk4e_bilibili",
            // 用 Starward 安装的原神不写注册表，所以注册表的优先级低于 config.ini
            _ => GetGameBizFromRegistry()
        };
    }

    private static string? ReadChannel(string iniPath)
    {
        try
        {
            if (!File.Exists(iniPath))
            {
                return null;
            }

            var match = ChannelRegex().Match(File.ReadAllText(iniPath));
            return match.Success ? match.Groups[1].Value : null;
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogDebug(e, "[Starward] 读取 config.ini 失败");
            return null;
        }
    }

    /// <summary>
    /// [General] 段中的 channel=xx
    /// </summary>
    [GeneratedRegex(@"^\s*\[General\]\s*$(?:(?!\[).|\r?\n)*^\s*channel=(\S+)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex ChannelRegex();

    private static string? GetGameBizFromRegistry()
    {
        try
        {
            if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\miHoYo\HYP\1_1\hk4e_cn", "GameInstallPath", null) is string cn
                && !string.IsNullOrEmpty(cn))
            {
                return "hk4e_cn";
            }

            if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\miHoYo\HYP\standalone\14_0\hk4e_cn\umfgRO5gh5\hk4e_cn", "GameInstallPath", null) is string bilibili
                && !string.IsNullOrEmpty(bilibili))
            {
                return "hk4e_bilibili";
            }
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogDebug(e, "[Starward] 从注册表读取游戏区服失败");
        }

        return null;
    }

    /// <summary>
    /// HKEY_CLASSES_ROOT\starward 存在且 URL Protocol 为空字符串（标准配置）时，认为协议已注册
    /// </summary>
    private static bool IsProtocolRegistered()
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey("starward");
            return key?.GetValue("URL Protocol") is string urlProtocol && urlProtocol.Length == 0;
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogDebug(e, "[Starward] 检查协议注册失败");
            return false;
        }
    }

    /// <summary>
    /// 用 PROCESS_QUERY_LIMITED_INFORMATION 读取进程的 exe 完整路径，失败返回 null
    /// </summary>
    private static string? GetProcessImagePath(int pid)
    {
        try
        {
            using var hProcess = Kernel32.OpenProcess(new ACCESS_MASK(Kernel32.ProcessAccess.PROCESS_QUERY_LIMITED_INFORMATION), false, (uint)pid);
            if (hProcess.IsInvalid)
            {
                return null;
            }

            var path = new StringBuilder(1024);
            var size = (uint)path.Capacity;
            return Kernel32.QueryFullProcessImageName(hProcess, 0, path, ref size) ? path.ToString() : null;
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogDebug(e, "[Starward] 读取游戏进程路径失败");
            return null;
        }
    }
}

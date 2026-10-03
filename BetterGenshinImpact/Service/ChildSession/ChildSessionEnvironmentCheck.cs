using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;
using Microsoft.Win32;

namespace BetterGenshinImpact.Service.ChildSession;

/// <summary>
/// 桌面分身的 Windows 环境预检。
///
/// 桌面分身完全依赖 Windows 自身的 RDP / Child Session 能力（WTSEnableChildSessions +
/// RDP ActiveX 的 ConnectToChildSession），BetterGI 只是唤起这个功能。
/// 当系统缺少前置条件时，RDP ActiveX 只会回报「发生内部错误」以及一个无法定位的断开原因，
/// 用户和日志都无法判断真实原因，因此在这里提前收集可读的环境结论。
/// </summary>
internal static class ChildSessionEnvironmentCheck
{
    private const string CurrentVersionRegistryPath =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    private const string PasswordLessRegistryPath =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\PasswordLess\Device";

    private const string TerminalServerRegistryPath =
        @"SYSTEM\CurrentControlSet\Control\Terminal Server";

    /// <summary>
    /// 远程桌面开关的组策略路径。该键存在时会覆盖本机设置。
    /// </summary>
    private const string TerminalServerPolicyRegistryPath =
        @"SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services";

    /// <summary>
    /// DevicePasswordLessBuildVersion 为 2 时，系统仅允许 Windows Hello 登录。
    /// 注意：该注册表值未见于微软官方文档，属于社区逆向得出的结论，
    /// 因此只把 2 判定为「已开启」，其余取值一律视为未开启。
    /// </summary>
    private const int PasswordLessHelloOnlyValue = 2;

    /// <summary>
    /// 家庭版（Core 系列）的 EditionID。
    /// Windows 10/11 上属于家庭版的取值只有这四个，官方文档要求桌面分身运行在非家庭版系统上。
    /// 注意不能依赖 ProductName：Windows 11 上它仍然写着「Windows 10 Home」。
    /// </summary>
    private static readonly HashSet<string> HomeEditionIds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Core",
            "CoreN",
            "CoreSingleLanguage",
            "CoreCountrySpecific",
        };

    /// <summary>问题的严重程度。</summary>
    internal enum Severity
    {
        /// <summary>已知会导致桌面分身无法建立会话。</summary>
        Blocking,

        /// <summary>可能导致登录失败，但不一定阻止会话建立。</summary>
        Warning,
    }

    /// <summary>一条环境检查结论。</summary>
    internal sealed record Issue(Severity Severity, string Title, string Message);

    /// <summary>
    /// 收集当前系统的环境结论。读取失败（键或值不存在、无权限）时跳过该项，不抛异常。
    /// </summary>
    internal static IReadOnlyList<Issue> Collect()
    {
        var issues = new List<Issue>();
        CollectEditionIssue(issues);
        CollectRdpHostIssue(issues);
        CollectPasswordLessIssue(issues);
        return issues;
    }

    /// <summary>是否存在已知会阻止会话建立的问题。</summary>
    internal static bool HasBlockingIssue(IReadOnlyList<Issue> issues)
    {
        foreach (var issue in issues)
        {
            if (issue.Severity == Severity.Blocking)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>把结论格式化成可直接展示给用户的文本。</summary>
    internal static string BuildSummary(IReadOnlyList<Issue> issues)
    {
        if (issues.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var issue in issues)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine().AppendLine();
            }

            builder.Append('【').Append(issue.Title).Append('】').AppendLine();
            builder.Append(issue.Message);
        }

        return builder.ToString();
    }

    private static void CollectEditionIssue(List<Issue> issues)
    {
        var editionId = ReadString(RegistryHive.LocalMachine, CurrentVersionRegistryPath, "EditionID");
        if (string.IsNullOrEmpty(editionId) || !HomeEditionIds.Contains(editionId))
        {
            return;
        }

        issues.Add(new Issue(
            Severity.Blocking,
            "检测到 Windows 家庭版",
            $"当前系统为家庭版（EditionID = {editionId}）。" + Environment.NewLine
            + "桌面分身需要系统能够创建独立的 RDP 会话，官方要求非家庭版系统。"
            + "家庭版通常无法建立该会话，表现为登录界面反复提示凭据无效，或等待一段时间后连接超时。"
            + Environment.NewLine
            + "建议升级到 Windows 专业版，或改用 RDP Wrapper 实现本地远程多用户。"));
    }

    private static void CollectRdpHostIssue(List<Issue> issues)
    {
        // 组策略中的值会覆盖本机设置，因此优先读取组策略。
        var policyValue = ReadInt(
            RegistryHive.LocalMachine,
            TerminalServerPolicyRegistryPath,
            "fDenyTSConnections");
        var localValue = policyValue is null
            ? ReadInt(RegistryHive.LocalMachine, TerminalServerRegistryPath, "fDenyTSConnections")
            : null;
        var denyTsConnections = policyValue ?? localValue;
        if (denyTsConnections != 1)
        {
            return;
        }

        // 组策略来源时，改本机注册表没有意义，必须给出不同的修复路径。
        var suggestion = policyValue is not null
            ? "该值由组策略下发，修改本机注册表会被组策略覆盖。"
              + "请在 gpedit.msc 的「计算机配置 - 管理模板 - Windows 组件 - 远程桌面服务 - "
              + "远程桌面会话主机 - 连接」中启用「允许用户通过使用远程桌面服务进行远程连接」，"
              + "或联系域管理员。"
            : "可以尝试在管理员权限下执行：" + Environment.NewLine
              + "Set-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Terminal Server' "
              + "-Name fDenyTSConnections -Value 0 -Type DWord" + Environment.NewLine
              + "Restart-Service TermService -Force";

        issues.Add(new Issue(
            Severity.Warning,
            "远程桌面主机已关闭",
            $"注册表 fDenyTSConnections = 1（{(policyValue is not null ? "组策略" : "本机设置")}），"
            + "本机 RDP 监听器不会启动，桌面分身可能无法建立会话。" + Environment.NewLine
            + suggestion));
    }

    private static void CollectPasswordLessIssue(List<Issue> issues)
    {
        var passwordLessVersion = ReadInt(
            RegistryHive.LocalMachine,
            PasswordLessRegistryPath,
            "DevicePasswordLessBuildVersion");
        if (passwordLessVersion != PasswordLessHelloOnlyValue)
        {
            return;
        }

        issues.Add(new Issue(
            Severity.Warning,
            "仅允许 Windows Hello 登录",
            "系统已开启「为了提高安全性，仅允许对此设备上的 Microsoft 帐户使用 Windows Hello 登录」，"
            + "此时 Microsoft 帐户在本机没有可用的密码凭据，桌面分身的登录会一直提示凭据无效，"
            + "无论输入什么密码都会失败。" + Environment.NewLine
            + "可在「设置 - 帐户 - 登录选项」中关闭该选项，然后注销并重新登录（部分情况需要重启系统）。"));
    }

    private static string? ReadString(RegistryHive hive, string path, string name)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(path);
            return key?.GetValue(name) as string;
        }
        catch (Exception exception) when (exception is SecurityException
                                              or UnauthorizedAccessException
                                              or IOException)
        {
            return null;
        }
    }

    private static int? ReadInt(RegistryHive hive, string path, string name)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(path);
            return key?.GetValue(name) as int?;
        }
        catch (Exception exception) when (exception is SecurityException
                                              or UnauthorizedAccessException
                                              or IOException)
        {
            return null;
        }
    }
}

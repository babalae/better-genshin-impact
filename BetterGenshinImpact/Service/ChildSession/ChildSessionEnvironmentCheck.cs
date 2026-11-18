using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace BetterGenshinImpact.Service.ChildSession;

/// <summary>
/// 一条检查结论的严重程度。
/// </summary>
public enum ChildSessionIssueSeverity
{
    /// <summary>会阻塞或明显影响桌面分身建立会话。</summary>
    Warning,

    /// <summary>不阻塞启动，但会影响后续使用或让修复失效。</summary>
    Notice,

    /// <summary>未触发。仅用于「全部检查项」视图。</summary>
    Normal
}

/// <summary>
/// 桌面分身的 Windows 环境检查，由用户主动打开诊断面板后执行。
///
/// 桌面分身完全依赖 Windows 自身的 RDP / Child Session 能力（WTSEnableChildSessions +
/// RDP ActiveX 的 ConnectToChildSession），BetterGI 只是唤起这个功能。
/// 当系统缺少前置条件时，RDP ActiveX 只会回报「发生内部错误」以及一个无法定位的断开原因，
/// 用户和日志都无法判断真实原因，因此在用户请求诊断时收集可读的环境结论。
///
/// 除了「能不能建立会话」之外，还覆盖 RDP 破解工具（RDP Wrapper / SuperRDP 等）留下的痕迹：
/// 它们会把 TermService 的 ServiceDll 换成自己的 DLL，卸载不干净时会让桌面分身一直失败；
/// 而且只修 DLL、不清开机任务的话，用户重启后又会坏回去。
/// </summary>
internal static class ChildSessionEnvironmentCheck
{
    /// <summary>
    /// 「仅允许 Windows Hello 登录」设置所在路径。
    /// </summary>
    internal const string PasswordLessRegistryPath =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\PasswordLess\Device";

    /// <summary>
    /// 远程桌面主机的本机设置所在路径。
    /// </summary>
    internal const string TerminalServerRegistryPath =
        @"SYSTEM\CurrentControlSet\Control\Terminal Server";

    /// <summary>
    /// 远程桌面开关的组策略路径。该键存在时会覆盖本机设置。
    /// </summary>
    internal const string TerminalServerPolicyRegistryPath =
        @"SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services";

    /// <summary>
    /// TermService 服务键本身（不是 Parameters 子键）。
    /// </summary>
    internal const string TermServiceKeyPath = @"SYSTEM\CurrentControlSet\Services\TermService";

    /// <summary>
    /// TermService 的 Parameters 子键，ServiceDll 就写在这里。
    /// </summary>
    internal const string TermServiceParametersRegistryPath =
        @"SYSTEM\CurrentControlSet\Services\TermService\Parameters";

    /// <summary>
    /// DevicePasswordLessBuildVersion 为 2 时，系统仅允许 Windows Hello 登录。
    /// 注意：该注册表值未见于微软官方文档，属于社区逆向得出的结论，
    /// 因此只把 2 判定为「已开启」，其余取值一律视为未开启。
    /// </summary>
    internal const int PasswordLessHelloOnlyValue = 2;

    /// <summary>
    /// 破解工具的特征关键词（任务名、任务内容、自启项命令行）。
    ///
    /// 只认「RDP Wrapper Autoupdate」一个名字是不够的：换名版本、SuperRDP、EndpWrap 都存在。
    ///
    /// autoupdate.bat 看着通用，其实是 RDP Wrapper Autoupdate 的专用脚本名——它注册的
    /// 任务就是 <c>cmd.exe /C "...\autoupdate.bat" -log</c>，而改名后的任务名不再含
    /// rdp 字样，只剩内容里的这个文件名可认。检索这个文件名只会命中 RDP 破解相关项目，
    /// 没见过别的软件用它建自动更新任务。即使真有误判，代价也只是别人的更新任务被删，
    /// 程序本身打开时仍会主动更新，不影响使用。
    /// </summary>
    private static readonly string[] ToolKeywords =
    [
        "rdpwrap",
        "rdp wrapper",
        "rdp-wrapper",
        "rdp_wrapper",
        "rdpwrapper",
        "superrdp",
        "termwrap",
        "umwrap",
        "endpwrap",
        "rdpwinst",
        "rdpwupdater",
        "rdp_cnc",
        "rdp-cnc",
        "autoupdate.bat",
        "rdpwrap.ini"
    ];

    /// <summary>
    /// 直接改过 termsrv.dll 的工具会留下的备份文件。
    /// </summary>
    private static readonly string[] TermsrvBackupNames =
    [
        ".termsrv.dll",
        "termsrv.dll.bak",
        "termsrv.dll.orig",
        "termsrv.dll.rdpwrap",
        "termsrv.dll.uninstall_bak",
        "termsrv.dll.old"
    ];

    /// <summary>诊断相关日志统一用这个类别，便于在日志里筛出来。</summary>
    private static readonly ILogger Logger = ChildSessionDiagnosticsLog.Instance;

    /// <summary>ServiceDll 值本身的状态。</summary>
    /// <param name="Exists">值是否存在。</param>
    /// <param name="Kind">值类型。</param>
    /// <param name="Value">原始字符串值（REG_SZ / REG_EXPAND_SZ 才有）。</param>
    /// <param name="Unreadable">读取本身失败（权限 / I/O），无法判断值是否存在。</param>
    internal readonly record struct ServiceDllEntry(
        bool Exists,
        RegistryValueKind Kind,
        string? Value,
        bool Unreadable = false);

    /// <summary>TermService 服务键里与系统默认不符的字段。</summary>
    internal sealed record ServiceKeyDeviation(string Field, string Current, string Expect);

    /// <summary>一处 RDP 破解工具的持久化项。</summary>
    /// <param name="Kind">「task」或「run」。</param>
    /// <param name="Target">显示用：任务全路径，或「注册表位置 → 值名」。</param>
    /// <param name="Reason">命中的原因，给用户看。</param>
    /// <param name="TaskPath">计划任务的全路径（Kind=task）。</param>
    /// <param name="RegistryRoot">注册表根：HKCU / HKU / HKLM（Kind=run）。</param>
    /// <param name="RegistryPath">注册表子键路径（Kind=run；HKU 时含 SID）。</param>
    /// <param name="ValueName">值名（Kind=run）。</param>
    internal sealed record ToolPersistenceHit(
        string Kind,
        string Target,
        string Reason,
        string TaskPath = "",
        string RegistryRoot = "",
        string RegistryPath = "",
        string ValueName = "");

    /// <summary>termsrv.dll 的完整性状态。</summary>
    /// <param name="Exists">文件是否存在。</param>
    /// <param name="Intact">是否与系统组件库一致。仅在 <paramref name="Verified"/> 为 true 时有意义。</param>
    /// <param name="Verified">是否成功完成校验。</param>
    /// <param name="Backups">同目录下疑似备份文件名。</param>
    internal sealed record TermsrvFileState(
        bool Exists,
        bool Intact,
        bool Verified,
        IReadOnlyList<string> Backups);

    /// <summary>一条环境检查结论。</summary>
    /// <param name="Id">稳定的标识，修复动作按它分发。</param>
    /// <param name="Title">标题。</param>
    /// <param name="Summary">一句话结论，直接显示在列表里。</param>
    /// <param name="Detail">展开后的完整说明与手动处理步骤。</param>
    /// <param name="Severity">严重程度。</param>
    /// <param name="CanAutoFix">是否提供一键修复。</param>
    internal sealed record Issue(
        string Id,
        string Title,
        string Summary,
        string Detail,
        ChildSessionIssueSeverity Severity,
        bool CanAutoFix);

    /// <summary>
    /// 收集当前系统需要处理的结论（只包含 Warning / Notice）。
    /// 读取失败（键或值不存在、无权限）时跳过该项，不抛异常。
    /// </summary>
    internal static IReadOnlyList<Issue> Collect()
    {
        var issues = new List<Issue>();
        CollectChildSessionApiIssue(issues);
        CollectTermServiceIssue(issues);
        CollectRdpHostIssue(issues);
        CollectPasswordLessIssue(issues);
        CollectRdpWrapperIssue(issues);
        CollectServiceDllTypeIssue(issues);
        CollectServiceKeyIssue(issues);
        CollectToolPersistenceIssue(issues);
        CollectTermsrvIssue(issues);
        return issues;
    }

    /// <summary>
    /// 收集全部检查项：已触发的原样返回，未触发的补一条 <see cref="ChildSessionIssueSeverity.Normal"/>，
    /// 并附上刚刚读到的实际值，便于区分「这里没问题」和「这里没检查」。
    /// </summary>
    internal static IReadOnlyList<Issue> CollectAll()
    {
        var triggered = Collect().ToDictionary(issue => issue.Id, StringComparer.Ordinal);
        var all = new List<Issue>();

        foreach (var spec in BuildSpecs())
        {
            if (triggered.TryGetValue(spec.Id, out var found))
            {
                all.Add(found);
                continue;
            }

            all.Add(new Issue(
                spec.Id,
                spec.Title,
                $"未触发 · {spec.NormalSummary()}",
                spec.NormalDetail(),
                ChildSessionIssueSeverity.Normal,
                CanAutoFix: false));
        }

        return all;
    }

    /// <summary>
    /// 把结论格式化成可直接展示给用户的文本。
    /// </summary>
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
            builder.Append(issue.Summary);
        }

        return builder.ToString();
    }

    /// <summary>
    /// 检查项清单：标题、默认严重度，以及未触发时展示的当前值。
    /// 修复动作按 Id 分发，所以 Id 一旦发布就不应再改。
    /// </summary>
    private static IReadOnlyList<IssueSpec> BuildSpecs()
    {
        return
        [
            new IssueSpec(
                "child-session-api-missing",
                "系统版本过低",
                () => "系统提供 Child Session 接口",
                () => "检查项：wtsapi32 是否导出 WTSEnableChildSessions（Windows 10 1803 起提供）。"),
            new IssueSpec(
                "termservice-disabled",
                "远程桌面服务已被禁用",
                () => $"启动类型：{QueryTermService().StartType}",
                () => "检查项：TermService 的启动类型是否为「禁用」。"),
            new IssueSpec(
                "termservice-stopped",
                "远程桌面服务未运行",
                () => $"运行状态：{QueryTermService().State}",
                () => "检查项：TermService 是否处于运行状态。"),
            new IssueSpec(
                "rdp-disabled",
                "远程桌面主机已关闭",
                () => $"fDenyTSConnections：{DescribeRdpDenyValue()}",
                () => "检查项：注册表 fDenyTSConnections 是否为 1（关闭远程桌面）。"),
            new IssueSpec(
                "hello-only",
                "仅允许 Windows Hello 登录",
                () => $"DevicePasswordLessBuildVersion：{DescribePasswordLessValue()}",
                () => "检查项：DevicePasswordLessBuildVersion 是否为 2（仅允许 Windows Hello 登录）。"),
            new IssueSpec(
                "rdp-wrapper",
                "远程桌面服务被第三方补丁接管",
                () => $"ServiceDll：{Path.GetFileName(ReadServiceDll() ?? string.Empty)}",
                () => $"检查项：ServiceDll 是否指向原生 termsrv.dll（当前：{ReadServiceDll() ?? "读取失败"}）。"),
            new IssueSpec(
                "termservice-dll-type",
                "ServiceDll 的值已损坏",
                () => $"值类型：{DescribeServiceDllKind()}",
                () => "检查项：ServiceDll 是否存在，且类型为 REG_EXPAND_SZ。"),
            new IssueSpec(
                "termservice-key-broken",
                "服务注册表项被整体覆盖",
                () => $"异常字段：{DescribeServiceKeyDeviationCount()}",
                () => "检查项：ImagePath / ObjectName / 依赖服务是否与系统默认一致。"),
            new IssueSpec(
                "rdp-tool-persistence",
                "存在破解工具的开机自启项",
                () => DescribeToolPersistenceSummary(),
                () => "检查项：计划任务与 Run 自启项中是否留有 RDP 破解工具"
                      + "（计划任务需要管理员权限才能读取）。"),
            new IssueSpec(
                "termsrv-dll-tampered",
                "termsrv.dll 与系统组件库不一致",
                () => DescribeTermsrvState(GetTermsrvState()),
                () => "检查项：termsrv.dll 与系统组件库是否一致（sfc /verifyfile）。")
        ];
    }

    /// <summary>检查项声明。</summary>
    /// <param name="Id">稳定标识。</param>
    /// <param name="Title">标题。</param>
    /// <param name="NormalSummary">未触发时的一句话实际值。</param>
    /// <param name="NormalDetail">未触发时展示的检查内容说明。</param>
    private sealed record IssueSpec(
        string Id,
        string Title,
        Func<string> NormalSummary,
        Func<string> NormalDetail);

    // ---------------- 各项检查 ----------------

    /// <summary>
    /// 检查 Child Session API 是否存在。Windows 10 1803 之前没有这个导出。
    /// </summary>
    private static void CollectChildSessionApiIssue(List<Issue> issues)
    {
        try
        {
            _ = ChildSessionNativeMethods.IsChildSessionsEnabled();
        }
        catch (EntryPointNotFoundException)
        {
            issues.Add(new Issue(
                "child-session-api-missing",
                "系统版本过低",
                "当前系统不支持 Child Session，桌面分身无法使用。",
                "桌面分身依赖 Windows 10 1803 起提供的 Child Session 接口"
                + "（wtsapi32 的 WTSEnableChildSessions）。更早的系统没有这个导出，无法通过任何设置启用。"
                + Environment.NewLine + Environment.NewLine
                + "处理：升级到 Windows 10 1803 或更新版本。",
                ChildSessionIssueSeverity.Warning,
                CanAutoFix: false));
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
                                              or InvalidOperationException)
        {
            // 其他失败交给后续连接流程报错，不在这里拦死。
            Logger.LogWarning(exception, "检查 Child Session API 时出现异常。");
        }
    }

    /// <summary>
    /// 检查 TermService 是否在运行、启动类型是否被禁用。
    /// </summary>
    private static void CollectTermServiceIssue(List<Issue> issues)
    {
        var (state, startType) = QueryTermService();

        if (startType == "Disabled")
        {
            issues.Add(new Issue(
                "termservice-disabled",
                "远程桌面服务已被禁用",
                "TermService 的启动类型是「禁用」，分身无法建立会话。",
                "远程桌面服务（TermService）被设为「禁用」后，系统不会启动它，"
                + "RDP 监听器也就不存在，桌面分身必然连接失败。"
                + Environment.NewLine + Environment.NewLine
                + "处理：把启动类型改回「手动」并启动服务。诊断面板可以直接修复。",
                ChildSessionIssueSeverity.Warning,
                CanAutoFix: true));
            return;
        }

        if (state == "Stopped")
        {
            issues.Add(new Issue(
                "termservice-stopped",
                "远程桌面服务未运行",
                "TermService 处于停止状态，分身无法建立会话。",
                "远程桌面服务（TermService）当前没有运行。它通常是按需启动的，"
                + "但如果启动失败，桌面分身就无法建立会话。"
                + Environment.NewLine + Environment.NewLine
                + "处理：启动服务；若启动后立刻又停下，检查系统日志中 TermService 的错误。",
                ChildSessionIssueSeverity.Warning,
                CanAutoFix: true));
        }
    }

    /// <summary>
    /// 检查远程桌面主机是否被关闭。组策略中的值会覆盖本机设置，因此优先读取组策略。
    /// </summary>
    private static void CollectRdpHostIssue(List<Issue> issues)
    {
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

        var byPolicy = policyValue is not null;
        var detail = byPolicy
            ? "注册表 fDenyTSConnections = 1，且来自组策略。本机 RDP 监听器不会启动，桌面分身可能无法建立会话。"
              + Environment.NewLine + Environment.NewLine
              + "处理：该值由组策略下发，修改本机注册表会被覆盖。请在 gpedit.msc 的"
              + "「计算机配置 - 管理模板 - Windows 组件 - 远程桌面服务 - 远程桌面会话主机 - 连接」中"
              + "启用「允许用户通过使用远程桌面服务进行远程连接」，或联系域管理员。"
            : "注册表 fDenyTSConnections = 1，本机 RDP 监听器不会启动，桌面分身可能无法建立会话。"
              + Environment.NewLine + Environment.NewLine
              + "处理：诊断面板可以直接修复（改回 0 并重启远程桌面服务）。";

        issues.Add(new Issue(
            "rdp-disabled",
            "远程桌面主机已关闭",
            $"注册表 fDenyTSConnections = 1（{(byPolicy ? "组策略" : "本机设置")}）。",
            detail,
            ChildSessionIssueSeverity.Warning,

            // 组策略下发时改本机值没有意义，不给修复按钮。
            CanAutoFix: !byPolicy));
    }

    /// <summary>
    /// 检查是否开启了「仅允许 Windows Hello 登录」，该限制会让密码验证必然失败。
    /// </summary>
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
            "hello-only",
            "仅允许 Windows Hello 登录",
            "系统只允许 Windows Hello 登录，密码凭据不可用。",
            "系统已开启「为了提高安全性，仅允许对此设备上的 Microsoft 帐户使用 Windows Hello 登录」，"
            + "此时 Microsoft 帐户在本机没有可用的密码凭据，桌面分身的登录会一直提示凭据无效，"
            + "无论输入什么密码都会失败。"
            + Environment.NewLine + Environment.NewLine
            + "处理：关闭该选项后注销或重启系统。诊断面板可以直接关闭，改完仍需重新登录。",
            ChildSessionIssueSeverity.Warning,
            CanAutoFix: true));
    }

    /// <summary>
    /// 检查 TermService 是否被第三方补丁劫持。
    /// </summary>
    private static void CollectRdpWrapperIssue(List<Issue> issues)
    {
        var serviceDll = ReadServiceDll();
        if (!IsServiceDllHijacked(serviceDll))
        {
            return;
        }

        issues.Add(new Issue(
            "rdp-wrapper",
            "远程桌面服务被第三方补丁接管",
            $"ServiceDll 指向 {Path.GetFileName(serviceDll ?? string.Empty)}，不是系统原版。",
            $"TermService 的 ServiceDll 当前是 {serviceDll}，不是系统的 termsrv.dll。"
            + "这通常是 RDP Wrapper / SuperRDP 之类的多用户补丁留下的：它们替换了服务加载的模块，"
            + "而桌面分身也依赖同一个服务，两者会互相干扰。"
            + Environment.NewLine + Environment.NewLine
            + "处理：诊断面板可以还原原生模块并清理相关开机任务。"
            + "注意还原后并发远程桌面（同一账户多会话）的能力会失效。",
            ChildSessionIssueSeverity.Warning,
            CanAutoFix: true));
    }

    /// <summary>
    /// ServiceDll 本身写坏了：值缺失，或类型不是 REG_EXPAND_SZ。
    /// </summary>
    private static void CollectServiceDllTypeIssue(List<Issue> issues)
    {
        var entry = GetServiceDllEntry();

        // 读不到就别下结论：既不能说「值缺失」，也不该给一个同样读不到注册表的修复按钮。
        if (entry.Unreadable)
        {
            return;
        }

        // 路径指向第三方 DLL 的情况交给 rdp-wrapper：那边修完同样是 REG_EXPAND_SZ。
        if (entry.Exists && IsServiceDllHijacked(entry.Value))
        {
            return;
        }

        // 空值同样是坏的：类型对、值却是空串时，服务照样不知道该加载哪个模块。
        var emptyValue = entry.Exists && string.IsNullOrWhiteSpace(entry.Value);
        if (entry.Exists && !emptyValue && entry.Kind == RegistryValueKind.ExpandString)
        {
            return;
        }

        var detail = !entry.Exists
            ? "TermService 的 Parameters 子键里没有 ServiceDll 值，服务不知道该加载哪个模块。"
            : emptyValue
                ? "ServiceDll 的值是空字符串。类型虽然是 REG_EXPAND_SZ，但服务加载模块时拿到的是空路径，"
                  + "表现同样是「服务起不来」。"
                : $"ServiceDll 的值是 {entry.Value}，但类型为 {entry.Kind}。"
                  + "写成 REG_SZ 时 %SystemRoot% 不会被展开，服务加载模块时会拿到字面量路径并失败，"
                  + "表现出来只是「服务起不来」，而 DLL 名字看起来完全正常。";

        issues.Add(new Issue(
            "termservice-dll-type",
            "ServiceDll 的值已损坏",
            !entry.Exists
                ? "ServiceDll 值缺失。"
                : emptyValue
                    ? "ServiceDll 的值是空字符串。"
                    : $"ServiceDll 的类型是 {entry.Kind}，应为 REG_EXPAND_SZ。",
            detail + Environment.NewLine + Environment.NewLine
                     + "处理：诊断面板可以写回原生路径与正确类型，然后重启服务。",
            ChildSessionIssueSeverity.Warning,
            CanAutoFix: true));
    }

    /// <summary>
    /// TermService 服务键被整键覆盖：可执行映像、运行身份或依赖服务不对。
    /// </summary>
    private static void CollectServiceKeyIssue(List<Issue> issues)
    {
        var deviations = GetServiceKeyDeviations();
        if (deviations.Count == 0)
        {
            return;
        }

        var builder = new StringBuilder();
        foreach (var deviation in deviations)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append($"· {deviation.Field}：{deviation.Current}（应为 {deviation.Expect}）");
        }

        issues.Add(new Issue(
            "termservice-key-broken",
            "服务注册表项被整体覆盖",
            $"{deviations.Count} 个字段与系统默认不一致。",
            "TermService 的服务键被整体覆盖后，服务可能连可执行映像都找不到。"
            + "这类覆盖常见于用 .reg 文件批量还原的脚本。"
            + Environment.NewLine + Environment.NewLine + builder
            + Environment.NewLine + Environment.NewLine
            + "处理：诊断面板可以恢复默认值（依赖只追加，不会删掉本机已有的依赖）。",
            ChildSessionIssueSeverity.Warning,
            CanAutoFix: true));
    }

    /// <summary>
    /// 发现 RDP 破解工具留下的持久化项（开机任务 / 自启项）。
    /// </summary>
    private static void CollectToolPersistenceIssue(List<Issue> issues)
    {
        var hits = GetToolPersistenceHits();
        if (hits.Count == 0)
        {
            return;
        }

        var builder = new StringBuilder();
        foreach (var hit in hits)
        {
            builder.Append("· ").Append(hit.Target).Append(" — ").AppendLine(hit.Reason);
        }

        issues.Add(new Issue(
            "rdp-tool-persistence",
            "存在破解工具的开机自启项",
            DescribeHitCount(hits.Count),
            "这些任务会在开机时把补丁写回系统。只还原 ServiceDll 而不清理它们，"
            + "用户重启后问题会再次出现，看起来就像「修了没用」。"
            + Environment.NewLine + Environment.NewLine + builder
            + Environment.NewLine + "处理：诊断面板可以导出备份后删除这些项。",
            ChildSessionIssueSeverity.Notice,
            CanAutoFix: true));
    }

    /// <summary>
    /// termsrv.dll 被直接打过补丁，或文件干脆不存在。
    /// </summary>
    private static void CollectTermsrvIssue(List<Issue> issues)
    {
        var state = GetTermsrvState();

        // 未验证（没提权、sfc 跑不起来）时不下结论：宁可漏报，不可误报。
        if (state.Exists && (!state.Verified || state.Intact))
        {
            return;
        }

        var backups = state.Backups.Count == 0
            ? "未发现备份文件"
            : string.Join("、", state.Backups);

        issues.Add(new Issue(
            "termsrv-dll-tampered",
            "termsrv.dll 与系统组件库不一致",
            state.Exists ? "sfc 校验发现该文件被直接修改过。" : "termsrv.dll 不存在。",
            "少数改版不走「替换 ServiceDll」那条路，而是直接给 termsrv.dll 打二进制补丁。"
            + "这种情况下 ServiceDll 指向原生 DLL 也没用，因为原生 DLL 本身已经被改过。"
            + Environment.NewLine + Environment.NewLine
            + $"疑似备份文件：{backups}"
            + Environment.NewLine + Environment.NewLine
            + "判定方式：用 sfc 与该文件的系统组件库副本比对（termsrv.dll 只有目录签名，"
            + "数字签名校验在这里不可靠）。"
            + Environment.NewLine
            + "处理：点「修复」即可——它先用工具留下的备份还原（还原后复核，不通过自动回滚），"
            + "还原不出来时自己继续处理，不需要你干预。"
            + "如果要手动修，以管理员身份运行 sfc /scannow（通常 5~15 分钟），"
            + "完成后重新检测；提示需要重启的话，重启后再检测一次。",
            ChildSessionIssueSeverity.Warning,
            CanAutoFix: true));
    }

    // ---------------- 注册表与系统状态读取 ----------------

    /// <summary>
    /// 读 ServiceDll 值（含类型）。
    /// </summary>
    internal static ServiceDllEntry GetServiceDllEntry()
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(TermServiceParametersRegistryPath);
            if (key is null)
            {
                return new ServiceDllEntry(false, RegistryValueKind.Unknown, null);
            }

            foreach (var name in key.GetValueNames())
            {
                if (!string.Equals(name, "ServiceDll", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return new ServiceDllEntry(
                    true,
                    key.GetValueKind(name),
                    key.GetValue(name) as string);
            }

            return new ServiceDllEntry(false, RegistryValueKind.Unknown, null);
        }
        catch (Exception exception) when (exception is SecurityException
                                              or UnauthorizedAccessException
                                              or IOException)
        {
            // 读不到和「没有这个值」是两件事：前者不能报「值缺失」，
            // 否则非管理员打开面板就会看到一条假问题和一个注定失败的修复。
            return new ServiceDllEntry(
                false, RegistryValueKind.Unknown, null, Unreadable: true);
        }
    }

    /// <summary>
    /// 读 TermService 的 ServiceDll。读不到返回 null。
    /// </summary>
    internal static string? ReadServiceDll() => ReadString(
        RegistryHive.LocalMachine,
        TermServiceParametersRegistryPath,
        "ServiceDll");

    /// <summary>
    /// 原生 termsrv.dll 的完整路径。
    /// </summary>
    internal static string GetNativeServiceDllPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "termsrv.dll");

    /// <summary>
    /// 判断 ServiceDll 是否被第三方 DLL 劫持。
    ///
    /// 不能只匹配 rdpwrap.dll：实际补丁还有 superrdp.dll、RDPWrappers.dll 等，
    /// 不同工具、不同版本用的名字都不一样。正确的判定是「不等于原生 termsrv.dll 就算异常」。
    /// </summary>
    /// <param name="serviceDll">读到的 ServiceDll 值。</param>
    internal static bool IsServiceDllHijacked(string? serviceDll)
    {
        if (string.IsNullOrWhiteSpace(serviceDll))
        {
            return false;
        }

        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(serviceDll);
            return !string.Equals(
                Path.GetFullPath(expanded),
                Path.GetFullPath(GetNativeServiceDllPath()),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException
                                              or NotSupportedException
                                              or PathTooLongException)
        {
            // 路径畸形时按可疑处理，不静默放行。
            return true;
        }
    }

    /// <summary>
    /// 列出 TermService 服务键里与系统默认不符的字段。
    ///
    /// 只查三类真正会让服务起不来的：可执行映像、运行身份、依赖服务。
    /// FailureActions 一律不动：系统默认是存在且带重启动作的，
    /// 早期的一些还原脚本把它删掉，反而让服务崩溃后不再自愈。
    /// </summary>
    internal static IReadOnlyList<ServiceKeyDeviation> GetServiceKeyDeviations()
    {
        var deviations = new List<ServiceKeyDeviation>();

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var serviceKey = baseKey.OpenSubKey(TermServiceKeyPath);
            if (serviceKey is null)
            {
                // 服务键整个不见了，这是更严重的问题，交给「服务未运行」那条结论。
                return deviations;
            }

            var imagePath = serviceKey.GetValue("ImagePath") as string;
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                // 缺值同样算坏：服务没有可执行映像，SCM 连启动都做不到，
                // 而「跳过空值」会让整条检查显示正常。
                deviations.Add(new ServiceKeyDeviation(
                    "ImagePath",
                    "（缺失）",
                    @"%SystemRoot%\System32\svchost.exe -k NetworkService"));
            }
            else if (!LooksLikeSvchostNetworkService(imagePath))
            {
                deviations.Add(new ServiceKeyDeviation(
                    "ImagePath",
                    imagePath,
                    @"%SystemRoot%\System32\svchost.exe -k NetworkService"));
            }

            var objectName = serviceKey.GetValue("ObjectName") as string;
            if (string.IsNullOrWhiteSpace(objectName))
            {
                deviations.Add(new ServiceKeyDeviation(
                    "ObjectName",
                    "（缺失）",
                    @"NT AUTHORITY\NetworkService"));
            }
            else if (!IsNetworkServiceAccount(objectName))
            {
                deviations.Add(new ServiceKeyDeviation(
                    "ObjectName",
                    objectName,
                    @"NT AUTHORITY\NetworkService"));
            }

            var dependencies = serviceKey.GetValue("DependOnService") as string[] ?? [];

            // 依赖集按系统版本分野：Win10 全系列 TermService 只依赖 RPCSS，
            // Win11 起才加上 StateRepository。不能拿「StateRepository 服务键存在」当版本代理——
            // 那个键从 Win10 1507 就存在，会让健康的 Win10 一律被判异常，修复还会写进一条
            // 系统出厂就没有的依赖（本类只加不删，写进去就再也回不去）。
            var expectsStateRepository = OsVersionHelper.IsWindows11_OrGreater;
            var expectedDependencies = expectsStateRepository ? "RPCSS,StateRepository" : "RPCSS";

            if (dependencies.Length == 0)
            {
                // 值被整个删掉也算坏：SCM 不会替服务补回依赖，服务崩了就不再自愈。
                // 「跳过空值」会让整条检查显示「异常字段：无」，还连修复按钮都不给。
                deviations.Add(new ServiceKeyDeviation(
                    "DependOnService",
                    "（缺失）",
                    expectedDependencies));
            }
            else if (!Contains(dependencies, "RPCSS")
                     || (expectsStateRepository && !Contains(dependencies, "StateRepository")))
            {
                deviations.Add(new ServiceKeyDeviation(
                    "DependOnService",
                    string.Join(",", dependencies),
                    expectedDependencies));
            }
        }
        catch (Exception exception) when (exception is SecurityException
                                              or UnauthorizedAccessException
                                              or IOException)
        {
            Logger.LogWarning(exception, "读取 TermService 服务键失败。");
        }

        return deviations;
    }

    /// <summary>
    /// 查询 TermService 的运行状态与启动类型。查询失败返回 ("Unknown", "Unknown")。
    /// </summary>
    internal static (string State, string StartType) QueryTermService()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(TermServiceKeyPath);
            var start = key?.GetValue("Start") as int?;
            var startType = start switch
            {
                2 => "Automatic",
                3 => "Manual",
                4 => "Disabled",
                _ => "Unknown"
            };

            var state = ChildSessionServiceNativeMethods.TryQueryState("TermService", out var currentState)
                ? currentState switch
                {
                    1 => "Stopped",
                    2 => "StartPending",
                    3 => "StopPending",
                    4 => "Running",
                    _ => "Unknown"
                }
                : "Unknown";

            return (state, startType);
        }
        catch (Exception exception) when (exception is SecurityException
                                              or UnauthorizedAccessException
                                              or IOException)
        {
            return ("Unknown", "Unknown");
        }
    }

    // ---------------- 持久化项扫描 ----------------

    private static readonly object ScanGate = new();
    private static DateTime _taskScanAt = DateTime.MinValue;
    private static IReadOnlyList<ToolPersistenceHit> _taskScanCache = [];

    /// <summary>当前缓存是不是深扫的结果（含 Microsoft 子树的任务内容）。</summary>
    private static bool _taskScanDeep;

    /// <summary>
    /// 这次扫描是否有目录被系统拒绝访问。
    ///
    /// 逐目录 try/catch 之后，某个子目录读不到只是跳过，命中列表看起来和「查过、确实没有」
    /// 一模一样。留一个标记，让面板能说清这一趟没扫全。
    /// </summary>
    private static bool _taskScanIncomplete;

    /// <summary>
    /// 一旦确认 termsrv.dll 被篡改，就把「深扫」黏住，直到深扫一次全清干净为止。
    ///
    /// 只按「当前是否被篡改」判断会有两个坑：修复时完整性缓存刚好过期，会降级成浅扫，
    /// 漏掉系统子树里的任务；文件被还原之后条件不再成立，修复时没删掉的任务就再也
    /// 报不出来，用户以为已经清干净了。
    /// </summary>
    private static bool _persistenceDeepScanSticky;

    /// <summary>任务扫描结果缓存 30 秒：面板不能每次刷新都读几百个 XML。</summary>
    private static readonly TimeSpan TaskScanTtl = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 计划任务目录能不能读。
    ///
    /// 非管理员时 <c>%SystemRoot%\System32\Tasks</c> 直接拒绝访问，扫描会静默返回空，
    /// 面板上就成了「命中项：0」——看起来像查过了。这一项必须能把「没查」和「查了没有」
    /// 区分开，只挑一个目录项试读，代价可以忽略。
    /// </summary>
    internal static bool IsTaskScanReadable()
    {
        try
        {
            var tasksRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "Tasks");

            using var entries = Directory.EnumerateFileSystemEntries(tasksRoot).GetEnumerator();
            _ = entries.MoveNext();
            return true;
        }
        catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or SecurityException)
        {
            return false;
        }
    }

    /// <summary>
    /// 上一次计划任务扫描有没有目录被系统拒绝访问。
    ///
    /// 标记跟着扫描结果一起缓存，所以要先取一次命中项（<see cref="GetToolPersistenceHits"/>）
    /// 再读它，否则拿到的可能还是上一轮的状态。
    /// </summary>
    internal static bool IsTaskScanIncomplete()
    {
        lock (ScanGate)
        {
            return _taskScanIncomplete;
        }
    }

    /// <summary>
    /// 扫描 RDP 破解工具留下的持久化项。
    /// </summary>
    internal static IReadOnlyList<ToolPersistenceHit> GetToolPersistenceHits()
    {
        var hits = new List<ToolPersistenceHit>(GetCachedTaskHits(ShouldDeepScanPersistence()));
        hits.AddRange(GetRunKeyHits());
        ReleaseStickyDeepScanIfClean(hits);
        return hits;
    }

    /// <summary>
    /// 是否需要连 Microsoft 子树里的任务一起读内容。
    ///
    /// 判据是 termsrv.dll 被 sfc 判定为「与组件库不一致」：这类工具把补丁写回去的唯一
    /// 手段就是开机任务，文件真被改过时才值得为它多读一千多个系统任务（面板每次打开
    /// 都会跑一遍）。没被改过时深扫只是白等，而破解工具也不会无缘无故藏在系统子树里。
    ///
    /// 检测和修复共用这一个判断：修复前会重新取一次命中项，天然带上同样的深度。
    /// </summary>
    private static bool ShouldDeepScanPersistence()
    {
        var state = GetTermsrvState();
        if (state.Exists && state.Verified && !state.Intact)
        {
            // 确认被篡改：从现在起深扫，直到深扫确认全清。
            _persistenceDeepScanSticky = true;
        }

        return _persistenceDeepScanSticky;
    }

    /// <summary>
    /// 文件已经还原、且深扫一次没再发现任何命中，才允许退回浅扫。
    ///
    /// 只要还留着命中项（例如修复时删不掉的那个任务），就继续深扫——否则提示会随着
    /// 文件被还原一起消失，而任务在下次开机时会把补丁再写回去。
    /// </summary>
    private static void ReleaseStickyDeepScanIfClean(IReadOnlyList<ToolPersistenceHit> hits)
    {
        if (!_persistenceDeepScanSticky || hits.Count > 0)
        {
            return;
        }

        var state = GetTermsrvState();
        if (state.Exists && state.Verified && state.Intact)
        {
            _persistenceDeepScanSticky = false;
        }
    }

    /// <summary>
    /// 清掉扫描缓存。修复动作完成后调用，避免面板还显示旧结论。
    /// </summary>
    internal static void InvalidateScanCache()
    {
        lock (ScanGate)
        {
            _taskScanAt = DateTime.MinValue;
            _taskScanDeep = false;
            _taskScanIncomplete = false;
        }

        InvalidateTermsrvCache();
    }

    /// <summary>
    /// 取计划任务命中（带 30 秒缓存）。
    /// </summary>
    private static IReadOnlyList<ToolPersistenceHit> GetCachedTaskHits(bool deep)
    {
        lock (ScanGate)
        {
            // 浅扫的缓存不能拿去回答深扫：那正是「文件被改过但任务藏在系统子树里」的场景。
            if (DateTime.UtcNow - _taskScanAt < TaskScanTtl && (!deep || _taskScanDeep))
            {
                return _taskScanCache;
            }

            _taskScanCache = ScanScheduledTasks(deep, out var incomplete);
            _taskScanIncomplete = incomplete;
            _taskScanAt = DateTime.UtcNow;
            _taskScanDeep = deep;
            return _taskScanCache;
        }
    }

    /// <summary>
    /// 扫描计划任务。
    ///
    /// 任务定义就是 <c>%SystemRoot%\System32\Tasks</c> 下的 XML 文件，直接读文件比解析
    /// schtasks 的本地化输出可靠。命中规则两条：
    /// 1. 任务名含关键词；
    /// 2. 任务 XML 内容含关键词（覆盖「改名但还指向 autoupdate.bat」的情况）。
    ///
    /// 内容默认只读 Microsoft 子树以外的文件：系统自带任务有一千多个，全套读一遍会明显变慢。
    /// 但 <paramref name="includeMicrosoftSubtree"/> 为 true 时会一并读——只有 termsrv.dll
    /// 被确认篡改时才这么做，那时才值得为「藏在系统子树里的还原任务」付这个代价。
    /// </summary>
    private static IReadOnlyList<ToolPersistenceHit> ScanScheduledTasks(
        bool includeMicrosoftSubtree,
        out bool incomplete)
    {
        var hits = new List<ToolPersistenceHit>();
        incomplete = false;

        var tasksRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "Tasks");

        if (!Directory.Exists(tasksRoot))
        {
            return hits;
        }

        // 逐目录遍历，不用 EnumerateFiles(AllDirectories)：后者在第一个无权限的
        // 子目录上就整体抛出，后面的任务全部漏掉，而且外面只看到一句「扫描失败」。
        var pending = new Stack<string>();
        pending.Push(tasksRoot);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            string[] files;
            string[] subdirectories;
            try
            {
                files = Directory.GetFiles(directory);
                subdirectories = Directory.GetDirectories(directory);
            }
            catch (Exception exception) when (exception is IOException
                                                  or UnauthorizedAccessException
                                                  or SecurityException)
            {
                // 跳过这个目录，但要把「没扫全」记下来：否则结果和「扫过、确实没有」一样。
                incomplete = true;
                Logger.LogDebug(exception, "跳过无法读取的任务目录 {Directory}。", directory);
                continue;
            }

            foreach (var subdirectory in subdirectories)
            {
                pending.Push(subdirectory);
            }

            foreach (var file in files)
            {
                var relative = Path.GetRelativePath(tasksRoot, file);
                var taskName = Path.GetFileName(file);
                var taskPath = @"\" + relative.Replace('/', '\\');

                if (ContainsKeyword(taskName))
                {
                    hits.Add(new ToolPersistenceHit(
                        "task",
                        taskPath,
                        "任务名包含破解工具特征",
                        TaskPath: taskPath));
                    continue;
                }

                if (!includeMicrosoftSubtree
                    && relative.StartsWith(
                        "Microsoft" + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var content = TryReadTextFile(file);
                if (content is null || !ContainsKeyword(content))
                {
                    continue;
                }

                hits.Add(new ToolPersistenceHit(
                    "task",
                    taskPath,
                    "任务内容指向破解工具",
                    TaskPath: taskPath));
            }
        }

        return hits;
    }

    /// <summary>
    /// 扫描自启项（Run / RunOnce / 策略 Run，含其它已加载用户的 HKCU）。
    /// </summary>
    private static IReadOnlyList<ToolPersistenceHit> GetRunKeyHits()
    {
        var hits = new List<ToolPersistenceHit>();

        var locations = new List<(RegistryKey Root, string Path, string Label)>
        {
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKLM"),
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", "HKLM"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "HKLM"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce", "HKLM"),
            (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKCU"),
            (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", "HKCU"),
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run", "HKLM"),
            (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run", "HKCU")
        };

        // 其它用户：值写在 HKU\<SID>\...\Run 里时，当前用户的 HKCU 看不到。
        try
        {
            foreach (var sid in Registry.Users.GetSubKeyNames())
            {
                if (!sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var suffix in new[] { @"\Run", @"\RunOnce" })
                {
                    locations.Add((
                        Registry.Users,
                        sid + @"\SOFTWARE\Microsoft\Windows\CurrentVersion" + suffix,
                        "HKU\\" + sid));
                }
            }
        }
        catch (Exception exception) when (exception is SecurityException
                                              or UnauthorizedAccessException
                                              or IOException)
        {
            // 枚举 HKU 需要权限，拿不到就跳过，不影响主流程。
            Logger.LogDebug(exception, "枚举其它用户的自启项失败。");
        }

        foreach (var (root, path, label) in locations)
        {
            try
            {
                using var key = root.OpenSubKey(path);
                if (key is null)
                {
                    continue;
                }

                foreach (var name in key.GetValueNames())
                {
                    if (key.GetValue(name) is not string data || string.IsNullOrWhiteSpace(data))
                    {
                        continue;
                    }

                    if (!ContainsKeyword(name) && !ContainsKeyword(data))
                    {
                        continue;
                    }

                    hits.Add(new ToolPersistenceHit(
                        "run",
                        $"{label}\\{path} → {name}",
                        data,
                        RegistryRoot: root == Registry.CurrentUser ? "HKCU"
                            : root == Registry.Users ? "HKU"
                            : "HKLM",
                        RegistryPath: path,
                        ValueName: name));
                }
            }
            catch (Exception exception) when (exception is SecurityException
                                                  or UnauthorizedAccessException
                                                  or IOException)
            {
                // 单个位置读不到不影响其它位置。
                Logger.LogDebug(exception, $"读取自启项 {label}\\{path} 失败。");
            }
        }

        return hits;
    }

    // ---------------- termsrv.dll 完整性 ----------------

    private static readonly object TermsrvGate = new();

    /// <summary>
    /// sfc 同一时刻只跑一个进程（校验 /verifyfile 与修复 /scanfile 共用）。
    ///
    /// 修复完成后的同步复核很容易和后台校验撞上：两个进程读同一个文件，只是白烧
    /// CPU 和磁盘，而且慢的那个还会拖住界面上的「检测中」。
    /// 修复侧也要过这把闸：两个 sfc 同时改同一个文件，CBS 会互相打架。
    /// </summary>
    internal static readonly SemaphoreSlim SfcGate = new(1, 1);

    private static TermsrvFileState _termsrvState = new(false, false, false, []);
    private static DateTime _termsrvStateAt = DateTime.MinValue;
    private static bool _termsrvVerifyRunning;

    /// <summary>
    /// 校验世代号。修复会同步重新校验并让旧结果作废，
    /// 后台任务完成时如果世代号已经变了，就把自己的结果丢掉——
    /// 否则「修复前开始、修复后才结束」的那次校验会把旧结论覆盖回去，
    /// 面板会连续五分钟报一个已经被修好的问题。
    /// </summary>
    private static int _termsrvTaskId;

    /// <summary>已验证结果缓存 5 分钟；未验证的 30 秒后重试。</summary>
    private static readonly TimeSpan TermsrvVerifiedTtl = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan TermsrvUnverifiedTtl = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 是否正在校验 termsrv.dll。界面靠它决定「检测中」什么时候结束。
    /// </summary>
    internal static bool IsTermsrvVerifying
    {
        get
        {
            lock (TermsrvGate)
            {
                return _termsrvVerifyRunning;
            }
        }
    }

    /// <summary>
    /// 取 termsrv.dll 的状态。第一次调用会返回「未验证」并在后台开始校验，
    /// 面板下一次刷新就能拿到结果。
    /// </summary>
    internal static TermsrvFileState GetTermsrvState()
    {
        var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var termsrvPath = Path.Combine(system32, "termsrv.dll");

        lock (TermsrvGate)
        {
            var ttl = _termsrvState.Verified ? TermsrvVerifiedTtl : TermsrvUnverifiedTtl;
            var stale = DateTime.UtcNow - _termsrvStateAt >= ttl;

            if (stale && !_termsrvVerifyRunning)
            {
                _termsrvVerifyRunning = true;
                var pendingBackups = CollectTermsrvBackups(system32);
                var taskId = ++_termsrvTaskId;

                _ = Task.Run(() =>
                {
                    TermsrvFileState result;
                    try
                    {
                        result = VerifyTermsrv(termsrvPath, pendingBackups);
                    }
                    catch (Exception exception) when (exception is IOException
                                                          or UnauthorizedAccessException
                                                          or SecurityException
                                                          or InvalidOperationException
                                                          or System.ComponentModel.Win32Exception)
                    {
                        Logger.LogWarning(exception, "校验 termsrv.dll 失败。");
                        result = new TermsrvFileState(File.Exists(termsrvPath), false, false, pendingBackups);
                    }

                    lock (TermsrvGate)
                    {
                        if (taskId != _termsrvTaskId)
                        {
                            // 这轮已经被更新的校验取代（修复后的复核）：结果作废，
                            // 也不能动「正在校验」标志——那个标志归最新的那轮负责。
                            Logger.LogDebug("丢弃过期的 termsrv.dll 校验结果。");
                            return;
                        }

                        _termsrvState = result;
                        _termsrvStateAt = DateTime.UtcNow;
                        _termsrvVerifyRunning = false;
                    }
                });
            }

            if (_termsrvVerifyRunning)
            {
                // 正在校验（首次校验或旧结论过期后的重新校验）：手上这份结论已经不是
                // 当前系统的事实了，不能当成新结论交出去——否则界面会先亮一条旧问题，
                // 等校验完成再撤掉，用户看到的就是一次假警报。
                return new TermsrvFileState(File.Exists(termsrvPath), false, false, CollectTermsrvBackups(system32));
            }

            if (_termsrvState.Exists || _termsrvState.Verified)
            {
                return _termsrvState;
            }

            // 还没跑过：先给出「文件在不在」这个廉价结论。
            // 这里必须真去查一次文件，不能写死 false —— 否则第一次打开面板会把
            // 「正在后台校验」误报成「termsrv.dll 不存在」。
            return new TermsrvFileState(File.Exists(termsrvPath), false, false, CollectTermsrvBackups(system32));
        }
    }

    /// <summary>
    /// 立即重新校验一次（修复完成后用），同步等待结果。
    /// </summary>
    internal static TermsrvFileState VerifyTermsrvNow()
    {
        var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var termsrvPath = Path.Combine(system32, "termsrv.dll");
        var backups = CollectTermsrvBackups(system32);

        var result = VerifyTermsrv(termsrvPath, backups);

        lock (TermsrvGate)
        {
            // 这次是权威结论：作废在此之前启动的所有后台校验。
            _termsrvTaskId++;
            _termsrvState = result;
            _termsrvStateAt = DateTime.UtcNow;
            _termsrvVerifyRunning = false;
        }

        return result;
    }

    /// <summary>
    /// 清掉缓存，让下一次读取重新校验。
    /// </summary>
    private static void InvalidateTermsrvCache()
    {
        lock (TermsrvGate)
        {
            _termsrvStateAt = DateTime.MinValue;

            // 作废正在跑的那轮：它的结论可能是修复前的，完成后不许再写回来。
            // 同时放下标志，让下一次读取能立刻发起新的校验。
            _termsrvTaskId++;
            _termsrvVerifyRunning = false;
        }
    }

    /// <summary>
    /// 用 sfc 校验 termsrv.dll：该文件只有目录签名，数字签名校验不可靠。
    /// </summary>
    private static TermsrvFileState VerifyTermsrv(string termsrvPath, IReadOnlyList<string> backups)
    {
        if (!File.Exists(termsrvPath))
        {
            return new TermsrvFileState(false, false, false, backups);
        }

        if (!IsElevated())
        {
            // sfc 非提权必然失败并返回 1，当成「被改过」会误报。
            return new TermsrvFileState(true, false, false, backups);
        }

        var exitCode = RunSfcVerifyFile(termsrvPath);
        return exitCode switch
        {
            0 => new TermsrvFileState(true, true, true, backups),
            1 => new TermsrvFileState(true, false, true, backups),
            _ => new TermsrvFileState(true, false, false, backups)
        };
    }

    /// <summary>
    /// 列出同目录下疑似备份文件。
    /// </summary>
    private static IReadOnlyList<string> CollectTermsrvBackups(string system32)
    {
        var backups = new List<string>();
        foreach (var name in TermsrvBackupNames)
        {
            if (File.Exists(Path.Combine(system32, name)))
            {
                backups.Add(name);
            }
        }

        return backups;
    }

    /// <summary>
    /// 运行 <c>sfc /verifyfile=</c>，返回退出码；跑不起来返回 -1。
    /// </summary>
    private static int RunSfcVerifyFile(string path)
    {
        // 排队等前一个跑完，而不是同时起两个。等不到（前一个卡住）就照跑：
        // 校验本身不该因为排队而失败，多一个进程也比拿不到结论强。
        var acquired = false;
        try
        {
            acquired = SfcGate.Wait(TimeSpan.FromSeconds(20));
            if (!acquired)
            {
                Logger.LogDebug("等待上一次 sfc 校验超时，直接开始本次校验。");
            }

            return RunSfcVerifyFileCore(path);
        }
        finally
        {
            if (acquired)
            {
                SfcGate.Release();
            }
        }
    }

    private static int RunSfcVerifyFileCore(string path)
    {
        try
        {
            var sfc = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "sfc.exe");

            if (!File.Exists(sfc))
            {
                return -1;
            }

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = sfc,
                Arguments = "/verifyfile=" + path,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process is null)
            {
                return -1;
            }

            // 输出是 UTF-16/本地化文本，不值得解析；只要它跑完，看退出码即可。
            _ = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(TimeSpan.FromMinutes(3)))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception exception) when (exception is InvalidOperationException
                                                      or System.ComponentModel.Win32Exception)
                {
                    // 进程刚好退出。
                }

                return -1;
            }

            return process.ExitCode;
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                              or System.ComponentModel.Win32Exception
                                              or IOException)
        {
            Logger.LogWarning(exception, "运行 sfc /verifyfile 失败。");
            return -1;
        }
    }

    /// <summary>
    /// 当前进程是否以管理员身份运行。
    /// </summary>
    internal static bool IsElevated()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch (Exception exception) when (exception is SecurityException
                                              or UnauthorizedAccessException
                                              or PlatformNotSupportedException)
        {
            return false;
        }
    }

    // ---------------- 通用工具 ----------------

    /// <summary>
    /// 读注册表 DWORD 值。键或值不存在、类型不符、无权限时返回 null。
    /// </summary>
    internal static int? ReadInt(RegistryHive hive, string path, string name)
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

    /// <summary>
    /// 读注册表字符串值。键或值不存在、类型不符、无权限时返回 null。
    /// </summary>
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

    private static bool ContainsKeyword(string text)
    {
        foreach (var keyword in ToolKeywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? TryReadTextFile(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0)
            {
                return string.Empty;
            }

            // 任务 XML 通常是 UTF-16 LE（带 BOM）
            if (bytes.Length > 1 && (bytes[0] == 0xFF && bytes[1] == 0xFE
                                     || bytes[0] == 0x3C && bytes[1] == 0x00))
            {
                return Encoding.Unicode.GetString(bytes);
            }

            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or SecurityException)
        {
            return null;
        }
    }

    /// <summary>
    /// 判断 ImagePath 是否还是系统默认的 <c>svchost.exe -k NetworkService</c>。
    ///
    /// 不能只查子串：<c>C:\Missing\svchost.exe -k NotNetworkService</c> 同时包含
    /// "svchost.exe"、"-k" 和 "NetworkService"，子串匹配会把它当成正常。这里拆成
    /// 「可执行文件」和「参数」两部分，要求文件名正确、文件存在、<c>-k</c> 后面
    /// 紧跟的就是 NetworkService。
    /// </summary>
    private static bool LooksLikeSvchostNetworkService(string imagePath)
    {
        var expanded = Environment.ExpandEnvironmentVariables(imagePath).Trim();
        if (expanded.Length == 0)
        {
            return false;
        }

        var firstSpace = expanded.IndexOf(' ');
        var executable = (firstSpace < 0 ? expanded : expanded[..firstSpace]).Trim('"');
        var arguments = firstSpace < 0 ? string.Empty : expanded[(firstSpace + 1)..];

        if (!string.Equals(Path.GetFileName(executable), "svchost.exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!File.Exists(executable))
        {
            return false;
        }

        var tokens = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < tokens.Length; index++)
        {
            // 两种写法都认：「-k NetworkService」和「-kNetworkService」。
            if (tokens[index].Equals("-k", StringComparison.OrdinalIgnoreCase)
                && index + 1 < tokens.Length
                && tokens[index + 1].Equals("NetworkService", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (tokens[index].Equals("-kNetworkService", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// ObjectName 是否为系统默认的 NetworkService。
    ///
    /// 用完整名称比较：子串匹配会放过 <c>NT AUTHORITY\NotNetworkService</c>，
    /// 而那个账户根本不存在，服务照样起不来。
    /// </summary>
    private static bool IsNetworkServiceAccount(string objectName)
    {
        var trimmed = objectName.Trim();

        return string.Equals(trimmed, @"NT AUTHORITY\NetworkService", StringComparison.OrdinalIgnoreCase)
               || string.Equals(trimmed, "NetworkService", StringComparison.OrdinalIgnoreCase);
    }

    private static bool Contains(string[] values, string expected)
    {
        foreach (var value in values)
        {
            if (string.Equals(value, expected, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // ---------------- 「未触发」时展示的当前值 ----------------

    private static string DescribeRdpDenyValue()
    {
        var policyValue = ReadInt(
            RegistryHive.LocalMachine,
            TerminalServerPolicyRegistryPath,
            "fDenyTSConnections");
        var localValue = ReadInt(
            RegistryHive.LocalMachine,
            TerminalServerRegistryPath,
            "fDenyTSConnections");
        return policyValue is not null
            ? $"{policyValue}（组策略）"
            : localValue?.ToString() ?? "未设置";
    }

    private static string DescribePasswordLessValue()
    {
        return ReadInt(
            RegistryHive.LocalMachine,
            PasswordLessRegistryPath,
            "DevicePasswordLessBuildVersion")?.ToString() ?? "未设置";
    }

    private static string DescribeServiceDllKind()
    {
        var entry = GetServiceDllEntry();
        return entry.Exists ? entry.Kind.ToString() : "值不存在";
    }

    private static string DescribeServiceKeyDeviationCount()
    {
        var count = GetServiceKeyDeviations().Count;
        return count == 0 ? "无" : $"{count} 个字段异常";
    }

    /// <summary>
    /// 已触发时的结论：命中几个，以及这一趟扫全了没有。
    ///
    /// 有命中同样要说「扫描不完整」——否则清理掉这几项之后，没扫到的那些永远不出现，
    /// 用户会以为已经清干净了。
    /// </summary>
    private static string DescribeHitCount(int count)
    {
        var summary = $"{count} 个开机任务或自启项指向 RDP 破解工具。";
        return IsTaskScanIncomplete()
            ? summary + "部分路径系统拒绝访问，扫描不完整。"
            : summary;
    }

    /// <summary>
    /// 「全部检查项」里持久化项的实际值。
    ///
    /// 三种状态要分开：读不到目录（没提权）、扫了但有目录被拒（不完整）、扫全了没有。
    /// 前两种都不等于「干净」，不能用「命中项：0」含糊过去。
    /// </summary>
    private static string DescribeToolPersistenceSummary()
    {
        if (!IsTaskScanReadable())
        {
            return "计划任务未读取（需要管理员权限）";
        }

        var count = GetToolPersistenceHits().Count;
        return IsTaskScanIncomplete()
            ? $"命中项：{count}；部分路径系统拒绝访问，扫描不完整"
            : $"命中项：{count}";
    }

    private static string DescribeTermsrvState(TermsrvFileState state)
    {
        if (!state.Exists)
        {
            return "文件不存在";
        }

        if (!state.Verified)
        {
            return "暂时无法验证（需要管理员权限，或正在等待 sfc）";
        }

        return state.Intact ? "与系统组件库一致" : "与系统组件库不一致";
    }
}

/// <summary>
/// 诊断相关日志的类型标记：让这一组检查的日志在日志文件里有一个独立分类。
///
/// 日志器带兜底：预检也会在单元测试与启动失败路径上被调用，那里没有宿主容器，
/// 拿不到日志器就退回空实现，不能因为「记不了日志」把检查本身弄挂。
/// </summary>
internal sealed class ChildSessionDiagnosticsLog
{
    private static readonly ILogger Cached = Resolve();

    /// <summary>供检查与修复使用的日志器。</summary>
    internal static ILogger Instance => Cached;

    private static ILogger Resolve()
    {
        try
        {
            // 单元测试与命令行场景没有 WPF 应用实例，这时不需要日志器，
            // 也不能去碰 App —— 它的静态初始化依赖完整的 GUI 宿主。
            if (System.Windows.Application.Current is null)
            {
                return NullLogger.Instance;
            }

            return App.GetLogger<ChildSessionDiagnosticsLog>();
        }
        catch (Exception)
        {
            return NullLogger.Instance;
        }
    }
}

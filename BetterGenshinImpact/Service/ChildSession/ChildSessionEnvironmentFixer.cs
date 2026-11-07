using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace BetterGenshinImpact.Service.ChildSession;

/// <summary>
/// 一次修复的结果。
/// </summary>
/// <param name="Success">是否修复成功。</param>
/// <param name="Message">给用户看的一句话说明。</param>
internal readonly record struct ChildSessionFixResult(bool Success, string Message);

/// <summary>
/// 环境问题的自动修复。
///
/// 与 <see cref="ChildSessionEnvironmentCheck"/> 分开：那边只读不改，这边只改不判断，
/// 这样「看」和「改」可以各自被审查与测试。
///
/// 四条硬约束：
/// 1. 改前备份、改后回读——写入成功不等于生效；
/// 2. 重启服务前确认没有正在使用的分身会话，避免把用户正在用的画面打断；
/// 3. 依赖服务只追加不覆盖（整键还原的脚本就是在这里把 Win11 的依赖冲掉的）；
/// 4. 能自动修的要真的修完：涉及文件替换的，修完必须用同一套校验重新验证。
/// </summary>
internal static class ChildSessionEnvironmentFixer
{
    private static readonly ILogger Logger = ChildSessionDiagnosticsLog.Instance;

    /// <summary>
    /// 按检查项标识执行修复。
    /// </summary>
    /// <param name="issueId">检查项的稳定标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    internal static ChildSessionFixResult Fix(string issueId, CancellationToken cancellationToken = default)
    {
        switch (issueId)
        {
            case "termservice-stopped":
                return FixTermServiceStopped();
            case "termservice-disabled":
                return FixTermServiceDisabled();
            case "rdp-disabled":
                return FixRdpDisabled(cancellationToken);
            case "hello-only":
                return FixHelloOnly();
            case "rdp-wrapper":
                return FixServiceDll(cancellationToken);
            case "termservice-dll-type":
                return FixServiceDll(cancellationToken);
            case "termservice-key-broken":
                return FixServiceKey(cancellationToken);
            case "rdp-tool-persistence":
                return FixToolPersistence();
            case "termsrv-dll-tampered":
                return FixTermsrvDll(cancellationToken);
            default:
                return new ChildSessionFixResult(false, "这个问题没有提供自动修复，请按展开后的说明手动处理。");
        }
    }

    /// <summary>
    /// 启动 TermService。
    /// </summary>
    private static ChildSessionFixResult FixTermServiceStopped()
    {
        return ChildSessionServiceNativeMethods.TryStart("TermService", out var error)
            ? new ChildSessionFixResult(true, "远程桌面服务已启动。")
            : new ChildSessionFixResult(false, error);
    }

    /// <summary>
    /// 把被禁用的 TermService 改回「手动」并启动。
    /// </summary>
    private static ChildSessionFixResult FixTermServiceDisabled()
    {
        if (!ChildSessionServiceNativeMethods.TrySetStartTypeToManual("TermService", out var configError))
        {
            return new ChildSessionFixResult(false, configError);
        }

        if (!ChildSessionServiceNativeMethods.TryStart("TermService", out var startError))
        {
            return new ChildSessionFixResult(false, $"启动类型已改为「手动」，但{startError}");
        }

        return new ChildSessionFixResult(true, "TermService 启动类型已改为「手动」并已启动。");
    }

    /// <summary>
    /// 打开远程桌面主机：fDenyTSConnections 改 0，然后重启 TermService 让监听器生效。
    /// </summary>
    private static ChildSessionFixResult FixRdpDisabled(CancellationToken cancellationToken)
    {
        // 组策略下发时改本机值没有意义，直接拒绝，避免造成「改过了但没用」的错觉。
        var policyValue = ChildSessionEnvironmentCheck.ReadInt(
            RegistryHive.LocalMachine,
            ChildSessionEnvironmentCheck.TerminalServerPolicyRegistryPath,
            "fDenyTSConnections");
        if (policyValue is not null)
        {
            return new ChildSessionFixResult(
                false,
                "该设置由组策略下发，改本机注册表会被覆盖。请改用 gpedit.msc 或联系管理员。");
        }

        if (!TryWriteDword(
                ChildSessionEnvironmentCheck.TerminalServerRegistryPath,
                "fDenyTSConnections",
                0,
                out var writeError))
        {
            return new ChildSessionFixResult(false, writeError);
        }

        // 回读确认：写入成功不等于生效。
        var after = ChildSessionEnvironmentCheck.ReadInt(
            RegistryHive.LocalMachine,
            ChildSessionEnvironmentCheck.TerminalServerRegistryPath,
            "fDenyTSConnections");
        if (after != 0)
        {
            return new ChildSessionFixResult(false, "注册表写入后回读不是 0，修复未生效。");
        }

        var restart = RestartTermService(cancellationToken);
        return restart.Success
            ? new ChildSessionFixResult(true, "已开启远程桌面并重启服务。如果正在使用分身，需要重新点一次「启动」。")
            : new ChildSessionFixResult(false, $"注册表已改回 0，但{restart.Message}");
    }

    /// <summary>
    /// 关闭「仅允许 Windows Hello 登录」。改完需要用户自己注销或重启才生效。
    /// </summary>
    private static ChildSessionFixResult FixHelloOnly()
    {
        if (!TryWriteDword(
                ChildSessionEnvironmentCheck.PasswordLessRegistryPath,
                "DevicePasswordLessBuildVersion",
                0,
                out var error))
        {
            return new ChildSessionFixResult(false, error);
        }

        var after = ChildSessionEnvironmentCheck.ReadInt(
            RegistryHive.LocalMachine,
            ChildSessionEnvironmentCheck.PasswordLessRegistryPath,
            "DevicePasswordLessBuildVersion");
        if (after == ChildSessionEnvironmentCheck.PasswordLessHelloOnlyValue)
        {
            return new ChildSessionFixResult(false, "注册表写入后回读仍是 2，修复未生效。");
        }

        return new ChildSessionFixResult(
            true,
            "已关闭「仅允许 Windows Hello 登录」。必须注销或重启才会生效——"
            + "凭据提供程序在登录时就已经确定，不重新登录不会刷新。");
    }

    /// <summary>
    /// 把 ServiceDll 写回「原生路径 + REG_EXPAND_SZ」，并清理会让它被改回去的开机任务。
    ///
    /// 同时覆盖两种问题：路径被换成第三方 DLL，以及值本身缺失/类型写错。
    /// 两者的修法相同，差别只在说明文案。
    /// </summary>
    private static ChildSessionFixResult FixServiceDll(CancellationToken cancellationToken)
    {
        var entry = ChildSessionEnvironmentCheck.GetServiceDllEntry();
        var hijacked = entry.Exists && ChildSessionEnvironmentCheck.IsServiceDllHijacked(entry.Value);
        var typeBroken = !entry.Exists || entry.Kind != RegistryValueKind.ExpandString;

        if (!hijacked && !typeBroken)
        {
            return new ChildSessionFixResult(true, "ServiceDll 已指向原生模块且类型正确，无需修复。");
        }

        var backupPath = WriteServiceDllBackup(entry);

        if (!TryWriteString(
                ChildSessionEnvironmentCheck.TermServiceParametersRegistryPath,
                "ServiceDll",
                ChildSessionEnvironmentCheck.GetNativeServiceDllPath(),
                RegistryValueKind.ExpandString,
                out var writeError))
        {
            return new ChildSessionFixResult(false, writeError);
        }

        var after = ChildSessionEnvironmentCheck.GetServiceDllEntry();
        if (!after.Exists || after.Kind != RegistryValueKind.ExpandString
            || ChildSessionEnvironmentCheck.IsServiceDllHijacked(after.Value))
        {
            return new ChildSessionFixResult(false, "注册表写入后回读仍是旧值，修复未生效。");
        }

        // 先断持久化再重启服务：这类工具的开机任务会在下次启动时把 DLL 写回去，
        // 不清理它们，这次修复看起来成功了，用户重启后又是坏的。
        var persistence = FixToolPersistence();
        var restart = RestartTermService(cancellationToken);

        if (!restart.Success)
        {
            return new ChildSessionFixResult(false, $"ServiceDll 已还原，但{restart.Message}");
        }

        var message = "已还原为原生 termsrv.dll 并重启远程桌面服务。"
                      + "注意：多用户并发远程桌面的能力已随之失效。";
        message += " " + persistence.Message;
        if (backupPath is not null)
        {
            message += $"原值已备份到 {backupPath}。";
        }

        return new ChildSessionFixResult(true, message);
    }

    /// <summary>
    /// 恢复被整键覆盖的 TermService 服务键字段。
    /// </summary>
    private static ChildSessionFixResult FixServiceKey(CancellationToken cancellationToken)
    {
        var deviations = ChildSessionEnvironmentCheck.GetServiceKeyDeviations();
        if (deviations.Count == 0)
        {
            return new ChildSessionFixResult(true, "服务键字段都正常，无需修复。");
        }

        var backupPath = WriteServiceKeyBackup(deviations);
        var failures = new List<string>();

        foreach (var deviation in deviations)
        {
            var ok = deviation.Field switch
            {
                "ImagePath" => TryWriteString(
                    ChildSessionEnvironmentCheck.TermServiceKeyPath,
                    "ImagePath",
                    @"%SystemRoot%\System32\svchost.exe -k NetworkService",
                    RegistryValueKind.ExpandString,
                    out _),

                "ObjectName" => TryWriteString(
                    ChildSessionEnvironmentCheck.TermServiceKeyPath,
                    "ObjectName",
                    @"NT AUTHORITY\NetworkService",
                    RegistryValueKind.ExpandString,
                    out _),

                "DependOnService" => TryAppendServiceDependencies(out _),

                _ => true
            };

            if (!ok)
            {
                failures.Add(deviation.Field);
            }
        }

        if (failures.Count > 0)
        {
            return new ChildSessionFixResult(
                false,
                $"以下字段恢复失败：{string.Join("、", failures)}。");
        }

        var restart = RestartTermService(cancellationToken);
        if (!restart.Success)
        {
            return new ChildSessionFixResult(false, $"服务键已恢复，但{restart.Message}");
        }

        var message = "TermService 服务键已恢复默认值（依赖只做了追加）。";
        if (backupPath is not null)
        {
            message += $"原值已备份到 {backupPath}。";
        }

        return new ChildSessionFixResult(true, message);
    }

    /// <summary>
    /// 清理 RDP 破解工具的持久化项：计划任务导出 XML 后删除，自启项备份后删值。
    /// </summary>
    private static ChildSessionFixResult FixToolPersistence()
    {
        var hits = ChildSessionEnvironmentCheck.GetToolPersistenceHits();
        if (hits.Count == 0)
        {
            return new ChildSessionFixResult(true, "没有发现需要清理的开机项。");
        }

        var backupDirectory = CreateToolBackupDirectory();
        var removedTasks = 0;
        var removedValues = 0;
        var failures = new List<string>();

        foreach (var hit in hits)
        {
            if (hit.Kind == "task")
            {
                if (RemoveScheduledTask(hit.TaskPath, backupDirectory))
                {
                    removedTasks++;
                }
                else
                {
                    failures.Add(hit.Target);
                }

                continue;
            }

            if (RemoveRunValue(hit, backupDirectory))
            {
                removedValues++;
            }
            else
            {
                failures.Add(hit.Target);
            }
        }

        ChildSessionEnvironmentCheck.InvalidateScanCache();

        if (failures.Count > 0)
        {
            return new ChildSessionFixResult(
                false,
                $"部分开机项清理失败：{string.Join("；", failures)}");
        }

        return new ChildSessionFixResult(
            true,
            $"已清理 {removedTasks} 个计划任务、{removedValues} 个自启项，备份在 {backupDirectory}。");
    }

    /// <summary>
    /// 修复被直接打过补丁（或丢失）的 termsrv.dll。
    ///
    /// 判定用 sfc /verifyfile，所以修复也用同一把尺子验收：复制完必须复核通过才算成功，
    /// 否则回滚原文件再交给 sfc —— 不能因为「看起来复制成功了」就宣布修好。
    /// </summary>
    private static ChildSessionFixResult FixTermsrvDll(CancellationToken cancellationToken)
    {
        var state = ChildSessionEnvironmentCheck.GetTermsrvState();
        if (state.Exists && state.Verified && state.Intact)
        {
            return new ChildSessionFixResult(true, "termsrv.dll 与系统组件库一致，无需修复。");
        }

        var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var termsrvPath = Path.Combine(system32, "termsrv.dll");

        // 1) 用工具自己的备份还原，但必须复核通过
        foreach (var backupName in state.Backups)
        {
            var backupPath = Path.Combine(system32, backupName);
            if (!File.Exists(backupPath))
            {
                continue;
            }

            var restore = TryRestoreTermsrvFromBackup(termsrvPath, backupPath, cancellationToken);
            if (restore is not null)
            {
                return restore.Value;
            }
        }

        // 2) 交给 sfc
        cancellationToken.ThrowIfCancellationRequested();

        var sfcResult = RunSfcScannow(cancellationToken);
        var after = ChildSessionEnvironmentCheck.VerifyTermsrvNow();

        return after.Exists && after.Verified && after.Intact
            ? new ChildSessionFixResult(true, $"已用 sfc 修复 termsrv.dll（{sfcResult}）。")
            : new ChildSessionFixResult(
                false,
                $"sfc 已运行（{sfcResult}），但文件仍与系统组件库不一致。"
                + "文件被远程桌面服务占用时，sfc 往往要到重启后才真正替换，请重启后再检测一次。");
    }

    /// <summary>
    /// 用备份覆盖 termsrv.dll，覆盖后用 sfc 复核。复核不通过则回滚并返回 null，让调用方继续走 sfc。
    /// </summary>
    private static ChildSessionFixResult? TryRestoreTermsrvFromBackup(
        string termsrvPath,
        string backupPath,
        CancellationToken cancellationToken)
    {
        if (HasActiveChildSession())
        {
            // 覆盖正在被服务使用的 DLL 要停服务；有活动会话时宁可不动。
            return new ChildSessionFixResult(
                false,
                "检测到还有正在运行的桌面分身会话。请先关闭分身再重试，"
                + "否则重启服务会把里面的程序一起关掉。");
        }

        var safetyCopy = Path.Combine(
            GetDataDirectory(),
            "termsrv.dll.replaced-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));

        try
        {
            if (File.Exists(termsrvPath))
            {
                File.Copy(termsrvPath, safetyCopy, overwrite: true);
            }

            ChildSessionServiceNativeMethods.TryStop("TermService", out _);
            cancellationToken.ThrowIfCancellationRequested();
            File.Copy(backupPath, termsrvPath, overwrite: true);
            ChildSessionServiceNativeMethods.TryStart("TermService", out _);

            var after = ChildSessionEnvironmentCheck.VerifyTermsrvNow();
            if (after.Exists && after.Verified && after.Intact)
            {
                return new ChildSessionFixResult(
                    true,
                    $"已用 {Path.GetFileName(backupPath)} 还原 termsrv.dll（原文件备份在 {safetyCopy}）。");
            }

            // 这个备份不是原版：回滚，别把系统留在更糟的状态。
            if (File.Exists(safetyCopy))
            {
                ChildSessionServiceNativeMethods.TryStop("TermService", out _);
                File.Copy(safetyCopy, termsrvPath, overwrite: true);
                ChildSessionServiceNativeMethods.TryStart("TermService", out _);
            }

            Logger.LogWarning("备份 {Backup} 未通过 sfc 校验，已回滚。", Path.GetFileName(backupPath));
            return null;
        }
        catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or SecurityException)
        {
            Logger.LogWarning(exception, "从备份还原 termsrv.dll 失败。");
            ChildSessionServiceNativeMethods.TryStart("TermService", out _);
            return null;
        }
    }

    /// <summary>
    /// 运行 <c>sfc /scannow</c>，返回给用户看的简短结果。
    /// </summary>
    private static string RunSfcScannow(CancellationToken cancellationToken)
    {
        try
        {
            var sfc = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "sfc.exe");

            if (!File.Exists(sfc))
            {
                return "找不到 sfc.exe";
            }

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = sfc,
                Arguments = "/scannow",
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return "sfc 启动失败";
            }

            // sfc 通常 5~15 分钟；给足时间，期间允许取消。
            var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(30);
            while (!process.WaitForExit(2000))
            {
                if (cancellationToken.IsCancellationRequested)
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

                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (DateTime.UtcNow > deadline)
                {
                    return "sfc 超过 30 分钟仍未结束";
                }
            }

            return process.ExitCode switch
            {
                0 => "未发现完整性冲突",
                1 => "已修复冲突文件",
                _ => $"sfc 返回 {process.ExitCode}"
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                              or System.ComponentModel.Win32Exception
                                              or IOException)
        {
            Logger.LogWarning(exception, "运行 sfc 失败。");
            return "sfc 运行失败";
        }
    }

    // ---------------- 辅助 ----------------

    /// <summary>
    /// 重启 TermService。先确认没有活动分身会话，避免打断正在用的用户。
    /// </summary>
    private static ChildSessionFixResult RestartTermService(CancellationToken cancellationToken)
    {
        if (HasActiveChildSession())
        {
            return new ChildSessionFixResult(
                false,
                "检测到还有正在运行的桌面分身会话。请先关闭分身再重试，"
                + "否则重启服务会把里面的程序一起关掉。");
        }

        if (!ChildSessionServiceNativeMethods.TryStop("TermService", out var stopError))
        {
            return new ChildSessionFixResult(false, $"停止远程桌面服务失败：{stopError}");
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!ChildSessionServiceNativeMethods.TryStart("TermService", out var startError))
        {
            return new ChildSessionFixResult(false, $"服务已停止但重新启动失败：{startError}");
        }

        return new ChildSessionFixResult(true, "远程桌面服务已重启。");
    }

    /// <summary>
    /// 是否存在正在运行的分身会话。存在时拒绝重启远程桌面服务。
    /// </summary>
    private static bool HasActiveChildSession() => ChildSessionNativeMethods.TryGetChildSessionId() is not null;

    /// <summary>
    /// 追加缺失的服务依赖（RPCSS / StateRepository），保留本机已有的其它依赖。
    /// </summary>
    private static bool TryAppendServiceDependencies(out string error)
    {
        error = string.Empty;

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(ChildSessionEnvironmentCheck.TermServiceKeyPath, writable: true);
            if (key is null)
            {
                error = $"无法打开注册表项 HKLM\\{ChildSessionEnvironmentCheck.TermServiceKeyPath}（权限不足或路径不存在）。";
                return false;
            }

            var current = key.GetValue("DependOnService") as string[] ?? [];
            var wanted = new List<string>(current);

            void Ensure(string name)
            {
                foreach (var existing in wanted)
                {
                    if (string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }

                wanted.Add(name);
            }

            Ensure("RPCSS");

            using (var stateRepository = baseKey.OpenSubKey(
                       @"SYSTEM\CurrentControlSet\Services\StateRepository"))
            {
                if (stateRepository is not null)
                {
                    Ensure("StateRepository");
                }
            }

            if (wanted.Count == current.Length)
            {
                return true;
            }

            key.SetValue("DependOnService", wanted.ToArray(), RegistryValueKind.MultiString);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
                                              or SecurityException
                                              or IOException)
        {
            error = $"写入注册表失败：{exception.Message}";
            return false;
        }
    }

    /// <summary>
    /// 写 DWORD 值。失败时返回给用户看的原因。
    /// </summary>
    private static bool TryWriteDword(string path, string name, int value, out string error)
    {
        error = string.Empty;

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(path, writable: true);
            if (key is null)
            {
                error = $"无法打开注册表项 HKLM\\{path}（权限不足或路径不存在）。";
                return false;
            }

            key.SetValue(name, value, RegistryValueKind.DWord);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
                                              or SecurityException
                                              or IOException)
        {
            error = $"写入注册表失败：{exception.Message}";
            return false;
        }
    }

    /// <summary>
    /// 写字符串值（可指定类型，用于 REG_EXPAND_SZ）。
    /// </summary>
    private static bool TryWriteString(
        string path,
        string name,
        string value,
        RegistryValueKind kind,
        out string error)
    {
        error = string.Empty;

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(path, writable: true);
            if (key is null)
            {
                error = $"无法打开注册表项 HKLM\\{path}（权限不足或路径不存在）。";
                return false;
            }

            key.SetValue(name, value, kind);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
                                              or SecurityException
                                              or IOException)
        {
            error = $"写入注册表失败：{exception.Message}";
            return false;
        }
    }

    /// <summary>
    /// 备份当前 ServiceDll 的值与类型，返回备份文件路径。
    /// </summary>
    private static string? WriteServiceDllBackup(ChildSessionEnvironmentCheck.ServiceDllEntry entry)
    {
        try
        {
            var path = Path.Combine(GetDataDirectory(), "child-session-service-dll-backup.txt");
            File.WriteAllText(
                path,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}"
                + (entry.Exists
                    ? $"Value={entry.Value}{Environment.NewLine}Kind={entry.Kind}"
                    : "<值不存在>"),
                Encoding.UTF8);
            return path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Logger.LogWarning(exception, "备份 ServiceDll 状态失败。");
            return null;
        }
    }

    /// <summary>
    /// 把服务键的异常字段导出成文本，便于手工回滚。
    /// </summary>
    private static string? WriteServiceKeyBackup(
        IReadOnlyList<ChildSessionEnvironmentCheck.ServiceKeyDeviation> deviations)
    {
        try
        {
            var path = Path.Combine(GetDataDirectory(), "child-session-termservice-key-backup.txt");
            var builder = new StringBuilder();
            builder.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            foreach (var deviation in deviations)
            {
                builder.AppendLine($"{deviation.Field}: {deviation.Current}  ->  {deviation.Expect}");
            }

            File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
            return path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Logger.LogWarning(exception, "备份 TermService 服务键失败。");
            return null;
        }
    }

    /// <summary>
    /// 数据目录：与 BetterGI 的 User 目录同级，便于随配置一起备份。
    /// </summary>
    private static string GetDataDirectory()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BetterGenshinImpact",
            "ChildSessionBackup");
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>
    /// 创建本次清理的备份目录。
    /// </summary>
    private static string CreateToolBackupDirectory()
    {
        var directory = Path.Combine(
            GetDataDirectory(),
            "rdp-tool-backup",
            DateTime.Now.ToString("yyyyMMdd-HHmmss"));

        try
        {
            Directory.CreateDirectory(directory);
            return directory;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Logger.LogWarning(exception, "创建清理备份目录失败，改为使用临时目录。");
            return Path.GetTempPath();
        }
    }

    /// <summary>
    /// 导出一份任务 XML 再删除任务。
    /// </summary>
    private static bool RemoveScheduledTask(string taskPath, string backupDirectory)
    {
        if (string.IsNullOrWhiteSpace(taskPath))
        {
            return false;
        }

        var schtasks = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "schtasks.exe");

        if (!File.Exists(schtasks))
        {
            return false;
        }

        try
        {
            var query = RunProcess(schtasks, $"/Query /TN \"{taskPath}\" /XML");
            if (query.Output is not null)
            {
                var fileName = taskPath.Trim('\\').Replace('\\', '_') + ".xml";
                File.WriteAllText(Path.Combine(backupDirectory, fileName), query.Output, Encoding.UTF8);
            }

            // 删除必须看退出码：不看的话删失败也报「已清理」，面板刷新后问题还在。
            var delete = RunProcess(schtasks, $"/Delete /TN \"{taskPath}\" /F");
            return delete.ExitCode == 0;
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                              or System.ComponentModel.Win32Exception
                                              or IOException
                                              or UnauthorizedAccessException)
        {
            Logger.LogWarning(exception, "删除计划任务 {Task} 失败。", taskPath);
            return false;
        }
    }

    /// <summary>
    /// 备份并删除一处自启项。
    /// </summary>
    private static bool RemoveRunValue(
        ChildSessionEnvironmentCheck.ToolPersistenceHit hit,
        string backupDirectory)
    {
        try
        {
            var root = hit.RegistryRoot switch
            {
                "HKCU" => Registry.CurrentUser,
                "HKU" => Registry.Users,
                _ => Registry.LocalMachine
            };

            using var key = root.OpenSubKey(hit.RegistryPath, writable: true);
            if (key is null)
            {
                return false;
            }

            var data = key.GetValue(hit.ValueName) as string ?? string.Empty;

            // 原文留一份，万一删错了还能手工加回去。
            var fileName = MakeSafeFileName(hit.Target) + ".txt";
            File.WriteAllText(
                Path.Combine(backupDirectory, fileName),
                hit.Target + Environment.NewLine + data + Environment.NewLine,
                Encoding.UTF8);

            key.DeleteValue(hit.ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception exception) when (exception is SecurityException
                                              or UnauthorizedAccessException
                                              or IOException)
        {
            Logger.LogWarning(exception, "删除自启项 {Target} 失败。", hit.Target);
            return false;
        }
    }

    /// <summary>
    /// 把带非法字符的显示文本转成安全文件名。
    /// </summary>
    private static string MakeSafeFileName(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            builder.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), ch) >= 0 ? '_' : ch);
        }

        return builder.ToString();
    }

    /// <summary>
    /// 跑一个外部命令，返回（标准输出，退出码）。跑不起来或超时返回退出码 -1。
    /// </summary>
    private static (string? Output, int ExitCode) RunProcess(string fileName, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            });

            if (process is null)
            {
                return (null, -1);
            }

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(30_000))
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

                return (output, -1);
            }

            return (output, process.ExitCode);
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                              or System.ComponentModel.Win32Exception
                                              or IOException)
        {
            Logger.LogWarning(exception, "运行 {File} 失败。", Path.GetFileName(fileName));
            return (null, -1);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Helpers;
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
///
/// 第五条只针对 sfc：<b>绝不终止正在运行的 sfc</b>。
/// sfc 的修复没有「取消」这个语义——进程被杀掉，替换动作不会丢，而是被登记成
/// 「重启后执行」，用户下次开机会被塞进「更新并关机/重启」，之后再运行 sfc 还会
/// 被要求先重启。所以超时或用户取消时只放弃等待，进程留着让它自己跑完。
/// </summary>
internal static class ChildSessionEnvironmentFixer
{
    private static readonly ILogger Logger = ChildSessionDiagnosticsLog.Instance;

    /// <summary>要修复的服务名。桌面分身只跟 TermService 打交道。</summary>
    private const string TermServiceName = "TermService";

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

        // 读不到当前值就没法备份、也没法判断要不要修。这时候硬写等于盲改，
        // 直接告诉用户需要提权比「修复成功」的假象好。
        if (entry.Unreadable)
        {
            return new ChildSessionFixResult(
                false,
                "无法读取 TermService 的 Parameters 键（权限不足）。请以管理员身份运行后再试。");
        }

        var hijacked = entry.Exists && ChildSessionEnvironmentCheck.IsServiceDllHijacked(entry.Value);
        var emptyValue = entry.Exists && string.IsNullOrWhiteSpace(entry.Value);
        var typeBroken = !entry.Exists || emptyValue
                         || entry.Kind != RegistryValueKind.ExpandString;

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
            || string.IsNullOrWhiteSpace(after.Value)
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
        if (backupPath is not null)
        {
            message += $"原值已备份到 {backupPath}。";
        }

        // 开机项没清干净就不算整体成功：任务还在的话，下次开机照样把补丁写回去。
        // 合并逻辑交给 AppendNote，和 FixTermsrvDll 保持同一条规则。
        return AppendNote(new ChildSessionFixResult(true, message), persistence);
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

        // 通过注册表写下去时，SCM 内存里还是旧配置，要重启系统才重新读取。
        // 记一下有没有走到这条退路，收尾时如实告诉用户。
        var needsReboot = false;

        foreach (var deviation in deviations)
        {
            var ok = deviation.Field switch
            {
                "ImagePath" => TrySetImagePath(ref needsReboot),
                "ObjectName" => TrySetObjectName(ref needsReboot),
                "DependOnService" => TrySetDependencies(ref needsReboot),
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
        if (needsReboot)
        {
            message += "其中部分字段是通过注册表写入的，需要重启电脑后服务才会读取新配置。";
        }

        if (backupPath is not null)
        {
            message += $"原值已备份到 {backupPath}。";
        }

        return new ChildSessionFixResult(true, message);
    }

    /// <summary>
    /// 改 TermService 的可执行映像。
    ///
    /// 优先走 <c>ChangeServiceConfig</c>：SCM 会立刻用上新配置。直接写注册表时
    /// SCM 内存里仍是旧值，回读注册表却是新值——「修复成功」但服务照样起不来。
    /// </summary>
    private static bool TrySetImagePath(ref bool needsReboot)
    {
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var imagePath = Path.Combine(systemDirectory, "svchost.exe") + " -k NetworkService";

        if (ChildSessionServiceNativeMethods.TryChangeServiceConfig(
                TermServiceName, imagePath, null, null, out _))
        {
            return true;
        }

        if (!TryWriteString(
                ChildSessionEnvironmentCheck.TermServiceKeyPath,
                "ImagePath",
                @"%SystemRoot%\System32\svchost.exe -k NetworkService",
                RegistryValueKind.ExpandString,
                out _))
        {
            return false;
        }

        needsReboot = true;
        return true;
    }

    /// <summary>
    /// 改 TermService 的运行账户。优先走 SCM，失败时退回注册表。
    /// </summary>
    private static bool TrySetObjectName(ref bool needsReboot)
    {
        if (ChildSessionServiceNativeMethods.TryChangeServiceConfig(
                TermServiceName, null, @"NT AUTHORITY\NetworkService", null, out _))
        {
            return true;
        }

        if (!TryWriteString(
                ChildSessionEnvironmentCheck.TermServiceKeyPath,
                "ObjectName",
                @"NT AUTHORITY\NetworkService",
                RegistryValueKind.ExpandString,
                out _))
        {
            return false;
        }

        needsReboot = true;
        return true;
    }

    /// <summary>
    /// 补齐缺失的服务依赖（只追加，不删本机已有的其它依赖）。
    /// </summary>
    private static bool TrySetDependencies(ref bool needsReboot)
    {
        var merged = MergeServiceDependencies();
        if (merged is { Length: > 0 }
            && ChildSessionServiceNativeMethods.TryChangeServiceConfig(
                TermServiceName, null, null, merged, out _))
        {
            return true;
        }

        if (!TryAppendServiceDependencies(out _))
        {
            return false;
        }

        needsReboot = true;
        return true;
    }

    /// <summary>
    /// 当前依赖 + RPCSS（Win11 上再补 StateRepository），保持原有顺序。
    /// </summary>
    private static string[]? MergeServiceDependencies()
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var serviceKey = baseKey.OpenSubKey(ChildSessionEnvironmentCheck.TermServiceKeyPath);
            if (serviceKey is null)
            {
                return null;
            }

            var current = serviceKey.GetValue("DependOnService") as string[] ?? [];
            var merged = new List<string>(current);

            // ChangeServiceConfig 的依赖列表是整表替换，只送服务名会把 DependOnGroup
            // 一起抹掉。组依赖在列表里用「+组名」表示，原样带回去。
            foreach (var group in serviceKey.GetValue("DependOnGroup") as string[] ?? [])
            {
                var entry = "+" + group.TrimStart('+');
                if (!merged.Exists(x => string.Equals(x, entry, StringComparison.OrdinalIgnoreCase)))
                {
                    merged.Add(entry);
                }
            }

            void Ensure(string name)
            {
                if (!merged.Exists(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)))
                {
                    merged.Add(name);
                }
            }

            Ensure("RPCSS");

            // 和检查侧同一个判据：Win10 的 TermService 出厂只依赖 RPCSS，
            // 补上 StateRepository 会写进一条系统本来没有的依赖（本类只加不删，回不去），
            // 而用户一旦把 StateRepository 设为禁用，TermService 就会 1075 起不来。
            if (OsVersionHelper.IsWindows11_OrGreater)
            {
                Ensure("StateRepository");
            }

            return merged.ToArray();
        }
        catch (Exception exception) when (exception is SecurityException
                                              or UnauthorizedAccessException
                                              or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// 清理 RDP 破解工具的持久化项：计划任务导出 XML 后删除，自启项备份后删值。
    /// </summary>
    private static ChildSessionFixResult FixToolPersistence()
    {
        var hits = ChildSessionEnvironmentCheck.GetToolPersistenceHits();
        if (hits.Count == 0)
        {
            // 扫不完整时「没有命中」不等于「没有」：任务藏在读不到的目录里时，
            // 报「没有发现需要清理的开机项」会让用户以为已经干净了。
            return ChildSessionEnvironmentCheck.IsTaskScanIncomplete()
                ? new ChildSessionFixResult(
                    false,
                    "部分任务目录系统拒绝访问，扫描不完整，无法确认是否还有残留。"
                    + "请以管理员身份重新检测后再试。")
                : new ChildSessionFixResult(true, "没有发现需要清理的开机项。");
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
    /// 否则回滚原文件再交给下一步 —— 不能因为「看起来复制成功了」就宣布修好。
    ///
    /// 修复顺序：工具备份还原 → sfc /scanfile → sfc /scannow（独立窗口兜底）。
    /// 前两步都在 TermService 停止的窗口里跑并当场复验：文件没被映射，sfc 能就地替换，
    /// 修好了当场生效，不用重启。只有单文件都修不好时才退到完整扫描。
    /// </summary>
    private static ChildSessionFixResult FixTermsrvDll(CancellationToken cancellationToken)
    {
        // 这里必须现验一次，不能读缓存。缓存过期或后台校验还没跑完时，
        // GetTermsrvState() 返回的是 Verified=false 的「未校验」状态，守卫拦不住，
        // 后面的流程照样会停服务、覆盖系统 DLL，还可能一路跑到 sfc /scannow。
        // 为一个本来完好的文件付这些代价不划算，所以先拿到权威结论再动手。
        var state = ChildSessionEnvironmentCheck.VerifyTermsrvNow();
        if (state.Exists && state.Verified && state.Intact)
        {
            return new ChildSessionFixResult(true, "termsrv.dll 与系统组件库一致，无需修复。");
        }

        if (state.Exists && !state.Verified)
        {
            // 非提权时 sfc 必然失败。把「没验成」当成「被改过」去修会误伤系统文件。
            return new ChildSessionFixResult(
                false,
                "无法校验 termsrv.dll（通常是没有以管理员身份运行）。为免误改系统文件，本次未做任何修改，请提权后重试。");
        }

        // 文件被改过，就把「把补丁写回去」的开机任务一起清掉，否则还原完重启又变回去。
        // 必须在还原之前取：文件一致之后，「是否深扫」的条件就不成立了，
        // 藏在系统子树里的任务会被漏掉。
        var persistence = ChildSessionEnvironmentCheck.GetToolPersistenceHits().Count > 0
            ? FixToolPersistence()
            : (ChildSessionFixResult?)null;

        var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var termsrvPath = Path.Combine(system32, "termsrv.dll");

        // 1) 用工具自己的备份还原，但必须复核通过
        string? backupStopFailure = null;
        foreach (var backupName in state.Backups)
        {
            var backupPath = Path.Combine(system32, backupName);
            if (!File.Exists(backupPath))
            {
                continue;
            }

            var restore = TryRestoreTermsrvFromBackup(
                termsrvPath, backupPath, cancellationToken, out var restoreStopError);
            backupStopFailure ??= restoreStopError;

            if (restore is not null)
            {
                return AppendNote(restore.Value, persistence);
            }

            // 服务停不下来时，换下一个备份也是同样的结局：DLL 还被映射着，复制必然失败。
            // 跳出循环，留给下面的 sfc 去给结论——那里同样要停服务，失败原因更具体。
            if (restoreStopError is not null)
            {
                break;
            }
        }

        // 2) 用 sfc /scanfile 就地修：只校验这一个文件，几秒到几十秒。
        //    关键是它跑在服务停止的窗口里——文件没被映射，修复能当场落地，
        //    不像 /scannow 那样被占用、只能登记到重启后。
        cancellationToken.ThrowIfCancellationRequested();

        var scanfileRepair = TryRepairTermsrvWithSfcScanfile(
            termsrvPath, cancellationToken, out var scanfileStopError);
        if (scanfileRepair is not null)
        {
            return AppendNote(scanfileRepair.Value, persistence);
        }

        // 3) 兜底：交回完整扫描，但不在本进程里等。
        //    进度和结论还给 sfc 自己的控制台，我们既不用解析它的输出，
        //    也不会因为超时去杀它——被杀会把替换登记成「重启后执行」，
        //    用户下次开机被塞进「更新并重启」，比多等几分钟糟得多。
        var launch = LaunchSfcScannowInConsole();

        var launchResult = launch switch
            {
                SfcLaunch.Launched => new ChildSessionFixResult(
                    false,
                    "已在新窗口启动完整的 sfc /scannow（通常 5~15 分钟）。请不要关闭该窗口——"
                    + "sfc 的修复被中断会转成「重启后执行」，下次开机会被强制修复。"
                    + "窗口里出现结论后重新检测；若提示需要重启，请重启后再检测一次。"),

                // 上一次 /scannow 还在跑。再开一个会让两个 sfc 抢同一个文件，
                // 而且它们都会登记 pending，重启后一起执行。
                SfcLaunch.AlreadyRunning => new ChildSessionFixResult(
                    false,
                    "已经有一个 sfc 正在运行，本次没有再启动。等它结束后重新检测即可。"),

                _ => new ChildSessionFixResult(
                    false,
                    "无法自动启动 sfc /scannow，请以管理员身份打开命令行，手动执行 sfc /scannow。")
            };

        if (backupStopFailure is not null || scanfileStopError is not null)
        {
            // 根因要跟着结论一起给用户看：只留在日志里，用户只会看到「sfc 跑过了但还是不一致」。
            // 两步分开说——备份停不下服务、就地修复却停下来了，这种情况不能混成一句「都没进行」。
            launchResult = launchResult with
            {
                Message = (backupStopFailure is not null
                              ? $"备份还原时无法停止远程桌面服务（{backupStopFailure}）。"
                              : string.Empty)
                          + (scanfileStopError is not null
                              ? $"就地修复时无法停止远程桌面服务（{scanfileStopError}）。"
                              : string.Empty)
                          + launchResult.Message
            };
        }

        return AppendNote(launchResult, persistence);
    }

    /// <summary>
    /// 把「顺带清理持久化项」的结论并进主结果，避免用户只看到「已修复」却不知道还清了任务。
    /// </summary>
    private static ChildSessionFixResult AppendNote(
        ChildSessionFixResult result,
        ChildSessionFixResult? persistence)
    {
        if (persistence is not { } note || string.IsNullOrWhiteSpace(note.Message))
        {
            return result;
        }

        return result with
        {
            // 开机项没清干净不能算整体成功：文件还原了、任务还在，下次开机照样把补丁写回去。
            Success = result.Success && note.Success,
            Message = result.Message + " " + note.Message
        };
    }

    /// <summary>
    /// 用备份覆盖 termsrv.dll，覆盖后用 sfc 复核。复核不通过则回滚并返回 null，让调用方继续走 sfc。
    /// </summary>
    private static ChildSessionFixResult? TryRestoreTermsrvFromBackup(
        string termsrvPath,
        string backupPath,
        CancellationToken cancellationToken,
        out string? stopFailure)
    {
        stopFailure = null;

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

        var stoppedDependents = new List<string>();
        var termServiceStopped = false;
        // 回滚失败时磁盘上会留一个未通过校验的 DLL，这时候不能把服务拉起来。
        var restoreSafeToStart = true;

        try
        {
            if (File.Exists(termsrvPath))
            {
                File.Copy(termsrvPath, safetyCopy, overwrite: true);
            }

            // 依赖服务在跑时 SCM 会拒绝停止 TermService（错误 1051），必须先连带停掉：
            // 服务没停就覆盖一个正被加载的 DLL，复制会失败，还白白退回耗时的 sfc /scannow。
            //
            // 标志位要提前置位：TryStop 发出停止请求后是在等状态收敛，等待超时会返回 false，
            // 可服务此刻可能已经停了或正在停。这时候跳过 TryStart 就等于把远程桌面关掉不还。
            // TryStart 对运行中的服务直接返回成功，多调一次没有副作用。
            termServiceStopped = true;
            if (!TryStopTermService(stoppedDependents, out var stopError, cancellationToken))
            {
                // 返回 null 让调用方继续走 sfc，但要把原因带出去：
                // 只写日志的话，用户最后只看得到「sfc 跑过了还是不一致」。
                Logger.LogWarning("停止 TermService 失败，跳过备份还原：{Error}", stopError);
                stopFailure = stopError;
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Copy(backupPath, termsrvPath, overwrite: true);

            // 校验只看文件哈希，服务停着也能做。先验完再决定要不要重启，
            // 免得为了回滚多折腾一次服务。
            var after = ChildSessionEnvironmentCheck.VerifyTermsrvNow();
            if (after.Exists && after.Verified && after.Intact)
            {
                return new ChildSessionFixResult(
                    true,
                    $"已用 {Path.GetFileName(backupPath)} 还原 termsrv.dll（原文件备份在 {safetyCopy}）。");
            }

            // 这个备份不是原版：回滚，别把系统留在更糟的状态。
            // 服务此刻还停着，直接覆盖回去即可，不需要再停一次。
            if (File.Exists(safetyCopy))
            {
                try
                {
                    File.Copy(safetyCopy, termsrvPath, overwrite: true);
                }
                catch (Exception rollbackException) when (rollbackException is IOException
                                                              or UnauthorizedAccessException
                                                              or SecurityException)
                {
                    // 回滚也失败了：磁盘上仍是不合格的 DLL，交给 finally 记住别重启服务。
                    restoreSafeToStart = false;
                    Logger.LogError(
                        rollbackException,
                        "回滚 termsrv.dll 失败，当前文件未通过校验，远程桌面服务将保持停止状态，请运行 sfc /scannow 修复后重启电脑。");
                }
            }

            if (!restoreSafeToStart)
            {
                // 回滚失败：磁盘上留的是我们刚换上去的那份不合格 DLL。
                // 这时候不能把 null 交给调用方继续走 sfc /scanfile —— 那一步会再停一次服务、
                // 结束时无条件重启，等于把这份不可信的文件重新加载一遍，正好绕过了 finally 的守卫。
                // 到此为止，把「手动完整扫描 + 重启」讲清楚。
                return new ChildSessionFixResult(
                    false,
                    "备份还原失败，回滚也没能把原文件写回去。为避免远程桌面服务加载一份不可信的文件，"
                    + "服务已保持停止状态。请以管理员身份运行 sfc /scannow 修复，然后重启电脑。");
            }

            Logger.LogWarning("备份 {Backup} 未通过 sfc 校验，已回滚。", Path.GetFileName(backupPath));
            return null;
        }
        catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or SecurityException)
        {
            Logger.LogWarning(exception, "从备份还原 termsrv.dll 失败。");
            return null;
        }
        finally
        {
            // 回滚失败时磁盘上留的是未通过校验的文件：这时候既不能重启 TermService，
            // 也不能恢复依赖服务——SCM 启动 UmRdpService 这类服务时会把它的依赖
            // TermService 一起拉起来，同样会加载坏 DLL。所以整块跳过，等 sfc 修完再说。
            //
            // 其余情况一律把服务拉回来，否则用户会剩下一个停着的远程桌面服务。
            if (restoreSafeToStart)
            {
                if (termServiceStopped
                    && !ChildSessionServiceNativeMethods.TryStart(TermServiceName, out var startError))
                {
                    Logger.LogWarning("还原流程结束后重启 TermService 失败：{Error}", startError);
                }

                RestoreDependents(stoppedDependents);
            }
        }
    }

    /// <summary>
    /// 在 TermService 停止的状态下用 <c>sfc /scanfile=</c> 就地修复 termsrv.dll。
    ///
    /// 相比 /scannow 的两点好处：
    /// 1. 只校验一个文件，几秒到几十秒，用户不必等一次完整扫描；
    /// 2. 服务停着，文件没被映射，sfc 可以当场替换，不必等到重启才生效。
    /// </summary>
    /// <returns>修好返回结果；没修好返回 null，由调用方交给兜底。</returns>
    private static ChildSessionFixResult? TryRepairTermsrvWithSfcScanfile(
        string termsrvPath,
        CancellationToken cancellationToken,
        out string? stopFailure)
    {
        stopFailure = null;

        if (HasActiveChildSession())
        {
            return new ChildSessionFixResult(
                false,
                "检测到还有正在运行的桌面分身会话。请先关闭分身再重试，"
                + "否则重启服务会把里面的程序一起关掉。");
        }

        var stoppedDependents = new List<string>();
        var termServiceStopped = false;

        try
        {
            // 标志位提前置位：TryStop 是在等状态收敛，等待超时会返回 false，
            // 但服务此刻可能已经停了。跳过 TryStart 就等于把远程桌面关掉不还。
            termServiceStopped = true;
            if (!TryStopTermService(stoppedDependents, out var stopError, cancellationToken))
            {
                Logger.LogWarning("停止 TermService 失败，跳过 sfc /scanfile 修复：{Error}", stopError);
                stopFailure = stopError;
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var outcome = RunSfcScanfile(termsrvPath);
            Logger.LogInformation("sfc /scanfile 结果：{Description}", outcome.Description);

            if (outcome.Deferred)
            {
                // 已有 sfc 在跑，或者它超时了还在跑。这时候既不能杀，也不能再起一个
                // /scannow 兜底——两个 sfc 同时改同一个文件，CBS 会互相打架。
                return new ChildSessionFixResult(
                    false,
                    $"{outcome.Description}，请稍后重新检测（修复可能仍在后台进行）。");
            }

            // 只认文件本身，不猜退出码：sfc 返回 0 既可能是「本来就一致」，
            // 也可能是「修好了」，还可能是「登记了重启后修复」，三者分不开。
            var after = ChildSessionEnvironmentCheck.VerifyTermsrvNow();
            if (after.Exists && after.Verified && after.Intact)
            {
                return new ChildSessionFixResult(
                    true,
                    $"已就地修复 termsrv.dll（{outcome.Description}），无需重启。");
            }

            Logger.LogWarning(
                "sfc /scanfile 未能修复 termsrv.dll（{Description}），转入完整扫描。", outcome.Description);
            return null;
        }
        finally
        {
            // 修复没成功也要把服务拉回来：不能因为一次失败的修复让用户失去远程桌面。
            // sfc 用事务性替换，不会留下写到一半的文件，所以这里不需要像回滚备份那样
            // 担心重启服务会加载坏 DLL。
            if (termServiceStopped
                && !ChildSessionServiceNativeMethods.TryStart(TermServiceName, out var startError))
            {
                Logger.LogWarning("sfc /scanfile 结束后重启 TermService 失败：{Error}", startError);
            }

            RestoreDependents(stoppedDependents);
        }
    }

    /// <summary>
    /// 单文件扫描的结果。
    /// </summary>
    /// <param name="ExitCode">sfc 退出码；没跑起来是 -1。</param>
    /// <param name="Description">给用户看的一句话。</param>
    /// <param name="Deferred">本次没有拿到结论：要么已有 sfc 在跑，要么它超时了还在跑。</param>
    private readonly record struct SfcOutcome(int ExitCode, string Description, bool Deferred = false);

    /// <summary>
    /// 运行 <c>sfc /scanfile=</c> 并等它结束。
    ///
    /// 两个「绝不」：
    /// 1. <b>绝不杀进程</b> —— sfc 的修复被中断只会变成 pending，下次开机强制跑一遍；
    /// 2. <b>绝不起第二个 sfc</b> —— 两个 sfc 同时改同一个文件，CBS 会互相打架。
    ///    所以已有 sfc 在跑、或本次超时仍未结束时，返回 Deferred 让调用方收手。
    /// </summary>
    private static SfcOutcome RunSfcScanfile(string path)
    {
        var gate = ChildSessionEnvironmentCheck.SfcGate;

        // 和后台校验共用一把闸：后台 verifyfile 只要几秒，等一会儿比撞车划算。
        if (!gate.Wait(TimeSpan.FromSeconds(30)))
        {
            return new SfcOutcome(-1, "已有 sfc 在运行，本次修复没有启动", Deferred: true);
        }

        var gateHandedOver = false;
        Process? process = null;
        try
        {
            var sfc = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "sfc.exe");

            if (!File.Exists(sfc))
            {
                return new SfcOutcome(-1, "找不到 sfc.exe");
            }

            // 不用 using：超时要把进程的所有权连同闸一起交给后台，
            // 这里提前 Dispose 会让后台的 WaitForExit 失去句柄。
            process = Process.Start(new ProcessStartInfo
            {
                FileName = sfc,
                Arguments = $"/scanfile={path}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,

                // sfc 输出是 UTF-16LE。不设这一行读到的是夹了一堆 \0 的乱码。
                StandardOutputEncoding = Encoding.Unicode
            });

            if (process is null)
            {
                return new SfcOutcome(-1, "sfc 启动失败");
            }

            // 必须先挂上读再等退出：输出超过管道缓冲区时子进程会卡在写，双方一起等死。
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var started = process;

            var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(2);
            while (!started.WaitForExit(500))
            {
                if (DateTime.UtcNow > deadline)
                {
                    // 进程还活着，就让它跑完：这里既不能杀，也不能再起一个。
                    // 闸也不能就地放——放了等于允许下一个 sfc 进来跟还活着的这个抢同一个文件。
                    // 所有权交给后台，等它真正退出再放。
                    HandOverGateUntilExit(started, gate);
                    gateHandedOver = true;
                    process = null;
                    return new SfcOutcome(-1, "sfc /scanfile 超过 2 分钟仍在运行", Deferred: true);
                }
            }

            var text = outputTask.GetAwaiter().GetResult();
            return new SfcOutcome(started.ExitCode, DescribeSfcOutput(text, started.ExitCode));
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                              or System.ComponentModel.Win32Exception
                                              or IOException)
        {
            Logger.LogWarning(exception, "运行 sfc /scanfile 失败。");
            return new SfcOutcome(-1, "sfc /scanfile 运行失败");
        }
        finally
        {
            process?.Dispose();
            if (!gateHandedOver)
            {
                gate.Release();
            }
        }
    }

    /// <summary>
    /// 把闸的所有权交给 sfc 进程：等它真正退出后才释放。
    ///
    /// 超时时进程还活着，就地释放等于允许第二个 sfc 进来改同一个文件 —— CBS 会互相打架，
    /// 而我们要么拿不到结论、要么把文件改坏。挂在后台等待进程退出是唯一诚实的做法。
    /// </summary>
    private static void HandOverGateUntilExit(Process process, SemaphoreSlim gate)
    {
        _ = Task.Run(() =>
        {
            try
            {
                process.WaitForExit();
            }
            catch (Exception exception) when (exception is InvalidOperationException
                                                  or System.ComponentModel.Win32Exception)
            {
                Logger.LogWarning(exception, "等待超时的 sfc /scanfile 退出失败。");
            }
            finally
            {
                process.Dispose();
                gate.Release();
            }
        });
    }

    /// <summary>
    /// 把 sfc 的中文输出压成一句能放进 UI 的话。
    /// 匹配不到就退回退出码——文案随系统语言变，不能拿它当判定依据，
    /// 判定一律看 <see cref="ChildSessionEnvironmentCheck.VerifyTermsrvNow"/> 的复验结果。
    /// </summary>
    private static string DescribeSfcOutput(string output, int exitCode)
    {
        if (output.Contains("未找到任何完整性违规", StringComparison.Ordinal))
        {
            return "校验通过，未发现损坏";
        }

        if (output.Contains("无法修复", StringComparison.Ordinal))
        {
            return "发现损坏但 sfc 无法修复";
        }

        if (output.Contains("成功修复", StringComparison.Ordinal))
        {
            return "已修复损坏文件";
        }

        if (output.Contains("重新启动", StringComparison.Ordinal)
            || output.Contains("重启", StringComparison.Ordinal))
        {
            return "sfc 要求重启后才能完成";
        }

        return $"sfc 返回 {exitCode}";
    }

    /// <summary>
    /// 兜底扫描的启动结果。
    /// </summary>
    private enum SfcLaunch
    {
        Launched,
        AlreadyRunning,
        Failed
    }

    /// <summary>
    /// 弹出独立命令行窗口运行 <c>sfc /scannow</c>，把进度和结论交回给 sfc 自己。
    ///
    /// 刻意不等待、不重定向、不杀进程：
    /// 1. 它是保底手段，跑几分钟很正常，把主程序卡在那儿没有意义；
    /// 2. sfc 的修复无法真正中断——被杀掉只会把替换动作登记到下次开机，
    ///    用户会被塞进「更新并关机/重启」，再运行 sfc 还会被要求先重启。
    ///    与其冒险终止，不如让它自己跑完。
    ///
    /// 走到这里时 TermService 已经恢复运行，termsrv.dll 又处在被映射状态，
    /// 所以这次扫描多半也是登记 pending、重启后才替换——这是预期行为，
    /// 调用方会把「可能需要重启」写进给用户的话里。
    /// </summary>
    private static SfcLaunch LaunchSfcScannowInConsole()
    {
        // 这里不持 SfcGate：这次扫描要跑几分钟，占着闸会把后台复核全部堵死。
        // 改成查进程——它同时能挡住「用户自己开的 sfc」和「上一次留下的窗口」。
        if (IsSfcRunning())
        {
            Logger.LogInformation("已有 sfc 进程在运行，跳过兜底扫描。");
            return SfcLaunch.AlreadyRunning;
        }

        try
        {
            var sfc = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "sfc.exe");

            if (!File.Exists(sfc))
            {
                Logger.LogWarning("找不到 sfc.exe，无法启动兜底扫描。");
                return SfcLaunch.Failed;
            }

            // 外层 cmd 用 start 显式开一个新控制台，内层 /k 让窗口留着不关，
            // 用户能看到 sfc 的输出和最后结论。
            // 不用 CreateNoWindow=false 直接起 cmd：父进程要是本来就有控制台
            // （比如从终端启动的本程序），子进程会附到同一个窗口里，看起来像卡住了。
            // 命令行里不掺中文：参数经 cmd 的代码页转换容易变乱码，提示放在 UI 文案里。
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                Arguments = "/c start \"\" cmd.exe /k sfc /scannow",
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return SfcLaunch.Failed;
            }

            Logger.LogInformation("已在独立命令行窗口启动 sfc /scannow。");
            return SfcLaunch.Launched;
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                              or System.ComponentModel.Win32Exception
                                              or IOException)
        {
            Logger.LogWarning(exception, "启动 sfc /scannow 窗口失败。");
            return SfcLaunch.Failed;
        }
    }

    /// <summary>
    /// 是否已经有 sfc 在跑。
    ///
    /// 按进程名查而不是查我们的闸：闸只管本进程，而 sfc 是用户也可能自己开的东西。
    /// 查不到（权限不足等）当作没有——宁可多启动一次，也不能让兜底完全失效。
    /// </summary>
    private static bool IsSfcRunning()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName("sfc");
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                              or System.ComponentModel.Win32Exception)
        {
            Logger.LogWarning(exception, "无法枚举 sfc 进程，按「没有在运行」处理。");
            return false;
        }

        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
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

        // TermService 平时带着依赖在跑（UmRdpService 就依赖它）。有依赖在运行时
        // SCM 会直接拒绝停止（错误 1051），所以先把它们停掉，重启完再按原样拉回来。
        var stoppedDependents = new List<string>();

        try
        {
            if (!TryStopTermService(stoppedDependents, out var stopError, cancellationToken))
            {
                // 依赖没停干净是最常见的原因，这里把话说明白，别让用户对着 1051 猜。
                return new ChildSessionFixResult(
                    false,
                    $"{stopError}如果反复失败，重启电脑后再试一次。");
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (!ChildSessionServiceNativeMethods.TryStart(TermServiceName, out var startError))
            {
                return new ChildSessionFixResult(false, $"服务已停止但重新启动失败：{startError}");
            }
        }
        finally
        {
            RestoreDependents(stoppedDependents);
        }

        return new ChildSessionFixResult(true, "远程桌面服务已重启。");
    }

    /// <summary>
    /// 连同正在运行的依赖服务一起停掉 TermService。
    ///
    /// 依赖服务在跑时 SCM 会拒绝停止主服务（错误 1051），所以要先把它们逐个停下。
    /// 被停掉的服务名写进 <paramref name="stoppedDependents"/>，调用方收尾时
    /// 交给 <see cref="RestoreDependents"/> 按相反顺序拉回来。
    /// </summary>
    private static bool TryStopTermService(
        List<string> stoppedDependents,
        out string error,
        CancellationToken cancellationToken)
    {
        foreach (var dependent in ChildSessionServiceNativeMethods.GetRunningDependents(TermServiceName))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ChildSessionServiceNativeMethods.TryStop(dependent, out _))
            {
                stoppedDependents.Add(dependent);
            }
        }

        return ChildSessionServiceNativeMethods.TryStop(TermServiceName, out error);
    }

    /// <summary>
    /// 把刚才为了停服务而停掉的依赖服务拉回来。
    ///
    /// 顺序反过来启动：后停的通常是更外层的服务。启动失败只记日志——
    /// 这些都是按需启动的服务，真正需要时系统会自己拉起来，没必要因此判定修复失败。
    /// </summary>
    private static void RestoreDependents(IReadOnlyList<string> dependents)
    {
        for (var index = dependents.Count - 1; index >= 0; index--)
        {
            if (!ChildSessionServiceNativeMethods.TryStart(dependents[index], out var error))
            {
                Logger.LogWarning("恢复依赖服务 {Service} 失败：{Error}", dependents[index], error);
            }
        }
    }

    /// <summary>
    /// 是否存在正在运行的分身会话。存在时拒绝重启远程桌面服务。
    /// </summary>
    private static bool HasActiveChildSession() => ChildSessionNativeMethods.TryGetChildSessionId() is not null;

    /// <summary>
    /// 追加缺失的服务依赖（RPCSS；Win11 上还有 StateRepository），保留本机已有的其它依赖。
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

            // 与 MergeServiceDependencies、检查侧保持同一判据，别在 Win10 上凭空造依赖。
            if (OsVersionHelper.IsWindows11_OrGreater)
            {
                Ensure("StateRepository");
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

            // 用 CreateSubKey 而不是 OpenSubKey：Parameters 子键被整键删掉时，
            // OpenSubKey 只会返回 null，修复就以「路径不存在」失败——而这时恰恰最需要写回去。
            using var key = baseKey.CreateSubKey(path, writable: true);
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
                // 备份要能直接喂回 `schtasks /Create /XML`，所以编码必须和文件里的
                // 声明一致：schtasks 输出的 XML 头写的是 UTF-16，字节却是本地代码页，
                // 按 UTF-8 存回去会对不上（中文任务名、描述会乱码）。
                // 文件名同样要走截断：任务可以藏在很深的子树里，拼出来的路径会超长。
                var fileName = MakeSafeFileName(taskPath.Trim('\\')) + ".xml";
                File.WriteAllText(Path.Combine(backupDirectory, fileName), query.Output, Encoding.Unicode);
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
    ///
    /// 长度要限制：自启项的显示文本是「注册表位置 → 值名」，值名可以很长，
    /// 拼出来的文件名一旦超过文件系统上限，备份就写不进去——而「备份失败就不删」
    /// 意味着这条永远修不好。
    ///
    /// 哈希必须按**原文**算，并且无条件拼上去：非法字符是先被替换成 <c>_</c> 的，
    /// 只对替换后的文本求哈希时，<c>rdpwrap:a</c> 和 <c>rdpwrap?a</c> 会得到同一个名字，
    /// 第二个备份直接覆盖第一个，而两项都会被删掉——等于丢了一份备份。
    /// 完整内容仍写在文件正文里，名字只用于区分和对照。
    /// </summary>
    private static string MakeSafeFileName(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            builder.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), ch) >= 0 ? '_' : ch);
        }

        var suffix = "_" + StableSuffix(text);
        var limit = MaxBackupNameLength - suffix.Length;
        var safe = builder.ToString();

        return string.Concat(safe.AsSpan(0, Math.Min(safe.Length, limit)), suffix);
    }

    /// <summary>备份文件名里正文部分的最大长度，留出目录路径的余量。</summary>
    private const int MaxBackupNameLength = 80;

    /// <summary>
    /// 确定性的短哈希（FNV-1a）。用 string.GetHashCode 不行：那个每个进程都不一样，
    /// 重启一次就会在备份目录里多出一份同名文件。
    /// </summary>
    private static string StableSuffix(string text)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var ch in text)
            {
                hash = (hash ^ ch) * 16777619u;
            }

            return hash.ToString("x8");
        }
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

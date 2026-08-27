using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask;

internal readonly record struct GenshinGameProcessIdentity(
    uint ProcessId,
    long StartTimeUtcTicks);

internal enum GenshinProcessIdentityReadStatus
{
    Found,
    NotFound,
    Unavailable,
}

internal readonly record struct GenshinProcessIdentityReadResult(
    GenshinProcessIdentityReadStatus Status,
    GenshinGameProcessIdentity Identity = default,
    Exception? Error = null)
{
    public static GenshinProcessIdentityReadResult Found(GenshinGameProcessIdentity identity) =>
        new(GenshinProcessIdentityReadStatus.Found, identity);

    public static GenshinProcessIdentityReadResult NotFound() =>
        new(GenshinProcessIdentityReadStatus.NotFound);

    public static GenshinProcessIdentityReadResult Unavailable(Exception error) =>
        new(GenshinProcessIdentityReadStatus.Unavailable, Error: error);
}

internal enum GenshinHdrRestartCheckStatus
{
    NotRequired,
    RestartRequired,
    StateUnavailable,
}

internal readonly record struct GenshinHdrRestartCheckResult(
    GenshinHdrRestartCheckStatus Status,
    Exception? Error = null);

internal readonly record struct GenshinHdrRestartStateWriteResult(
    bool Success,
    Exception? Error = null);

internal readonly record struct GenshinHdrPolicyLockResult(
    bool Success,
    IDisposable? LockHandle = null,
    Exception? Error = null);

/// <summary>
/// 跨 BetterGI 重启保存仍在使用旧 HDR 配置的游戏进程。
/// 记录同时绑定 PID、UTC 启动时间、游戏版本和注册表目标，避免 PID 复用或双版本安装造成误拦截。
/// </summary>
internal sealed class GenshinHdrRestartStateStore
{
    private const int CurrentFormatVersion = 1;
    private const string DefaultStateFileName = "genshin-hdr-restart.json";
    private static readonly TimeSpan StateLockTimeout = TimeSpan.FromSeconds(2);
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);

    private readonly object _syncRoot = new();
    private readonly string _statePath;
    private readonly Func<uint, GenshinProcessIdentityReadResult> _processIdentityReader;
    private readonly Func<string, string?> _stateReader;
    private readonly Action<string, string> _stateWriter;

    internal static string DefaultStatePath => GetDefaultStatePath();

    public GenshinHdrRestartStateStore()
        : this(DefaultStatePath)
    {
    }

    private static string GetDefaultStatePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException("无法确定当前用户的 LocalAppData 目录。 ");
        }

        // HDR 注册表位于 HKCU；状态与策略锁也必须使用稳定的 per-user 路径，不能随解压目录变化。
        return Path.Combine(localAppData, "BetterGI", "State", DefaultStateFileName);
    }

    internal GenshinHdrRestartStateStore(
        string statePath,
        Func<uint, GenshinProcessIdentityReadResult>? processIdentityReader = null,
        Func<string, string?>? stateReader = null,
        Action<string, string>? stateWriter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        _statePath = statePath;
        _processIdentityReader = processIdentityReader ?? ReadProcessIdentityCore;
        _stateReader = stateReader ?? ReadStateText;
        _stateWriter = stateWriter ?? WriteStateTextAtomically;
    }

    /// <summary>
    /// 获取游戏进程的稳定身份。调用方只有在成功获取 PID 和启动时间后才可以持久化 marker。
    /// </summary>
    internal GenshinProcessIdentityReadResult ReadProcessIdentity(uint processId)
    {
        if (processId == 0)
        {
            return GenshinProcessIdentityReadResult.NotFound();
        }

        try
        {
            return _processIdentityReader(processId);
        }
        catch (Exception e)
        {
            return GenshinProcessIdentityReadResult.Unavailable(e);
        }
    }

    /// <summary>
    /// 串行化跨 BetterGI 进程的“检查状态—修改注册表—提交时间屏障”完整决策，消除多实例 TOCTOU。
    /// UI 提示前应释放返回的句柄，避免用户交互期间长期占用策略锁。
    /// </summary>
    internal async Task<GenshinHdrPolicyLockResult> TryAcquirePolicyLockAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return new GenshinHdrPolicyLockResult(
                true,
                await AcquireExclusiveFileLockAsync(
                        $"{_statePath}.policy.lock",
                        cancellationToken)
                    .ConfigureAwait(false));
        }
        catch (Exception e)
        {
            return new GenshinHdrPolicyLockResult(false, Error: e);
        }
    }

    /// <summary>
    /// 检查当前进程是否仍需重启，并顺便删除已退出或 PID 已复用的旧记录。
    /// 无法读取状态文件或无法保存清理结果时返回 StateUnavailable，由上层停止 SDR 捕获。
    /// </summary>
    internal GenshinHdrRestartCheckResult CheckAndPrune(
        uint currentProcessId,
        GenshinGameEdition edition,
        string registryTarget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registryTarget);

        lock (_syncRoot)
        {
            FileStream stateFileLock;
            try
            {
                stateFileLock = AcquireStateFileLock();
            }
            catch (Exception e)
            {
                return new GenshinHdrRestartCheckResult(
                    GenshinHdrRestartCheckStatus.StateUnavailable,
                    e);
            }

            using var stateFileLockScope = stateFileLock;
            var loadResult = TryLoad();
            if (!loadResult.Success)
            {
                return new GenshinHdrRestartCheckResult(
                    GenshinHdrRestartCheckStatus.StateUnavailable,
                    loadResult.Error);
            }

            var document = loadResult.Document!;
            var processResults = new Dictionary<uint, GenshinProcessIdentityReadResult>();
            var removed = document.Requirements.RemoveAll(requirement =>
            {
                if (!processResults.TryGetValue(requirement.ProcessId, out var processResult))
                {
                    processResult = ReadProcessIdentity(requirement.ProcessId);
                    processResults[requirement.ProcessId] = processResult;
                }

                return processResult.Status switch
                {
                    GenshinProcessIdentityReadStatus.NotFound => true,
                    GenshinProcessIdentityReadStatus.Found =>
                        processResult.Identity.StartTimeUtcTicks != requirement.ProcessStartTimeUtcTicks,
                    // 权限或瞬时错误不能证明旧进程已退出，保留 marker 以避免错误放行。
                    _ => false,
                };
            });

            if (removed > 0)
            {
                var writeResult = TryWrite(document);
                if (!writeResult.Success)
                {
                    return new GenshinHdrRestartCheckResult(
                        GenshinHdrRestartCheckStatus.StateUnavailable,
                        writeResult.Error);
                }
            }

            var restartRequired = currentProcessId != 0 && document.Requirements.Any(requirement =>
                requirement.ProcessId == currentProcessId &&
                requirement.Edition == edition &&
                string.Equals(requirement.RegistryTarget, registryTarget, StringComparison.OrdinalIgnoreCase));

            var registryChange = document.RegistryChanges.LastOrDefault(change =>
                change.Edition == edition &&
                string.Equals(change.RegistryTarget, registryTarget, StringComparison.OrdinalIgnoreCase));
            if (!restartRequired && currentProcessId != 0 && registryChange is not null)
            {
                if (!processResults.TryGetValue(currentProcessId, out var currentProcessResult))
                {
                    currentProcessResult = ReadProcessIdentity(currentProcessId);
                    processResults[currentProcessId] = currentProcessResult;
                }

                restartRequired = registryChange.AppliedAtUtcTicks is null ||
                                  currentProcessResult.Status == GenshinProcessIdentityReadStatus.Unavailable ||
                                  currentProcessResult.Status == GenshinProcessIdentityReadStatus.Found &&
                                  currentProcessResult.Identity.StartTimeUtcTicks <= registryChange.AppliedAtUtcTicks.Value;
            }

            return new GenshinHdrRestartCheckResult(
                restartRequired
                    ? GenshinHdrRestartCheckStatus.RestartRequired
                    : GenshinHdrRestartCheckStatus.NotRequired);
        }
    }

    /// <summary>
    /// 在注册表写入前同步保存 marker。保存失败必须阻止后续注册表修改。
    /// </summary>
    internal GenshinHdrRestartStateWriteResult TryMarkRestartRequired(
        GenshinGameProcessIdentity processIdentity,
        GenshinGameEdition edition,
        string registryTarget)
    {
        return TryPrepareRegistryChange(processIdentity, edition, registryTarget);
    }

    /// <summary>
    /// 在没有可见游戏窗口时仍预写版本级变更代次；这样隐藏窗口和同版本多进程也不能绕过检查。
    /// </summary>
    internal GenshinHdrRestartStateWriteResult TryPrepareRegistryChange(
        GenshinGameEdition edition,
        string registryTarget)
    {
        return TryPrepareRegistryChange(null, edition, registryTarget);
    }

    private GenshinHdrRestartStateWriteResult TryPrepareRegistryChange(
        GenshinGameProcessIdentity? processIdentity,
        GenshinGameEdition edition,
        string registryTarget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registryTarget);

        if (processIdentity is { } identity &&
            (identity.ProcessId == 0 || identity.StartTimeUtcTicks <= 0))
        {
            return new GenshinHdrRestartStateWriteResult(
                false,
                new ArgumentException("游戏进程身份缺少有效的 PID 或 UTC 启动时间。"));
        }

        var expectedTarget = GenshinHdrRegistryHelper.GetHdrRegistryFullValuePath(edition);
        if (!string.Equals(expectedTarget, registryTarget, StringComparison.OrdinalIgnoreCase))
        {
            return new GenshinHdrRestartStateWriteResult(
                false,
                new ArgumentException("游戏版本与 HDR 注册表目标不匹配。"));
        }

        lock (_syncRoot)
        {
            FileStream stateFileLock;
            try
            {
                stateFileLock = AcquireStateFileLock();
            }
            catch (Exception e)
            {
                return new GenshinHdrRestartStateWriteResult(false, e);
            }

            using var stateFileLockScope = stateFileLock;
            var loadResult = TryLoad();
            if (!loadResult.Success)
            {
                return new GenshinHdrRestartStateWriteResult(false, loadResult.Error);
            }

            var document = loadResult.Document!;
            var changed = false;
            if (processIdentity is { } runningIdentity &&
                !document.Requirements.Any(requirement =>
                    requirement.ProcessId == runningIdentity.ProcessId &&
                    requirement.ProcessStartTimeUtcTicks == runningIdentity.StartTimeUtcTicks &&
                    requirement.Edition == edition &&
                    string.Equals(requirement.RegistryTarget, registryTarget, StringComparison.OrdinalIgnoreCase)))
            {
                // 同一 PID 的旧启动时间必然来自已退出进程，先移除以处理 PID 复用。
                document.Requirements.RemoveAll(requirement =>
                    requirement.ProcessId == runningIdentity.ProcessId &&
                    requirement.ProcessStartTimeUtcTicks != runningIdentity.StartTimeUtcTicks);
                document.Requirements.Add(new GenshinHdrRestartRequirement
                {
                    ProcessId = runningIdentity.ProcessId,
                    ProcessStartTimeUtcTicks = runningIdentity.StartTimeUtcTicks,
                    Edition = edition,
                    RegistryTarget = registryTarget,
                });
                changed = true;
            }

            var existingChange = document.RegistryChanges.LastOrDefault(change =>
                change.Edition == edition &&
                string.Equals(change.RegistryTarget, registryTarget, StringComparison.OrdinalIgnoreCase));
            if (existingChange is null || existingChange.AppliedAtUtcTicks is not null)
            {
                document.RegistryChanges.RemoveAll(change =>
                    change.Edition == edition &&
                    string.Equals(change.RegistryTarget, registryTarget, StringComparison.OrdinalIgnoreCase));
                document.RegistryChanges.Add(new GenshinHdrRegistryChange
                {
                    Edition = edition,
                    RegistryTarget = registryTarget,
                    PreparedAtUtcTicks = DateTime.UtcNow.Ticks,
                });
                changed = true;
            }

            return changed ? TryWrite(document) : new GenshinHdrRestartStateWriteResult(true);
        }
    }

    /// <summary>
    /// 注册表写入完成后提交版本级时间屏障；该时间之前启动的同版本进程都必须重启。
    /// Pending 状态若因异常未提交，会更保守地拦截所有当前进程。
    /// </summary>
    internal GenshinHdrRestartStateWriteResult TryCompleteRegistryChange(
        GenshinGameEdition edition,
        string registryTarget,
        long appliedAtUtcTicks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registryTarget);
        if (appliedAtUtcTicks <= 0 || appliedAtUtcTicks > DateTime.MaxValue.Ticks)
        {
            return new GenshinHdrRestartStateWriteResult(
                false,
                new ArgumentOutOfRangeException(nameof(appliedAtUtcTicks)));
        }

        lock (_syncRoot)
        {
            FileStream stateFileLock;
            try
            {
                stateFileLock = AcquireStateFileLock();
            }
            catch (Exception e)
            {
                return new GenshinHdrRestartStateWriteResult(false, e);
            }

            using var stateFileLockScope = stateFileLock;
            var loadResult = TryLoad();
            if (!loadResult.Success)
            {
                return new GenshinHdrRestartStateWriteResult(false, loadResult.Error);
            }

            var document = loadResult.Document!;
            var change = document.RegistryChanges.LastOrDefault(candidate =>
                candidate.Edition == edition &&
                string.Equals(candidate.RegistryTarget, registryTarget, StringComparison.OrdinalIgnoreCase));
            if (change is null || change.AppliedAtUtcTicks is not null)
            {
                return new GenshinHdrRestartStateWriteResult(true);
            }

            if (appliedAtUtcTicks < change.PreparedAtUtcTicks)
            {
                return new GenshinHdrRestartStateWriteResult(
                    false,
                    new InvalidOperationException("HDR 注册表提交时间早于预写 marker 时间。"));
            }

            change.AppliedAtUtcTicks = appliedAtUtcTicks;
            return TryWrite(document);
        }
    }

    private GenshinHdrRestartStateLoadResult TryLoad()
    {
        try
        {
            var json = _stateReader(_statePath);
            if (json is null)
            {
                return GenshinHdrRestartStateLoadResult.Succeeded(new GenshinHdrRestartStateDocument());
            }

            var document = JsonConvert.DeserializeObject<GenshinHdrRestartStateDocument>(json)
                           ?? throw new JsonSerializationException("HDR 重启状态文件内容为空。 ");
            Validate(document);
            return GenshinHdrRestartStateLoadResult.Succeeded(document);
        }
        catch (Exception e)
        {
            // 读取失败不能降级为空状态，否则 BetterGI 重启后会错误放行尚未重启的游戏。
            return GenshinHdrRestartStateLoadResult.Failed(e);
        }
    }

    private GenshinHdrRestartStateWriteResult TryWrite(GenshinHdrRestartStateDocument document)
    {
        try
        {
            Validate(document);
            var json = JsonConvert.SerializeObject(document, Formatting.Indented);
            _stateWriter(_statePath, json);
            return new GenshinHdrRestartStateWriteResult(true);
        }
        catch (Exception e)
        {
            return new GenshinHdrRestartStateWriteResult(false, e);
        }
    }

    private static void Validate(GenshinHdrRestartStateDocument document)
    {
        if (document.FormatVersion != CurrentFormatVersion)
        {
            throw new JsonSerializationException(
                $"不支持的 HDR 重启状态文件版本：{document.FormatVersion}。 ");
        }

        document.Requirements ??= [];
        foreach (var requirement in document.Requirements)
        {
            if (requirement is null ||
                requirement.ProcessId == 0 ||
                requirement.ProcessStartTimeUtcTicks <= 0 ||
                requirement.ProcessStartTimeUtcTicks > DateTime.MaxValue.Ticks ||
                requirement.Edition == GenshinGameEdition.Unknown ||
                string.IsNullOrWhiteSpace(requirement.RegistryTarget))
            {
                throw new JsonSerializationException("HDR 重启状态文件包含无效记录。 ");
            }

            var expectedTarget = GenshinHdrRegistryHelper.GetHdrRegistryFullValuePath(requirement.Edition);
            if (!string.Equals(expectedTarget, requirement.RegistryTarget, StringComparison.OrdinalIgnoreCase))
            {
                throw new JsonSerializationException("HDR 重启状态记录的游戏版本与注册表目标不匹配。 ");
            }
        }

        document.RegistryChanges ??= [];
        foreach (var change in document.RegistryChanges)
        {
            var appliedAtIsInvalid = change?.AppliedAtUtcTicks is { } appliedAtUtcTicks &&
                                     (appliedAtUtcTicks <= 0 ||
                                      appliedAtUtcTicks > DateTime.MaxValue.Ticks ||
                                      appliedAtUtcTicks < change.PreparedAtUtcTicks);
            if (change is null ||
                change.PreparedAtUtcTicks <= 0 ||
                change.PreparedAtUtcTicks > DateTime.MaxValue.Ticks ||
                appliedAtIsInvalid ||
                change.Edition == GenshinGameEdition.Unknown ||
                string.IsNullOrWhiteSpace(change.RegistryTarget))
            {
                throw new JsonSerializationException("HDR 重启状态文件包含无效的版本变更记录。 ");
            }

            var expectedTarget = GenshinHdrRegistryHelper.GetHdrRegistryFullValuePath(change.Edition);
            if (!string.Equals(expectedTarget, change.RegistryTarget, StringComparison.OrdinalIgnoreCase))
            {
                throw new JsonSerializationException("HDR 版本变更记录与注册表目标不匹配。 ");
            }
        }
    }

    private static GenshinProcessIdentityReadResult ReadProcessIdentityCore(uint processId)
    {
        if (processId == 0 || processId > int.MaxValue)
        {
            return GenshinProcessIdentityReadResult.NotFound();
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            if (process.HasExited)
            {
                return GenshinProcessIdentityReadResult.NotFound();
            }

            return GenshinProcessIdentityReadResult.Found(
                new GenshinGameProcessIdentity(
                    processId,
                    process.StartTime.ToUniversalTime().Ticks));
        }
        catch (ArgumentException)
        {
            return GenshinProcessIdentityReadResult.NotFound();
        }
        catch (InvalidOperationException)
        {
            return GenshinProcessIdentityReadResult.NotFound();
        }
        catch (Exception e)
        {
            return GenshinProcessIdentityReadResult.Unavailable(e);
        }
    }

    private static string? ReadStateText(string statePath)
    {
        try
        {
            return File.ReadAllText(statePath, Utf8WithoutBom);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static void WriteStateTextAtomically(string statePath, string json)
    {
        var directory = Path.GetDirectoryName(statePath)
                        ?? throw new InvalidOperationException("HDR 重启状态文件缺少父目录。 ");
        Directory.CreateDirectory(directory);

        // 临时文件必须与目标文件位于同一目录，确保最终替换不会跨卷并尽可能保持原子性。
        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(statePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, Utf8WithoutBom, 4096, leaveOpen: true))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            // 同卷覆盖重命名避免目标文件短暂消失，也比 File.Replace 更兼容非 NTFS 的便携目录。
            File.Move(tempPath, statePath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // 临时文件清理失败不覆盖原始持久化异常；下次写入使用新的 GUID 文件名。
            }
        }
    }

    private FileStream AcquireStateFileLock()
    {
        return AcquireExclusiveFileLock($"{_statePath}.lock");
    }

    private FileStream AcquireExclusiveFileLock(string lockPath)
    {
        var directory = Path.GetDirectoryName(lockPath)
                        ?? throw new InvalidOperationException("HDR 重启状态文件缺少父目录。 ");
        Directory.CreateDirectory(directory);
        try
        {
            // policy.lock 已串行化合规运行时；内部状态锁若仍冲突应立即 fail closed，不能再阻塞 UI。
            return new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.None);
        }
        catch (IOException e)
        {
            throw new IOException("HDR 重启状态文件正被另一进程占用。", e);
        }
    }

    private static async Task<FileStream> AcquireExclusiveFileLockAsync(
        string lockPath,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(lockPath)
                        ?? throw new InvalidOperationException("HDR 重启状态文件缺少父目录。 ");
        Directory.CreateDirectory(directory);
        var stopwatch = Stopwatch.StartNew();
        IOException? lastError = null;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.None);
            }
            catch (IOException e)
            {
                lastError = e;
                if (stopwatch.Elapsed >= StateLockTimeout)
                {
                    break;
                }

                // 策略锁由 WPF 命令等待，必须异步让出 UI 线程，不能沿用状态锁的 Thread.Sleep。
                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            }
        }
        while (stopwatch.Elapsed < StateLockTimeout);

        throw new IOException("等待 HDR 跨进程策略锁超时。", lastError);
    }

    private sealed class GenshinHdrRestartStateDocument
    {
        public int FormatVersion { get; set; } = CurrentFormatVersion;

        public List<GenshinHdrRestartRequirement> Requirements { get; set; } = [];

        public List<GenshinHdrRegistryChange> RegistryChanges { get; set; } = [];
    }

    private sealed class GenshinHdrRestartRequirement
    {
        public uint ProcessId { get; set; }

        public long ProcessStartTimeUtcTicks { get; set; }

        public GenshinGameEdition Edition { get; set; }

        public string RegistryTarget { get; set; } = string.Empty;
    }

    private sealed class GenshinHdrRegistryChange
    {
        public GenshinGameEdition Edition { get; set; }

        public string RegistryTarget { get; set; } = string.Empty;

        public long PreparedAtUtcTicks { get; set; }

        public long? AppliedAtUtcTicks { get; set; }
    }

    private readonly record struct GenshinHdrRestartStateLoadResult(
        bool Success,
        GenshinHdrRestartStateDocument? Document,
        Exception? Error)
    {
        public static GenshinHdrRestartStateLoadResult Succeeded(GenshinHdrRestartStateDocument document) =>
            new(true, document, null);

        public static GenshinHdrRestartStateLoadResult Failed(Exception error) =>
            new(false, null, error);
    }
}

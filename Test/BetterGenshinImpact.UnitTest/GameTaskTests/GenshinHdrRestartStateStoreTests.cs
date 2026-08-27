using BetterGenshinImpact.GameTask;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.GameTaskTests;

public sealed class GenshinHdrRestartStateStoreTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        "BetterGI.UnitTest",
        Guid.NewGuid().ToString("N"));

    private string StatePath => Path.Combine(_testDirectory, "genshin-hdr-restart.json");

    [Fact]
    public void DefaultStatePath_UsesStablePerUserLocalAppData()
    {
        var localAppData = Path.GetFullPath(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        var statePath = Path.GetFullPath(GenshinHdrRestartStateStore.DefaultStatePath);

        Assert.StartsWith(localAppData, statePath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Path.Combine("BetterGI", "State"), statePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Mark_ThenCreateNewStore_ExactProcessStillRequiresRestart()
    {
        var identity = new GenshinGameProcessIdentity(1234, 638603424000000000L);
        var target = GetTarget(GenshinGameEdition.Cn);
        var processes = CreateProcessTable(identity);

        var firstStore = CreateStore(processes);
        var writeResult = firstStore.TryMarkRestartRequired(identity, GenshinGameEdition.Cn, target);

        Assert.True(writeResult.Success, writeResult.Error?.ToString());
        var restartedBetterGiStore = CreateStore(processes);
        var checkResult = restartedBetterGiStore.CheckAndPrune(
            identity.ProcessId,
            GenshinGameEdition.Cn,
            target);
        Assert.Equal(GenshinHdrRestartCheckStatus.RestartRequired, checkResult.Status);
    }

    [Fact]
    public void Check_SamePidWithDifferentStartTime_PrunesReusedPid()
    {
        var oldIdentity = new GenshinGameProcessIdentity(2234, DateTime.UtcNow.AddMinutes(-2).Ticks);
        var target = GetTarget(GenshinGameEdition.Cn);

        var firstStore = CreateStore(CreateProcessTable(oldIdentity));
        Assert.True(firstStore.TryMarkRestartRequired(oldIdentity, GenshinGameEdition.Cn, target).Success);
        var appliedAt = DateTime.UtcNow.Ticks;
        Assert.True(firstStore.TryCompleteRegistryChange(GenshinGameEdition.Cn, target, appliedAt).Success);
        var newIdentity = oldIdentity with { StartTimeUtcTicks = appliedAt + 1 };

        var newProcessTable = CreateProcessTable(newIdentity);
        var checkResult = CreateStore(newProcessTable).CheckAndPrune(
            newIdentity.ProcessId,
            GenshinGameEdition.Cn,
            target);
        Assert.Equal(GenshinHdrRestartCheckStatus.NotRequired, checkResult.Status);

        // 重新加载文件，确认 PID 复用记录已经从磁盘状态中清理。
        var persistedCheck = CreateStore(newProcessTable).CheckAndPrune(
            newIdentity.ProcessId,
            GenshinGameEdition.Cn,
            target);
        Assert.Equal(GenshinHdrRestartCheckStatus.NotRequired, persistedCheck.Status);
        Assert.Empty(ReadRequirements());
    }

    [Fact]
    public void Check_ExitedProcess_PrunesPersistedRequirement()
    {
        var identity = new GenshinGameProcessIdentity(3234, 638603424000000000L);
        var target = GetTarget(GenshinGameEdition.Global);
        var firstStore = CreateStore(CreateProcessTable(identity));
        Assert.True(firstStore.TryMarkRestartRequired(identity, GenshinGameEdition.Global, target).Success);

        var noProcesses = new Dictionary<uint, GenshinProcessIdentityReadResult>();
        var result = CreateStore(noProcesses).CheckAndPrune(
            currentProcessId: 0,
            GenshinGameEdition.Global,
            target);

        Assert.Equal(GenshinHdrRestartCheckStatus.NotRequired, result.Status);
        Assert.Empty(ReadRequirements());
    }

    [Fact]
    public void Check_ProcessInspectionDenied_KeepsRequirementAndBlocksSamePid()
    {
        var identity = new GenshinGameProcessIdentity(4234, 638603424000000000L);
        var target = GetTarget(GenshinGameEdition.Cn);
        var firstStore = CreateStore(CreateProcessTable(identity));
        Assert.True(firstStore.TryMarkRestartRequired(identity, GenshinGameEdition.Cn, target).Success);

        var denied = new Dictionary<uint, GenshinProcessIdentityReadResult>
        {
            [identity.ProcessId] = GenshinProcessIdentityReadResult.Unavailable(
                new UnauthorizedAccessException("denied")),
        };
        var result = CreateStore(denied).CheckAndPrune(
            identity.ProcessId,
            GenshinGameEdition.Cn,
            target);

        Assert.Equal(GenshinHdrRestartCheckStatus.RestartRequired, result.Status);
        Assert.Single(ReadRequirements());
    }

    [Fact]
    public void Check_DifferentEditionAndTarget_DoesNotConsumeOtherEditionMarker()
    {
        var identity = new GenshinGameProcessIdentity(5234, 638603424000000000L);
        var cnTarget = GetTarget(GenshinGameEdition.Cn);
        var globalTarget = GetTarget(GenshinGameEdition.Global);
        var processes = CreateProcessTable(identity);
        var store = CreateStore(processes);
        Assert.True(store.TryMarkRestartRequired(identity, GenshinGameEdition.Cn, cnTarget).Success);

        var globalCheck = CreateStore(processes).CheckAndPrune(
            identity.ProcessId,
            GenshinGameEdition.Global,
            globalTarget);
        var cnCheck = CreateStore(processes).CheckAndPrune(
            identity.ProcessId,
            GenshinGameEdition.Cn,
            cnTarget);

        Assert.Equal(GenshinHdrRestartCheckStatus.NotRequired, globalCheck.Status);
        Assert.Equal(GenshinHdrRestartCheckStatus.RestartRequired, cnCheck.Status);
    }

    [Fact]
    public void CompleteRegistryChange_BlocksOtherProcessStartedBeforeBarrier()
    {
        var firstProcess = new GenshinGameProcessIdentity(5334, DateTime.UtcNow.AddMinutes(-2).Ticks);
        var secondProcess = new GenshinGameProcessIdentity(5335, firstProcess.StartTimeUtcTicks + 100);
        var target = GetTarget(GenshinGameEdition.Cn);
        var processes = CreateProcessTable(firstProcess, secondProcess);
        var store = CreateStore(processes);

        Assert.True(store.TryMarkRestartRequired(firstProcess, GenshinGameEdition.Cn, target).Success);
        var appliedAt = DateTime.UtcNow.Ticks;
        Assert.True(store.TryCompleteRegistryChange(GenshinGameEdition.Cn, target, appliedAt).Success);

        var secondProcessCheck = CreateStore(processes).CheckAndPrune(
            secondProcess.ProcessId,
            GenshinGameEdition.Cn,
            target);
        Assert.Equal(GenshinHdrRestartCheckStatus.RestartRequired, secondProcessCheck.Status);
    }

    [Fact]
    public void CompleteRegistryChange_AllowsProcessStartedAfterBarrier()
    {
        var oldProcess = new GenshinGameProcessIdentity(5434, DateTime.UtcNow.AddMinutes(-2).Ticks);
        var target = GetTarget(GenshinGameEdition.Global);
        var firstStore = CreateStore(CreateProcessTable(oldProcess));

        Assert.True(firstStore.TryMarkRestartRequired(oldProcess, GenshinGameEdition.Global, target).Success);
        var appliedAt = DateTime.UtcNow.Ticks;
        Assert.True(firstStore.TryCompleteRegistryChange(GenshinGameEdition.Global, target, appliedAt).Success);
        var newProcess = new GenshinGameProcessIdentity(5435, appliedAt + 100);

        var newProcessCheck = CreateStore(CreateProcessTable(newProcess)).CheckAndPrune(
            newProcess.ProcessId,
            GenshinGameEdition.Global,
            target);
        Assert.Equal(GenshinHdrRestartCheckStatus.NotRequired, newProcessCheck.Status);
    }

    [Fact]
    public void PendingRegistryChange_BlocksProcessWithoutPidMarker()
    {
        var process = new GenshinGameProcessIdentity(5534, 638603424000000000L);
        var target = GetTarget(GenshinGameEdition.Cn);
        var store = CreateStore(CreateProcessTable(process));

        Assert.True(store.TryPrepareRegistryChange(GenshinGameEdition.Cn, target).Success);

        var check = CreateStore(CreateProcessTable(process)).CheckAndPrune(
            process.ProcessId,
            GenshinGameEdition.Cn,
            target);
        Assert.Equal(GenshinHdrRestartCheckStatus.RestartRequired, check.Status);
    }

    [Fact]
    public void PrepareRegistryChange_AfterAppliedBarrier_CreatesNewPendingGeneration()
    {
        var oldProcess = new GenshinGameProcessIdentity(5634, DateTime.UtcNow.AddMinutes(-3).Ticks);
        var target = GetTarget(GenshinGameEdition.Cn);
        var processTable = CreateProcessTable(oldProcess);
        var store = CreateStore(processTable);

        Assert.True(store.TryMarkRestartRequired(oldProcess, GenshinGameEdition.Cn, target).Success);
        var oldAppliedAt = DateTime.UtcNow.Ticks;
        Assert.True(store.TryCompleteRegistryChange(
            GenshinGameEdition.Cn,
            target,
            oldAppliedAt).Success);
        var processAfterOldBarrier = new GenshinGameProcessIdentity(5635, oldAppliedAt + 100);
        processTable[processAfterOldBarrier.ProcessId] =
            GenshinProcessIdentityReadResult.Found(processAfterOldBarrier);
        Assert.Equal(
            GenshinHdrRestartCheckStatus.NotRequired,
            store.CheckAndPrune(processAfterOldBarrier.ProcessId, GenshinGameEdition.Cn, target).Status);

        // 模拟用户重新开启 HDR 后再次准备写 0；旧屏障之后启动的进程也必须被新 Pending 拦截。
        Assert.True(store.TryPrepareRegistryChange(GenshinGameEdition.Cn, target).Success);
        Assert.Equal(
            GenshinHdrRestartCheckStatus.RestartRequired,
            store.CheckAndPrune(processAfterOldBarrier.ProcessId, GenshinGameEdition.Cn, target).Status);
    }

    [Fact]
    public void Check_StateReadDenied_ReturnsExplicitUnavailable()
    {
        var expected = new UnauthorizedAccessException("denied");
        var store = new GenshinHdrRestartStateStore(
            StatePath,
            processIdentityReader: _ => GenshinProcessIdentityReadResult.NotFound(),
            stateReader: _ => throw expected);

        var result = store.CheckAndPrune(
            currentProcessId: 0,
            GenshinGameEdition.Cn,
            GetTarget(GenshinGameEdition.Cn));

        Assert.Equal(GenshinHdrRestartCheckStatus.StateUnavailable, result.Status);
        Assert.Same(expected, result.Error);
    }

    [Fact]
    public void Check_CorruptedState_ReturnsExplicitUnavailable()
    {
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(StatePath, "{ invalid json");
        var store = CreateStore(new Dictionary<uint, GenshinProcessIdentityReadResult>());

        var result = store.CheckAndPrune(
            currentProcessId: 0,
            GenshinGameEdition.Cn,
            GetTarget(GenshinGameEdition.Cn));

        Assert.Equal(GenshinHdrRestartCheckStatus.StateUnavailable, result.Status);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Mark_StateWriteDenied_ReturnsExplicitFailure()
    {
        var identity = new GenshinGameProcessIdentity(6234, 638603424000000000L);
        var expected = new UnauthorizedAccessException("denied");
        var store = new GenshinHdrRestartStateStore(
            StatePath,
            processIdentityReader: _ => GenshinProcessIdentityReadResult.Found(identity),
            stateWriter: (_, _) => throw expected);

        var result = store.TryMarkRestartRequired(
            identity,
            GenshinGameEdition.Cn,
            GetTarget(GenshinGameEdition.Cn));

        Assert.False(result.Success);
        Assert.Same(expected, result.Error);
    }

    [Fact]
    public void Mark_EditionAndRegistryTargetMismatch_IsRejected()
    {
        var identity = new GenshinGameProcessIdentity(7234, 638603424000000000L);
        var store = CreateStore(CreateProcessTable(identity));

        var result = store.TryMarkRestartRequired(
            identity,
            GenshinGameEdition.Cn,
            GetTarget(GenshinGameEdition.Global));

        Assert.False(result.Success);
        Assert.IsType<ArgumentException>(result.Error);
    }

    [Fact]
    public async Task PolicyLock_SerializesIndependentStoreInstances()
    {
        var firstStore = CreateStore(new Dictionary<uint, GenshinProcessIdentityReadResult>());
        var secondStore = CreateStore(new Dictionary<uint, GenshinProcessIdentityReadResult>());
        var firstLock = firstStore.TryAcquirePolicyLock();
        Assert.True(firstLock.Success, firstLock.Error?.ToString());

        var secondLockTask = Task.Run(secondStore.TryAcquirePolicyLock);
        await Task.Delay(100);
        Assert.False(secondLockTask.IsCompleted);

        firstLock.LockHandle!.Dispose();
        var secondLock = await secondLockTask;
        Assert.True(secondLock.Success, secondLock.Error?.ToString());
        secondLock.LockHandle!.Dispose();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch
        {
            // 测试临时文件的清理失败不覆盖真正的断言结果。
        }
    }

    private GenshinHdrRestartStateStore CreateStore(
        IReadOnlyDictionary<uint, GenshinProcessIdentityReadResult> processTable)
    {
        return new GenshinHdrRestartStateStore(
            StatePath,
            processIdentityReader: processId => processTable.TryGetValue(processId, out var result)
                ? result
                : GenshinProcessIdentityReadResult.NotFound());
    }

    private static Dictionary<uint, GenshinProcessIdentityReadResult> CreateProcessTable(
        params GenshinGameProcessIdentity[] identities)
    {
        return identities.ToDictionary(
            identity => identity.ProcessId,
            GenshinProcessIdentityReadResult.Found);
    }

    private static string GetTarget(GenshinGameEdition edition)
    {
        return GenshinHdrRegistryHelper.GetHdrRegistryFullValuePath(edition)!;
    }

    private JArray ReadRequirements()
    {
        var document = JObject.Parse(File.ReadAllText(StatePath));
        return (JArray)document[nameof(GenshinHdrRestartStateDocumentProxy.Requirements)]!;
    }

    // 仅用于以 nameof 读取私有持久化模型的稳定 JSON 属性名。
    private sealed class GenshinHdrRestartStateDocumentProxy
    {
        public object? Requirements { get; set; }
    }
}

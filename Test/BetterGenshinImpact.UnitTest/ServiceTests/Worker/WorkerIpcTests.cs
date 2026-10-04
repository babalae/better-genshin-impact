using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.Service.Instance.MessageHandlers;
using BetterGenshinImpact.Service.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.ServiceTests.Worker;

/// <summary>
/// 跨用户 Worker 的可自动化验证部分：命令行、管道命名、授权策略与管道 ACL。
/// 需要在第二个 Windows 用户下运行 Worker、以及与真实游戏交互的场景无法在单机单用户环境自动断言。
/// </summary>
public class WorkerIpcTests
{
    private const string WorkerSid = "S-1-5-21-1-1-1-1001";
    private const string ControllerSid = "S-1-5-21-1-1-1-1002";
    private const string StrangerSid = "S-1-5-21-1-1-1-1003";

    [Fact]
    public void CommandLineParser_ShouldRecognizeHeadlessWorker()
    {
        var options = CommandLineOptions.Parse(
        [
            "BetterGI.exe",
            "--headless",
            "--controller-sid",
            $" {ControllerSid} "
        ]);

        Assert.True(options.Headless);
        Assert.Equal(ControllerSid, options.ControllerUserSid);
        // headless 不改变原有实例类型判定，也不会走 --instance 客户端分支
        Assert.Equal(BetterGiInstanceType.Primary, options.InstanceType);
        Assert.False(options.HasExplicitInstanceType);
        Assert.Equal(CommandLineAction.None, options.Action);
    }

    [Fact]
    public void CommandLineParser_ShouldKeepNormalStartupUnchanged()
    {
        var options = CommandLineOptions.Parse(["BetterGI.exe"]);

        Assert.False(options.Headless);
        Assert.Null(options.ControllerUserSid);
        Assert.Equal(BetterGiInstanceType.Primary, options.InstanceType);
        Assert.False(options.HasExplicitInstanceType);
        Assert.Equal(CommandLineAction.None, options.Action);
    }

    [Fact]
    public void CommandLineParser_ShouldIgnoreHeadlessControllerWithoutValue()
    {
        var options = CommandLineOptions.Parse(["BetterGI.exe", "--headless", "--controller-sid"]);

        Assert.True(options.Headless);
        Assert.Null(options.ControllerUserSid);
    }

    [Fact]
    public void WorkerPipeName_ShouldEmbedSidWithoutChangingRootPipe()
    {
        Assert.Equal(
            $"BetterGI.v2.cross-user.{WorkerSid}.worker",
            InstancePipeNames.WorkerForUserSid(WorkerSid));

        // 原有根管道命名保持不变
        Assert.Equal(
            $"BetterGI.v2.user-{WorkerSid}.root",
            InstancePipeNames.ForUserSid(WorkerSid));
    }

    /// <summary>
    /// capture.* 是 Worker 专属操作，不得与根实例 IPC 的既有操作重名
    /// </summary>
    [Fact]
    public void WorkerCaptureOperations_ShouldBeWorkerOnly()
    {
        Assert.Equal("capture.start", InstanceOperations.CaptureStart);
        Assert.Equal("capture.stop", InstanceOperations.CaptureStop);

        string[] rootOperations =
        [
            InstanceOperations.Ping,
            InstanceOperations.Response,
            InstanceOperations.ConnectionOpen,
            InstanceOperations.ActivationDispatch,
            InstanceOperations.RelativeMouseSubscribe,
            InstanceOperations.RelativeMouseUnsubscribe,
            InstanceOperations.RelativeMouseState,
            InstanceOperations.WebViewList,
            InstanceOperations.WebViewSend,
            InstanceOperations.WebViewMessage,
            InstanceOperations.WorkerStatus,
            InstanceOperations.TaskStart,
            InstanceOperations.TaskStop,
            InstanceOperations.TaskStatus,
            InstanceOperations.TaskPause,
            InstanceOperations.TaskResume,
            InstanceOperations.WorkerLogMode
        ];

        Assert.DoesNotContain(InstanceOperations.CaptureStart, rootOperations);
        Assert.DoesNotContain(InstanceOperations.CaptureStop, rootOperations);
        Assert.DoesNotContain(InstanceOperations.WorkerLog, rootOperations);
    }

    [Fact]
    public void WorkerStatus_ShouldRoundTripCaptureRunning()
    {
        var status = new WorkerStatusResponse
        {
            WorkerSid = WorkerSid,
            SessionId = 2,
            ProcessId = 4321,
            InstanceId = "Headless:S2:P4321:T1",
            State = WorkerState.RunningTask,
            CurrentTask = "scriptGroup:每日",
            CaptureRunning = true
        };

        var payload = JObject.FromObject(status, InstanceIpcProtocol.Serializer);
        var restored = payload.ToObject<WorkerStatusResponse>(InstanceIpcProtocol.Serializer);

        Assert.NotNull(restored);
        Assert.Equal(status.WorkerSid, restored!.WorkerSid);
        Assert.Equal(status.SessionId, restored.SessionId);
        Assert.Equal(status.ProcessId, restored.ProcessId);
        Assert.Equal(status.InstanceId, restored.InstanceId);
        Assert.Equal(status.State, restored.State);
        Assert.Equal(status.CurrentTask, restored.CurrentTask);
        Assert.True(restored.CaptureRunning);
    }

    /// <summary>
    /// 旧版 Worker 的状态载荷没有 CaptureRunning，反序列化后必须为 false（向后兼容）
    /// </summary>
    [Fact]
    public void WorkerStatus_ShouldDefaultCaptureRunningToFalse()
    {
        var payload = JObject.Parse(
            $$"""{"WorkerSid":"{{WorkerSid}}","State":2}""");

        var status = payload.ToObject<WorkerStatusResponse>(InstanceIpcProtocol.Serializer);

        Assert.NotNull(status);
        Assert.Equal(WorkerState.Idle, status!.State);
        Assert.False(status.CaptureRunning);
    }

    /// <summary>
    /// task.start 的类型常量必须互不相同（Controller 与 Worker 共用同一份常量）
    /// </summary>
    [Fact]
    public void WorkerTaskTypes_ShouldBeDistinct()
    {
        string[] types =
        [
            WorkerTaskTypes.ScriptGroup,
            WorkerTaskTypes.ScriptGroups,
            WorkerTaskTypes.TaskProgress,
            WorkerTaskTypes.OneDragon,
            WorkerTaskTypes.Solo,
            WorkerTaskTypes.ScriptFolder,
            WorkerTaskTypes.PathingFile,
            WorkerTaskTypes.StartGame
        ];

        Assert.All(types, type => Assert.False(string.IsNullOrWhiteSpace(type)));
        Assert.Equal(types.Length, types.Distinct(StringComparer.Ordinal).Count());

        // 兼容旧调用方保留的别名
        Assert.Equal(WorkerTaskTypes.ScriptGroup, WorkerTaskExecutor.ScriptGroupTaskType);
        Assert.Equal(WorkerTaskTypes.StartGame, WorkerTaskExecutor.StartGameTaskType);
    }

    /// <summary>
    /// 独立任务标识由两侧共用，重复或改名都会导致下发到 Worker 后无法识别
    /// </summary>
    [Fact]
    public void WorkerSoloTaskKeys_ShouldBeDistinct()
    {
        var keys = typeof(WorkerSoloTaskKeys)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string?)field.GetRawConstantValue())
            .ToArray();

        Assert.NotEmpty(keys);
        Assert.DoesNotContain(keys, key => string.IsNullOrWhiteSpace(key));
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// 扩展后的 task.start 请求必须能原样往返（连续执行 / 继续执行 / 地图追踪都依赖这些字段）
    /// </summary>
    [Fact]
    public void WorkerTaskStartRequest_ShouldRoundTripExtendedFields()
    {
        var request = new WorkerTaskStartRequest
        {
            Type = WorkerTaskTypes.ScriptGroups,
            Names = ["每日", "周本"],
            Loop = true,
            ProgressName = "latest",
            Directory = @"User\Pathing"
        };

        var payload = JObject.FromObject(request, InstanceIpcProtocol.Serializer);
        var restored = payload.ToObject<WorkerTaskStartRequest>(InstanceIpcProtocol.Serializer);

        Assert.NotNull(restored);
        Assert.Equal(request.Type, restored!.Type);
        Assert.Equal(request.Names, restored.Names);
        Assert.True(restored.Loop);
        Assert.Equal(request.ProgressName, restored.ProgressName);
        Assert.Equal(request.Directory, restored.Directory);
    }

    [Theory]
    [InlineData(WorkerSid, true)]
    [InlineData(ControllerSid, true)]
    public void Authorization_ShouldAllowWorkerAndController(string clientSid, bool expected)
    {
        Assert.Equal(expected, WorkerAuthorization.IsControllerAllowed(WorkerSid, ControllerSid, clientSid));
    }

    [Fact]
    public void Authorization_ShouldRejectOtherUsersAndUnknownSid()
    {
        // 未配置 Controller 时只允许 Worker 自身用户
        Assert.False(WorkerAuthorization.IsControllerAllowed(WorkerSid, null, StrangerSid));
        // 配置了 Controller，但连接方是第三方用户
        Assert.False(WorkerAuthorization.IsControllerAllowed(WorkerSid, ControllerSid, StrangerSid));
        // 内核取不到客户端 SID（空值）必须按拒绝处理
        Assert.False(WorkerAuthorization.IsControllerAllowed(WorkerSid, ControllerSid, string.Empty));
        Assert.False(WorkerAuthorization.IsControllerAllowed(WorkerSid, ControllerSid, "   "));
    }

    [Fact]
    public void Authorization_ShouldBeCaseInsensitive()
    {
        Assert.True(WorkerAuthorization.IsControllerAllowed(
            WorkerSid.ToUpperInvariant(),
            ControllerSid,
            WorkerSid.ToLowerInvariant()));
        Assert.True(WorkerAuthorization.IsControllerAllowed(
            WorkerSid,
            ControllerSid.ToUpperInvariant(),
            ControllerSid.ToLowerInvariant()));
    }

    [Fact]
    public void WorkerPipeAcl_ShouldAllowOnlyExplicitSids()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var workerSid = identity.User!;
        var controllerSid = new SecurityIdentifier(ControllerSid);
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var networkSid = new SecurityIdentifier(WellKnownSidType.NetworkSid, null);
        var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var authenticatedUsersSid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);

        var security = InstancePipeFactory.CreatePipeSecurity(
            workerSid,
            [workerSid, systemSid, controllerSid]);
        var rules = security
            .GetAccessRules(includeExplicit: true, includeInherited: true, targetType: typeof(SecurityIdentifier))
            .Cast<PipeAccessRule>()
            .ToArray();

        var allowed = rules
            .Where(rule => rule.AccessControlType == AccessControlType.Allow)
            .Select(rule => rule.IdentityReference.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 恰好三项显式允许：Worker 用户、SYSTEM、配置的 Controller
        Assert.Equal(3, allowed.Count);
        Assert.Contains(workerSid.Value, allowed);
        Assert.Contains(systemSid.Value, allowed);
        Assert.Contains(controllerSid.Value, allowed);

        // 明确不包含 Everyone / Authenticated Users
        Assert.DoesNotContain(everyoneSid.Value, allowed);
        Assert.DoesNotContain(authenticatedUsersSid.Value, allowed);

        // 继续拒绝 Network SID，且 DACL 受保护（不继承）
        var denied = rules
            .Where(rule => rule.AccessControlType == AccessControlType.Deny)
            .Select(rule => rule.IdentityReference.Value)
            .ToArray();
        Assert.Contains(networkSid.Value, denied);
        Assert.True(security.AreAccessRulesProtected);
    }

    /// <summary>
    /// 证明构建出的安全描述符能被系统接受，且 Worker 自身用户可以正常连接（跨用户拒绝由 ACL + SID 校验共同保证）。
    /// </summary>
    [Fact]
    public async Task WorkerPipe_ShouldAcceptCurrentUserConnection()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var workerSid = identity.User!;
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var pipeName = $"BetterGI.UnitTest.pipe.{Guid.NewGuid():N}";

        using var server = InstancePipeFactory.CreateServer(
            pipeName,
            firstPipeInstance: true,
            workerSid,
            [workerSid, systemSid]);

        var waitForConnection = server.WaitForConnectionAsync();
        using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        await waitForConnection;

        Assert.True(server.IsConnected);
        Assert.True(client.IsConnected);
    }

    [Fact]
    public void WorkerNotice_ShouldRoundTrip()
    {
        Assert.Equal("worker.notice", InstanceOperations.WorkerNotice);

        var notice = new WorkerNotice
        {
            Sequence = 7,
            Level = WorkerNoticeLevel.Warning,
            Message = "请先在启动页，启动截图器再使用本功能",
            Timestamp = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.FromHours(8))
        };

        var payload = JObject.FromObject(notice, InstanceIpcProtocol.Serializer);
        var restored = payload.ToObject<WorkerNotice>(InstanceIpcProtocol.Serializer);

        Assert.NotNull(restored);
        Assert.Equal(notice.Sequence, restored!.Sequence);
        Assert.Equal(notice.Level, restored.Level);
        Assert.Equal(notice.Message, restored.Message);
        Assert.Equal(notice.Timestamp, restored.Timestamp);
    }

    /// <summary>
    /// Controller 未连接时（例如 Worker 单独运行）提示必须被静默丢弃，不能把异常抛给调用 Toast 的业务代码
    /// </summary>
    [Fact]
    public void WorkerNoticeHub_ShouldDropSilentlyWithoutController()
    {
        var hub = new WorkerNoticeHub(NullLogger<WorkerNoticeHub>.Instance);

        Assert.False(hub.HasControllers);
        hub.Publish(WorkerNoticeLevel.Error, "Worker 侧错误");
        hub.Publish(WorkerNoticeLevel.Information, "  ");
        hub.Publish(WorkerNoticeLevel.Information, string.Empty);
    }

    /// <summary>
    /// 端到端验证提示回传：真实命名管道 + 真实 InstanceConnection。
    /// Worker 侧 Publish 后，Controller 侧必须通过 worker.notice 收到同样内容。
    /// （跨用户 SID 校验依赖两个真实 Windows 用户，无法在此自动断言）
    /// </summary>
    [Fact]
    public async Task WorkerNotice_ShouldReachControllerOverPipe()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var workerSid = identity.User!;
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var pipeName = $"BetterGI.UnitTest.notice.{Guid.NewGuid():N}";

        using var serverStream = InstancePipeFactory.CreateServer(
            pipeName,
            firstPipeInstance: true,
            workerSid,
            [workerSid, systemSid]);

        using var clientStream = new NamedPipeClientStream(
            ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        var waitForConnection = serverStream.WaitForConnectionAsync();
        await clientStream.ConnectAsync(5000);
        await waitForConnection;

        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var received = new TaskCompletionSource<WorkerNotice?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var receivedLog = new TaskCompletionSource<WorkerLogBatch?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var controllerOwner = new TestConnectionOwner(request =>
        {
            if (request.Operation == InstanceOperations.WorkerNotice)
            {
                received.TrySetResult(
                    request.Data?.ToObject<WorkerNotice>(InstanceIpcProtocol.Serializer));
            }
            else if (request.Operation == InstanceOperations.WorkerLog)
            {
                receivedLog.TrySetResult(
                    request.Data?.ToObject<WorkerLogBatch>(InstanceIpcProtocol.Serializer));
            }

            return Task.FromResult<InstanceIpcEnvelope?>(null);
        });

        await using var workerConnection =
            new InstanceConnection(serverStream, new TestConnectionOwner(), NullLogger.Instance);
        await using var controllerConnection =
            new InstanceConnection(clientStream, controllerOwner, NullLogger.Instance);

        var hub = new WorkerNoticeHub(NullLogger<WorkerNoticeHub>.Instance);
        hub.Register(workerConnection);
        Assert.True(hub.HasControllers);

        workerConnection.Start(lifetime.Token);
        controllerConnection.Start(lifetime.Token);

        hub.Publish(WorkerNoticeLevel.Warning, "请先在启动页，启动截图器再使用本功能");

        var notice = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(notice);
        Assert.Equal(WorkerNoticeLevel.Warning, notice!.Level);
        Assert.Equal("请先在启动页，启动截图器再使用本功能", notice.Message);
        Assert.True(notice.Sequence > 0);

        // 同一连接上还要能回传日志批次（显示位置为「本地独立窗口」时）
        await hub.PublishLogBatchAsync(["[12:00:00 INF] 第一行", "[12:00:01 WRN] 第二行"]);
        var batch = await receivedLog.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(batch);
        Assert.Equal(2, batch!.Lines.Length);
        Assert.Equal("[12:00:01 WRN] 第二行", batch.Lines[1]);

        hub.Unregister(workerConnection);
        Assert.False(hub.HasControllers);
    }

    /// <summary>
    /// 走真实 <see cref="WorkerController"/> 连接一个用生产管道 API 搭起来的假 Worker，
    /// 验证日志显示位置下发与日志回传的完整契约。
    /// </summary>
    [Fact]
    public async Task WorkerController_ShouldPushLogModeAndReceiveLogBatches()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var workerSid = identity.User!;
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var pipeName = InstancePipeNames.WorkerForUserSid(workerSid.Value);

        using var serverStream = InstancePipeFactory.CreateServer(
            pipeName,
            firstPipeInstance: true,
            workerSid,
            [workerSid, systemSid]);

        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var modeRequest = new TaskCompletionSource<WorkerLogModeRequest?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var serverConnection = new TaskCompletionSource<InstanceConnection>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var serverOwner = new TestConnectionOwner(request =>
        {
            switch (request.Operation)
            {
                case InstanceOperations.ConnectionOpen:
                    return Task.FromResult<InstanceIpcEnvelope?>(InstanceIpcEnvelope.Response(
                        request,
                        new ConnectionOpenResponse
                        {
                            Disposition = ConnectionOpenDisposition.Accepted,
                            AssignedType = BetterGiInstanceType.Primary,
                            RootProcessId = Environment.ProcessId,
                            RootSessionId = 0
                        }));

                case InstanceOperations.WorkerStatus:
                    return Task.FromResult<InstanceIpcEnvelope?>(InstanceIpcEnvelope.Response(
                        request,
                        new WorkerStatusResponse
                        {
                            WorkerSid = workerSid.Value,
                            ProcessId = Environment.ProcessId,
                            State = WorkerState.Idle,
                            CaptureRunning = true
                        }));

                case InstanceOperations.WorkerLogMode:
                    modeRequest.TrySetResult(
                        request.Data?.ToObject<WorkerLogModeRequest>(InstanceIpcProtocol.Serializer));
                    return Task.FromResult<InstanceIpcEnvelope?>(InstanceIpcEnvelope.Response(
                        request,
                        new WorkerLogModeResponse
                        {
                            Mode = WorkerLogDisplayMode.LocalWindow,
                            NotificationIntervalSeconds = 7
                        }));

                default:
                    return Task.FromResult<InstanceIpcEnvelope?>(null);
            }
        });

        var acceptTask = Task.Run(async () =>
        {
            await serverStream.WaitForConnectionAsync(lifetime.Token);
            var connection = new InstanceConnection(serverStream, serverOwner, NullLogger.Instance);
            connection.Start(lifetime.Token);
            serverConnection.TrySetResult(connection);
            return connection;
        });

        await using var controller = new WorkerController(NullLogger<WorkerController>.Instance);
        var logs = new List<string>();
        var logArrived = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        controller.LogReceived += (_, batch) =>
        {
            lock (logs)
            {
                logs.AddRange(batch.Lines);
            }

            logArrived.TrySetResult();
        };

        var status = await controller.ConnectAsync(workerSid.Value, lifetime.Token);
        Assert.Equal(WorkerState.Idle, status.State);
        Assert.True(controller.IsConnected);

        var modeResponse = await controller.SetLogDisplayModeAsync(WorkerLogDisplayMode.LocalWindow, 7);
        Assert.Equal(WorkerLogDisplayMode.LocalWindow, modeResponse.Mode);
        Assert.Equal(7, modeResponse.NotificationIntervalSeconds);

        var forwarded = await modeRequest.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(forwarded);
        Assert.Equal(WorkerLogDisplayMode.LocalWindow, forwarded!.Mode);
        Assert.Equal(7, forwarded.NotificationIntervalSeconds);

        // Worker 按设置回传日志批次，控制器必须原样交给订阅方
        var connection = await serverConnection.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await connection.WriteJsonAsync(
            InstanceIpcEnvelope.Request(
                InstanceOperations.WorkerLog,
                new WorkerLogBatch
                {
                    Sequence = 1,
                    Lines = ["[12:00:00 INF] 第一行", "[12:00:01 WRN] 第二行"]
                }),
            CancellationToken.None);

        await logArrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        lock (logs)
        {
            Assert.Equal(2, logs.Count);
            Assert.Equal("[12:00:01 WRN] 第二行", logs[1]);
        }

        await controller.DisconnectAsync();
        await (await acceptTask).DisposeAsync();
    }

    private sealed class TestConnectionOwner(
        Func<InstanceIpcEnvelope, Task<InstanceIpcEnvelope?>>? onRequest = null)
        : IInstanceConnectionOwner
    {
        public bool IsGameMouseModeEnabled => false;

        public Task<InstanceIpcEnvelope?> HandleRequestAsync(
            InstanceConnection connection,
            InstanceIpcEnvelope request,
            CancellationToken cancellationToken)
        {
            return onRequest?.Invoke(request) ?? Task.FromResult<InstanceIpcEnvelope?>(null);
        }

        public bool ReceiveRelativeMouseBatch(
            InstanceConnection connection,
            ulong firstSequence,
            IReadOnlyList<RelativeMouseSample> samples)
        {
            return false;
        }

        public void ReceiveRelativeMouseResult(
            InstanceConnection connection,
            RelativeMouseResult result)
        {
        }

        public void ConnectionClosed(InstanceConnection connection)
        {
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.Service.Worker;
using Newtonsoft.Json.Linq;
using Serilog.Events;
using Serilog.Parsing;

namespace BetterGenshinImpact.UnitTest.ServiceTests.Worker;

/// <summary>
/// Worker 日志显示位置的可自动化验证部分：选项顺序、路由映射、批次聚合与提示文本裁剪。
/// 真正的窗口显示与通知渠道投递依赖 UI / 双用户环境，无法在此断言。
/// </summary>
public class WorkerLogDisplayTests
{
    [Fact]
    public void WorkerLogDisplayMode_ShouldMatchHomePageOptionOrder()
    {
        // 启动页下拉框按固定顺序列 5 个选项，SelectedIndex 直接映射到枚举值，
        // 因此新增选项只能追加在末尾，且必须是与界面对应的同一顺序
        Assert.Equal(0, (int)WorkerLogDisplayMode.None);
        Assert.Equal(1, (int)WorkerLogDisplayMode.Notification);
        Assert.Equal(2, (int)WorkerLogDisplayMode.GameOverlay);
        Assert.Equal(3, (int)WorkerLogDisplayMode.RemoteWindow);
        Assert.Equal(4, (int)WorkerLogDisplayMode.LocalWindow);

        Assert.Equal(
            [
                WorkerLogDisplayMode.None,
                WorkerLogDisplayMode.Notification,
                WorkerLogDisplayMode.GameOverlay,
                WorkerLogDisplayMode.RemoteWindow,
                WorkerLogDisplayMode.LocalWindow
            ],
            Enum.GetValues<WorkerLogDisplayMode>());
    }

    [Theory]
    [InlineData(WorkerLogDisplayMode.Notification, WorkerLogDestination.NotificationChannel)]
    [InlineData(WorkerLogDisplayMode.RemoteWindow, WorkerLogDestination.WorkerWindow)]
    [InlineData(WorkerLogDisplayMode.LocalWindow, WorkerLogDestination.ControllerWindow)]
    public void ResolveDestination_ShouldMapSelectedMode(WorkerLogDisplayMode mode, WorkerLogDestination expected)
    {
        Assert.Equal(expected, WorkerLogRouter.ResolveDestination(mode));
    }

    [Theory]
    [InlineData(WorkerLogDisplayMode.None)]
    [InlineData(WorkerLogDisplayMode.GameOverlay)]
    public void ResolveDestination_ShouldSkipModesHandledElsewhere(WorkerLogDisplayMode mode)
    {
        // 「不显示」没有任何去向；「游戏内叠加层」由遮罩日志框 sink 负责，路由器不参与
        Assert.Null(WorkerLogRouter.ResolveDestination(mode));
    }

    [Fact]
    public void FormatLine_ShouldMatchMaskWindowOverlayFormat()
    {
        var logEvent = new LogEvent(
            DateTimeOffset.Now,
            LogEventLevel.Information,
            null,
            new MessageTemplateParser().Parse("任务启动！"),
            []);

        Assert.Equal($"[{logEvent.Timestamp:HH:mm:ss} INF] 任务启动！", WorkerLogRouter.FormatLine(logEvent));
    }

    [Fact]
    public void ShouldWriteToGameOverlay_ShouldIgnoreModeForNonHeadlessInstance()
    {
        // 测试宿主不是 --headless：无论显示位置选什么，遮罩日志框都必须照旧工作
        WorkerLogRouter.Configure(WorkerLogDisplayMode.None, 5);
        Assert.True(WorkerLogRouter.ShouldWriteToGameOverlay);

        WorkerLogRouter.Configure(WorkerLogDisplayMode.GameOverlay, 5);
        Assert.True(WorkerLogRouter.ShouldWriteToGameOverlay);
    }

    [Fact]
    public void Configure_ShouldIgnoreUnknownMode()
    {
        WorkerLogRouter.Configure((WorkerLogDisplayMode)99, 5);
        Assert.Equal(WorkerLogDisplayMode.GameOverlay, WorkerLogRouter.Mode);

        WorkerLogRouter.Configure((WorkerLogDisplayMode)0, 5);
        Assert.Equal(WorkerLogDisplayMode.None, WorkerLogRouter.Mode);

        WorkerLogRouter.Configure(WorkerLogDisplayMode.GameOverlay, 0);
        Assert.Equal(WorkerLogRouter.DefaultNotificationIntervalSeconds, WorkerLogRouter.NotificationIntervalSeconds);
    }

    [Fact]
    public void WorkerLogMode_ShouldRoundTrip()
    {
        Assert.Equal("worker.logMode", InstanceOperations.WorkerLogMode);
        Assert.Equal("worker.log", InstanceOperations.WorkerLog);

        var request = new WorkerLogModeRequest
        {
            Mode = WorkerLogDisplayMode.LocalWindow,
            NotificationIntervalSeconds = 15
        };
        var restoredRequest = JObject
            .FromObject(request, InstanceIpcProtocol.Serializer)
            .ToObject<WorkerLogModeRequest>(InstanceIpcProtocol.Serializer);
        Assert.NotNull(restoredRequest);
        Assert.Equal(WorkerLogDisplayMode.LocalWindow, restoredRequest!.Mode);
        Assert.Equal(15, restoredRequest.NotificationIntervalSeconds);

        var response = new WorkerLogModeResponse
        {
            Mode = WorkerLogDisplayMode.Notification,
            NotificationIntervalSeconds = 5
        };
        var restoredResponse = JObject
            .FromObject(response, InstanceIpcProtocol.Serializer)
            .ToObject<WorkerLogModeResponse>(InstanceIpcProtocol.Serializer);
        Assert.NotNull(restoredResponse);
        Assert.Equal(WorkerLogDisplayMode.Notification, restoredResponse!.Mode);
        Assert.Equal(5, restoredResponse.NotificationIntervalSeconds);
    }

    [Fact]
    public void WorkerLogModeRequest_ShouldKeepMissingModeAsNull()
    {
        // 旧版 Controller 或手工构造的请求没有 mode：Worker 必须能识别并拒绝，而不是当成「不显示」
        var restored = JObject
            .Parse("""{"notificationIntervalSeconds":5}""")
            .ToObject<WorkerLogModeRequest>(InstanceIpcProtocol.Serializer);

        Assert.NotNull(restored);
        Assert.Null(restored!.Mode);
    }

    [Fact]
    public void WorkerLogBatch_ShouldRoundTrip()
    {
        var batch = new WorkerLogBatch
        {
            Sequence = 12,
            Lines = ["[12:00:00 INF] 第一行", "[12:00:01 WRN] 第二行"]
        };

        var restored = JObject
            .FromObject(batch, InstanceIpcProtocol.Serializer)
            .ToObject<WorkerLogBatch>(InstanceIpcProtocol.Serializer);

        Assert.NotNull(restored);
        Assert.Equal(12, restored!.Sequence);
        Assert.Equal(batch.Lines, restored.Lines);
    }

    [Fact]
    public async Task WorkerLogBatcher_ShouldFlushPendingLinesOnDemand()
    {
        var batches = new List<IReadOnlyList<string>>();
        // 间隔为 0：只按手动 Flush 发送，避免测试依赖定时器
        using var batcher = new WorkerLogBatcher(
            TimeSpan.Zero,
            100,
            lines =>
            {
                batches.Add(lines);
                return Task.CompletedTask;
            });

        batcher.Add("[12:00:00 INF] 第一行");
        batcher.Add("[12:00:01 INF] 第二行");
        await batcher.FlushAsync();

        var batch = Assert.Single(batches);
        Assert.Equal(2, batch.Count);
        Assert.Equal("[12:00:00 INF] 第一行", batch[0]);

        // 没有新内容时不应产生空批次
        await batcher.FlushAsync();
        Assert.Single(batches);
    }

    [Fact]
    public async Task WorkerLogBatcher_ShouldFlushWhenBatchIsFull()
    {
        var flushed = new TaskCompletionSource<IReadOnlyList<string>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var batcher = new WorkerLogBatcher(
            TimeSpan.Zero,
            3,
            lines =>
            {
                flushed.TrySetResult(lines);
                return Task.CompletedTask;
            });

        batcher.Add("1");
        batcher.Add("2");
        batcher.Add("3");

        var batch = await flushed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(3, batch.Count);
    }

    [Fact]
    public async Task WorkerLogBatcher_ShouldSwallowFlushFailures()
    {
        using var batcher = new WorkerLogBatcher(
            TimeSpan.Zero,
            100,
            _ => throw new InvalidOperationException("管道已断开"));

        batcher.Add("1");
        await batcher.FlushAsync();
    }

    [Fact]
    public void WorkerLogNotificationFormatter_ShouldKeepAllLinesWhenShort()
    {
        var message = WorkerLogNotificationFormatter.Format(["[12:00:00 INF] 第一行", "[12:00:01 INF] 第二行"]);

        Assert.NotNull(message);
        Assert.Contains("[12:00:00 INF] 第一行", message, StringComparison.Ordinal);
        Assert.Contains("[12:00:01 INF] 第二行", message, StringComparison.Ordinal);
        Assert.DoesNotContain("已省略", message, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkerLogNotificationFormatter_ShouldTrimOldestLinesWhenTooLong()
    {
        var lines = Enumerable
            .Range(0, 200)
            .Select(i => $"[12:00:{i:00} INF] 第 {i} 行")
            .ToArray();

        var message = WorkerLogNotificationFormatter.Format(lines);

        Assert.NotNull(message);
        Assert.True(message!.Length <= WorkerLogNotificationFormatter.MaxMessageLength + 64);
        Assert.Contains("已省略", message, StringComparison.Ordinal);
        // 保留最新的行，丢掉最早的行
        Assert.Contains(lines[^1], message, StringComparison.Ordinal);
        Assert.DoesNotContain(lines[0], message, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkerLogNotificationFormatter_ShouldTruncateSingleOverlongLine()
    {
        var message = WorkerLogNotificationFormatter.Format([new string('长', 5000)]);

        Assert.NotNull(message);
        Assert.EndsWith("……", message, StringComparison.Ordinal);
        // 行首 MaxMessageLength 个字符 + 省略号
        Assert.Equal(WorkerLogNotificationFormatter.MaxMessageLength + 2, message!.Length);
    }
}

using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using BetterGenshinImpact.Core.Monitor;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.Service.Instance.MessageHandlers;

namespace BetterGenshinImpact.UnitTest.ServiceTests.Instance;

public class InstanceIpcProtocolTests
{
    [Fact]
    public void CommandLineParser_ShouldSeparateInstanceMetadataAndActivation()
    {
        var options = CommandLineOptions.Parse(
        [
            "BetterGI.exe",
            "--instance",
            "childSession",
            "--restart-from-pid",
            "1234",
            "bettergi://start"
        ]);

        Assert.Equal(BetterGiInstanceType.ChildSession, options.InstanceType);
        Assert.True(options.HasExplicitInstanceType);
        Assert.Equal(1234, options.RestartFromProcessId);
        Assert.Equal(CommandLineAction.Start, options.Action);
    }

    [Fact]
    public void CommandLineParser_ShouldRecognizeWebViewWithoutTaskArguments()
    {
        var options = CommandLineOptions.Parse(
        [
            "BetterGI.exe",
            "--instance",
            "webview"
        ]);

        Assert.Equal(BetterGiInstanceType.WebView, options.InstanceType);
        Assert.True(options.HasExplicitInstanceType);
        Assert.Equal(CommandLineAction.None, options.Action);
    }

    [Fact]
    public void CommandLineParser_ShouldTolerateInvalidInstanceMetadata()
    {
        var options = CommandLineOptions.Parse(
        [
            "BetterGI.exe",
            "--instance",
            "unsupported",
            "--restart-from-pid",
            "invalid-process-id"
        ]);

        Assert.Equal(BetterGiInstanceType.Primary, options.InstanceType);
        Assert.False(options.HasExplicitInstanceType);
        Assert.Null(options.RestartFromProcessId);
        Assert.Equal(CommandLineAction.None, options.Action);
    }

    [Fact]
    public void CommandLineParser_ShouldRecognizeExplicitPrimaryInstance()
    {
        var options = CommandLineOptions.Parse(
        [
            "BetterGI.exe",
            "--instance",
            "primary"
        ]);

        Assert.Equal(BetterGiInstanceType.Primary, options.InstanceType);
        Assert.False(options.HasExplicitInstanceType);
        Assert.Equal(CommandLineAction.None, options.Action);
    }

    [Theory]
    [InlineData("bettergi://startOneDragon")]
    [InlineData("--startGroups")]
    [InlineData("--TaskProgress")]
    [InlineData("--instance", "childSession", "bettergi://startOneDragon")]
    public void ActivationForwarding_ShouldRejectManagedAutomation(params string[] arguments)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ActivationForwardingPolicy.ThrowIfManagedAutomation(
                ["BetterGI.exe", .. arguments]));

        Assert.Contains("已有 BetterGI 实例", exception.Message);
        Assert.Contains("无法转发托管自动化任务", exception.Message);
    }

    [Theory]
    [InlineData()]
    [InlineData("bettergi://start")]
    public void ActivationForwarding_ShouldAllowWindowActivation(params string[] arguments)
    {
        ActivationForwardingPolicy.ThrowIfManagedAutomation(
            ["BetterGI.exe", .. arguments]);
    }

    [Fact]
    public void RootPipeName_ShouldBeStableForWindowsUser()
    {
        const string userSid = "S-1-5-21-1000-2000-3000-4000";

        var pipeName = InstancePipeNames.ForUserSid(userSid);

        Assert.Equal($"BetterGI.v2.user-{userSid}.root", pipeName);
    }

    [Fact]
    public async Task Server_ShouldDeriveClientProcessAndSessionFromPipe()
    {
        var pipeName = $"BetterGI.UnitTest.{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        await using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        var waitForConnection = server.WaitForConnectionAsync();
        await client.ConnectAsync();
        await waitForConnection;

        var result = InstancePipePeerInfo.TryGetClientProcessAndSession(
            server.SafePipeHandle,
            out var processId,
            out var sessionId);

        Assert.True(result);
        Assert.Equal(Environment.ProcessId, processId);
        Assert.Equal(Process.GetCurrentProcess().SessionId, sessionId);
    }

    [Fact]
    public async Task JsonFrame_ShouldRoundTrip()
    {
        var request = InstanceIpcEnvelope.Request(InstanceOperations.Ping);
        await using var stream = new MemoryStream();

        await InstanceIpcProtocol.WriteJsonAsync(stream, request, CancellationToken.None);
        stream.Position = 0;
        var frame = await InstanceIpcProtocol.ReadFrameAsync(stream, CancellationToken.None);
        var result = InstanceIpcProtocol.ReadJson(frame!.Value);

        Assert.Equal(InstanceIpcProtocol.Version, result.Version);
        Assert.Equal(request.RequestId, result.RequestId);
        Assert.Equal(InstanceOperations.Ping, result.Operation);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task RelativeMouseBatch_ShouldRoundTripSignedDeltasAndTimestamps()
    {
        var timestamp = new DateTime(638900000000000000, DateTimeKind.Utc);
        RelativeMouseSample[] samples =
        [
            new(-12, 8, timestamp),
            new(3, -5, timestamp.AddTicks(320))
        ];
        await using var stream = new MemoryStream();

        await InstanceIpcProtocol.WriteRelativeMouseBatchAsync(
            stream,
            42,
            samples,
            CancellationToken.None);
        stream.Position = 0;
        var frame = await InstanceIpcProtocol.ReadFrameAsync(stream, CancellationToken.None);
        var result = InstanceIpcProtocol.ReadRelativeMouseBatch(frame!.Value);

        Assert.Equal(42UL, result.FirstSequence);
        Assert.Equal(samples, result.Samples);
    }

    [Theory]
    [InlineData(long.MaxValue, 1)]
    [InlineData(3155378975999999999L, 1)]
    [InlineData(0L, -1)]
    public void RelativeMouseBatch_ShouldRejectInvalidTimestamp(
        long baseTicks,
        int offsetMicroseconds)
    {
        var payload = new byte[30];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, 1);
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(10), baseTicks);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(26), offsetMicroseconds);
        var frame = new InstanceIpcFrame(
            InstanceIpcPayloadType.RelativeMouseBatch,
            payload);

        Assert.Throws<InvalidDataException>(
            () => InstanceIpcProtocol.ReadRelativeMouseBatch(frame));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RelativeMouseResult_ShouldRoundTripHandledState(bool handled)
    {
        var expected = new RelativeMouseResult(57, handled);
        await using var stream = new MemoryStream();

        await InstanceIpcProtocol.WriteRelativeMouseResultAsync(
            stream,
            expected,
            CancellationToken.None);
        stream.Position = 0;
        var frame = await InstanceIpcProtocol.ReadFrameAsync(stream, CancellationToken.None);
        var result = InstanceIpcProtocol.ReadRelativeMouseResult(frame!.Value);

        Assert.Equal(expected, result);
    }
}

using System.Buffers.Binary;
using BetterGenshinImpact.Core.Monitor;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Instance;

namespace BetterGenshinImpact.UnitTest.ServiceTests.Instance;

public class InstanceIpcProtocolTests
{
    [Fact]
    public void CommandLineParser_ShouldSeparateInstanceMetadataAndActivation()
    {
        const string instanceId = "A1B2C3D4";
        const string parentInstanceId = "89ABCDEF";
        var options = CommandLineOptions.Parse(
        [
            "BetterGI.exe",
            "--instance",
            "childSession",
            "--instance-id",
            instanceId,
            "--parent-instance",
            parentInstanceId,
            "--parent-pipe",
            "BetterGI.v1.session-1",
            "bettergi://start"
        ]);

        Assert.Equal(BetterGiInstanceType.ChildSession, options.InstanceType);
        Assert.Equal(instanceId.ToLowerInvariant(), options.RequestedInstanceId);
        Assert.Equal(parentInstanceId.ToLowerInvariant(), options.ParentInstanceId);
        Assert.Equal("BetterGI.v1.session-1", options.ParentPipeName);
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
            "--instance-id",
            "invalid-instance-id",
            "--parent-instance",
            "invalid-parent-id",
            "--parent-pipe"
        ]);

        Assert.True(options.IsPrimaryInstance);
        Assert.Null(options.RequestedInstanceId);
        Assert.Null(options.ParentInstanceId);
        Assert.Null(options.ParentPipeName);
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

        Assert.True(options.IsPrimaryInstance);
        Assert.Equal(CommandLineAction.None, options.Action);
    }

    [Fact]
    public void LaunchInfo_ShouldEmitInstanceTypeAndParentMetadata()
    {
        const string instanceId = "0123abcd";
        const string parentInstanceId = "89abcdef";
        var launchInfo = new InstanceLaunchInfo(
            instanceId,
            BetterGiInstanceType.WebView,
            parentInstanceId,
            "BetterGI.v1.session-9");

        var arguments = launchInfo.ToCommandLineArguments();

        Assert.Contains("--instance webview", arguments);
        Assert.Contains($"--instance-id {instanceId}", arguments);
        Assert.Contains($"--parent-instance {parentInstanceId}", arguments);
        Assert.Contains("--parent-pipe \"BetterGI.v1.session-9\"", arguments);
    }

    [Fact]
    public void InstanceId_ShouldUseFirstEightLowercaseUuidCharacters()
    {
        var instanceId = InstanceIds.Create();

        Assert.Matches("^[0-9a-f]{8}$", instanceId);
        Assert.Equal($"BetterGI.v1.instance-{instanceId}", InstancePipeNames.ForInstance(instanceId));
    }

    [Fact]
    public async Task JsonFrame_ShouldRoundTrip()
    {
        const string sourceInstanceId = "0123abcd";
        var request = InstanceIpcEnvelope.Request(
            InstanceOperations.Ping,
            sourceInstanceId);
        await using var stream = new MemoryStream();

        await InstanceIpcProtocol.WriteJsonAsync(stream, request, CancellationToken.None);
        stream.Position = 0;
        var frame = await InstanceIpcProtocol.ReadFrameAsync(stream, CancellationToken.None);
        var result = InstanceIpcProtocol.ReadJson(frame!.Value);

        Assert.Equal(InstanceIpcProtocol.Version, result.Version);
        Assert.Equal(request.RequestId, result.RequestId);
        Assert.Equal(InstanceOperations.Ping, result.Operation);
        Assert.Equal(sourceInstanceId, result.SourceInstanceId);
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

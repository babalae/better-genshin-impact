using BetterGenshinImpact.Core.Monitor;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Instance;

namespace BetterGenshinImpact.UnitTest.ServiceTests.Instance;

public class InstanceIpcProtocolTests
{
    [Fact]
    public void CommandLineParser_ShouldSeparateInstanceMetadataAndActivation()
    {
        var instanceId = Guid.NewGuid();
        var parentInstanceId = Guid.NewGuid();
        var options = CommandLineOptions.Parse(
        [
            "BetterGI.exe",
            "--instance",
            "childSession",
            "--instance-id",
            instanceId.ToString("D"),
            "--parent-instance",
            parentInstanceId.ToString("D"),
            "--parent-pipe",
            "BetterGI.v1.session-1",
            "bettergi://start"
        ]);

        Assert.Equal(BetterGiInstanceType.ChildSession, options.InstanceType);
        Assert.Equal(instanceId, options.RequestedInstanceId);
        Assert.Equal(parentInstanceId, options.ParentInstanceId);
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
    public void LaunchInfo_ShouldEmitInstanceTypeAndParentMetadata()
    {
        var instanceId = Guid.NewGuid();
        var parentInstanceId = Guid.NewGuid();
        var launchInfo = new InstanceLaunchInfo(
            instanceId,
            BetterGiInstanceType.WebView,
            parentInstanceId,
            "BetterGI.v1.session-9");

        var arguments = launchInfo.ToCommandLineArguments();

        Assert.Contains("--instance webview", arguments);
        Assert.Contains($"--instance-id {instanceId:D}", arguments);
        Assert.Contains($"--parent-instance {parentInstanceId:D}", arguments);
        Assert.Contains("--parent-pipe \"BetterGI.v1.session-9\"", arguments);
    }

    [Fact]
    public async Task JsonFrame_ShouldRoundTrip()
    {
        var sourceInstanceId = Guid.NewGuid();
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
}

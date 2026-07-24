using System;
using System.Diagnostics;
using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.Service.Instance;

public enum BetterGiInstanceType
{
    Primary,
    ChildSession,
    WebView
}

public sealed class InstanceContext
{
    internal InstanceContext(
        Guid instanceId,
        BetterGiInstanceType instanceType,
        string pipeName,
        Guid? parentInstanceId,
        string? parentPipeName)
    {
        InstanceId = instanceId;
        InstanceType = instanceType;
        PipeName = pipeName;
        ParentInstanceId = parentInstanceId;
        ParentPipeName = parentPipeName;
        ProcessId = Environment.ProcessId;
        WindowsSessionId = Process.GetCurrentProcess().SessionId;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public Guid InstanceId { get; }

    public BetterGiInstanceType InstanceType { get; }

    public string PipeName { get; }

    public Guid? ParentInstanceId { get; }

    public string? ParentPipeName { get; }

    public int ProcessId { get; }

    public int WindowsSessionId { get; }

    public DateTimeOffset StartedAt { get; }

    public bool CanCreateChildSession => InstanceType == BetterGiInstanceType.Primary;

    public bool CanCreateWebView => InstanceType is BetterGiInstanceType.Primary
        or BetterGiInstanceType.ChildSession;

    public InstanceDescriptor ToDescriptor()
    {
        return new InstanceDescriptor
        {
            InstanceId = InstanceId,
            InstanceType = InstanceType,
            ParentInstanceId = ParentInstanceId,
            PipeName = PipeName,
            ProcessId = ProcessId,
            WindowsSessionId = WindowsSessionId,
            StartedAt = StartedAt
        };
    }
}

public sealed class InstanceDescriptor
{
    public Guid InstanceId { get; init; }

    public BetterGiInstanceType InstanceType { get; init; }

    public Guid? ParentInstanceId { get; init; }

    public string PipeName { get; init; } = string.Empty;

    public int ProcessId { get; init; }

    public int WindowsSessionId { get; init; }

    public DateTimeOffset StartedAt { get; init; }
}

public sealed class InstanceTreeNode
{
    public InstanceDescriptor Instance { get; init; } = new();

    public InstanceTreeNode[] Children { get; init; } = [];
}

public sealed record InstanceLaunchInfo(
    Guid InstanceId,
    BetterGiInstanceType InstanceType,
    Guid ParentInstanceId,
    string ParentPipeName)
{
    public string ToCommandLineArguments()
    {
        var instanceType = InstanceType switch
        {
            BetterGiInstanceType.ChildSession => "childSession",
            BetterGiInstanceType.WebView => "webview",
            _ => throw new InvalidOperationException("Primary 实例不能作为子实例启动。")
        };
        return string.Join(
            " ",
            CommandLineOptions.InstanceArgument,
            instanceType,
            CommandLineOptions.InstanceIdArgument,
            InstanceId.ToString("D"),
            CommandLineOptions.ParentInstanceArgument,
            ParentInstanceId.ToString("D"),
            CommandLineOptions.ParentPipeArgument,
            Quote(ParentPipeName));
    }

    private static string Quote(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }
}

internal static class InstancePipeNames
{
    private const string Prefix = "BetterGI.v1.";

    internal static string ForSession(int windowsSessionId)
    {
        return $"{Prefix}session-{windowsSessionId}";
    }

    internal static string ForInstance(Guid instanceId)
    {
        return $"{Prefix}instance-{instanceId:N}";
    }
}

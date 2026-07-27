namespace BetterGenshinImpact.Service.Instance.MessageHandlers;

/// <summary>
/// 客户端连接固定根管道后提交的用途和启动信息。
/// </summary>
internal sealed class ConnectionOpenRequest
{
    public BetterGiInstanceType RequestedType { get; init; }

    public int? RestartFromProcessId { get; init; }

    public string[] Arguments { get; init; } = [];
}

internal enum ConnectionOpenDisposition
{
    Accepted,
    ActivationForwarded
}

internal sealed class ConnectionOpenResponse
{
    public ConnectionOpenDisposition Disposition { get; init; }

    public BetterGiInstanceType AssignedType { get; init; }

    public int RootProcessId { get; init; }

    public int RootSessionId { get; init; }
}

internal sealed class ActivationDispatchRequest
{
    public string[] Arguments { get; init; } = [];
}

internal sealed class WebViewListResponse
{
    public InstanceEndpoint[] Endpoints { get; init; } = [];
}

internal sealed class WebViewSendRequest
{
    public int TargetProcessId { get; init; }

    public string Operation { get; init; } = string.Empty;

    public Newtonsoft.Json.Linq.JToken? Data { get; init; }
}

internal sealed class WebViewMessage
{
    public int SourceProcessId { get; init; }

    public string Operation { get; init; } = string.Empty;

    public Newtonsoft.Json.Linq.JToken? Data { get; init; }
}

internal sealed class StartOneDragonTaskRequest
{
    public string RunId { get; init; } = string.Empty;

    public string ConfigName { get; init; } = string.Empty;

    public string ResultPath { get; init; } = string.Empty;
}

internal sealed class StartOneDragonTaskResponse
{
    public string RunId { get; init; } = string.Empty;

    public bool Accepted { get; init; }
}

/// <summary>
/// 相对鼠标转发订阅操作的响应状态。
/// </summary>
internal sealed class RelativeMouseState
{
    public bool IsSubscribed { get; init; }
}

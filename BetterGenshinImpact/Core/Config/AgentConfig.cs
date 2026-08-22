using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
/// BetterGI 内置 Agent 的 OpenAI-compatible 接口配置。
/// </summary>
public partial class AgentConfig : ObservableObject
{
    /// <summary>
    /// 外部 OpenAI-compatible 服务基础地址，支持填到服务根、/v1 或完整 chat/completions 地址。
    /// </summary>
    [ObservableProperty]
    private string _baseUrl = string.Empty;

    /// <summary>
    /// 外部服务 API Key。只随出站请求发送，不会暴露给 MCP 工具结果。
    /// </summary>
    [ObservableProperty]
    private string _apiKey = string.Empty;

    /// <summary>
    /// 可选模型 ID。留空时从外部服务 /models 自动选择第一个模型。
    /// </summary>
    [ObservableProperty]
    private string _model = string.Empty;

    /// <summary>
    /// 一次对话允许的最大本地工具调用轮数。
    /// </summary>
    [ObservableProperty]
    private int _maxToolRounds = 12;

    /// <summary>
    /// 触发 Agent Framework 自动摘要压缩的消息数量。
    /// </summary>
    [ObservableProperty]
    private int _compactionTriggerMessages = 48;

    /// <summary>
    /// 摘要或截断时至少保留的最近消息组数量。
    /// </summary>
    [ObservableProperty]
    private int _compactionPreserveRecentGroups = 12;

    /// <summary>
    /// 摘要失败时触发硬性历史截断的消息数量。
    /// </summary>
    [ObservableProperty]
    private int _hardTruncationMessages = 96;

    /// <summary>
    /// 磁盘可见对话最多保留的消息数。
    /// </summary>
    [ObservableProperty]
    private int _maxPersistedMessages = 80;

    /// <summary>
    /// 磁盘可见对话允许的总字符数。
    /// </summary>
    [ObservableProperty]
    private int _maxPersistedCharacters = 120000;

    /// <summary>
    /// 磁盘缓存中单条消息允许的最大字符数。
    /// </summary>
    [ObservableProperty]
    private int _maxPersistedMessageCharacters = 20000;

    /// <summary>
    /// 序列化 Agent Framework Session 文件允许的最大字符数；超限时不写入磁盘，重启后退回可见对话恢复。
    /// </summary>
    [ObservableProperty]
    private int _maxSerializedSessionCharacters = 500000;
}

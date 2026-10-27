using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;

namespace BetterGenshinImpact.Core.Config;

/// <summary>自动配音设置，由 AllConfig 统一持久化；更改在下次启动配音时生效。</summary>
public partial class QuestVoiceConfig : ObservableObject
{
    /// <summary>新增引擎的独立配置，沿用 AllConfig 序列化。</summary>
    [ObservableProperty] private Dictionary<string, QuestVoiceEngineConfig> _extraProviders = QuestVoiceEngineConfig.Defaults();
    /// <summary>缺省保留旧版 GPT-SoVITS；独立引擎配置互不覆盖。</summary>
    [ObservableProperty] private string _providerId = "gpt-sovits";
    /// <summary>使用远程实例；本地 ServiceUrl 和声线字段继续保留。</summary>
    [ObservableProperty] private bool _useRemoteService;
    /// <summary>远程 api_v2 地址，支持反向代理子路径。</summary>
    [ObservableProperty] private string _remoteServiceUrl = "";
    /// <summary>远程服务器可访问的 GPT 路径，原样传递，不上传文件。</summary>
    [ObservableProperty] private string _remoteGptWeightsPath = "";
    /// <summary>远程服务器可访问的 SoVITS 路径。</summary>
    [ObservableProperty] private string _remoteSovitsWeightsPath = "";
    /// <summary>远程服务器可访问的参考音频路径。</summary>
    [ObservableProperty] private string _remoteReferenceAudioPath = "";
    /// <summary>远程参考音频的逐字文本。</summary>
    [ObservableProperty] private string _remotePromptText = "";
    /// <summary>火山预设 voice ID，不修改现有 GPT 声线。</summary>
    [ObservableProperty] private string _huoshanVoiceId = "zh_female_qingxin";
    /// <summary>连接超时，秒。</summary>
    [ObservableProperty] private int _connectTimeoutSeconds = 10;
    /// <summary>探测/切权请求超时，秒。</summary>
    [ObservableProperty] private int _requestTimeoutSeconds = 30;
    /// <summary>合成/排空等待上限，秒；超时不意味着 GPT 后端已空闲。</summary>
    [ObservableProperty] private int _synthesisTimeoutSeconds = 180;
    /// <summary>本机 GPT-SoVITS api_v2 HTTP 服务。</summary>
    [ObservableProperty] private string _serviceUrl = "http://127.0.0.1:9880/";
    /// <summary>实验性按句流式配音；默认关闭，停止后修改、下次启动配音生效。</summary>
    [ObservableProperty] private bool _streamingEnabled;
    /// <summary>带 character 字典的 PP-OCRv6 Rec 模型，支持程序目录相对路径。</summary>
    [ObservableProperty] private string _ocrModelPath = "User/QuestVoice/ocr/PP-OCRv6_rec_small.onnx";
    /// <summary>专用 OCR 使用的 DirectML 设备序号，不修改全局 OCR。</summary>
    [ObservableProperty] private int _deviceId;
    /// <summary>默认 NPC 与未绑定角色的兜底声线名称。</summary>
    [ObservableProperty] private string _voiceName = "默认 NPC";
    /// <summary>GPT 权重路径，后端必须能够访问同一路径。</summary>
    [ObservableProperty] private string _gptWeightsPath = "";
    /// <summary>SoVITS 权重路径。</summary>
    [ObservableProperty] private string _sovitsWeightsPath = "";
    /// <summary>参考音频路径。</summary>
    [ObservableProperty] private string _referenceAudioPath = "";
    /// <summary>参考音频的逐字原文。</summary>
    [ObservableProperty] private string _promptText = "";
    /// <summary>是否读取外置角色目录，否则使用固定兜底声线。</summary>
    [ObservableProperty] private bool _routingEnabled;
    /// <summary>角色/旅行者/旁白/默认 NPC 映射目录路径。</summary>
    [ObservableProperty] private string _routingCatalogPath = "User/QuestVoice/routing.json";
    /// <summary>只在宿主已有截图中按此间隔取样，不创建捕获器。</summary>
    [ObservableProperty] private int _sampleIntervalMilliseconds = 150;
}

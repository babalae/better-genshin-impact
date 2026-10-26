using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Core.Config;

/// <summary>自动配音设置，由 AllConfig 统一持久化；更改在下次启动配音时生效。</summary>
public partial class QuestVoiceConfig : ObservableObject
{
    /// <summary>本机 GPT-SoVITS api_v2 HTTP 服务。</summary>
    [ObservableProperty] private string _serviceUrl = "http://127.0.0.1:9880/";
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

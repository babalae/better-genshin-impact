using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Core.Config;

/// <summary>新增引擎独立保存实例和默认声音；角色 profile 仅保存引用。</summary>
public partial class QuestVoiceEngineConfig : ObservableObject
{
    /// <summary>本地或默认服务的根地址，保留反向代理子路径。</summary>
    [ObservableProperty] private string _localUrl = "";
    /// <summary>可选远程实例地址。</summary>
    [ObservableProperty] private string _remoteUrl = "";
    /// <summary>当前默认实例，不改变其他引擎选择。</summary>
    [ObservableProperty] private bool _useRemote;
    /// <summary>引擎的默认声音；不等同于角色目录 profile ID。</summary>
    [ObservableProperty] private string _voiceId = "";
    /// <summary>服务支持的语言。</summary>
    [ObservableProperty] private string _language = "zh";
    /// <summary>OpenAI 兼容模型名称。</summary>
    [ObservableProperty] private string _model = "tts-1";
    /// <summary>明确声明的音频格式。</summary>
    [ObservableProperty] private string _format = "wav";
    /// <summary>可选服务密钥，只通过请求头发送，不进入日志。</summary>
    [ObservableProperty] private string _apiKey = "";
    /// <summary>速度倍率，1 为默认。</summary>
    [ObservableProperty] private double _rate = 1;
    /// <summary>VOICEVOX pitchScale。</summary>
    [ObservableProperty] private double _pitch;

    /// <summary>完整缺省值不要求额外安装，不覆盖旧配置。</summary>
    public static Dictionary<string, QuestVoiceEngineConfig> Defaults() => new()
    {
        ["windows-sapi"] = new(),
        ["voicevox"] = new() { LocalUrl = "http://127.0.0.1:50021/", VoiceId = "0", Language = "ja" },
        ["vits-simple-api"] = new() { LocalUrl = "http://127.0.0.1:23456/", VoiceId = "VITS:0", Language = "auto" },
        ["openai-speech"] = new() { LocalUrl = "https://api.openai.com/v1/", VoiceId = "alloy", Format = "mp3" },
        ["google-translate"] = new() { VoiceId = "zh-CN", Language = "zh-CN" }
    };
}

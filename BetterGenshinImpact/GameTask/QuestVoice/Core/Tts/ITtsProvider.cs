using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;

namespace BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;

/// <summary>普通后端的共同能力；流式仍由可选 IStreamingTtsEngine 提供。</summary>
public interface ITtsProvider : ITtsEngine, IDisposable
{
    /// <summary>引擎身份和实际能力，不含 WPF 对象。</summary>
    TtsProviderInfo Info { get; }
    /// <summary>只校验引擎参数，不运行 OCR、上传资产或启动推理。</summary>
    void Validate(AppSettings settings);
    /// <summary>返回可选声音的稳定标识和显示名称。</summary>
    Task<IReadOnlyList<TtsVoiceInfo>> GetVoicesAsync(AppSettings settings, CancellationToken token);
    /// <summary>只读探测；成功不意味着已经完成合成或后端排空。</summary>
    Task<string> ProbeAsync(AppSettings settings, CancellationToken token);
}

/// <summary>驱动配置页面显隐，避免按引擎名散布条件判断。</summary>
public sealed record TtsProviderCapabilities(bool ServerConnection = false, bool VoiceAssets = false,
    bool Streaming = false, bool PresetVoices = false, bool RequiresDrain = false,
    bool Rate = false, bool Pitch = false, bool Language = false, bool Model = false, bool ApiKey = false,
    bool AudioFormat = false);

/// <summary>注册表和页面共用的不可变引擎描述。</summary>
public sealed record TtsProviderInfo(string Id, string DisplayName, TtsProviderCapabilities Capabilities,
    string Description);

/// <summary>Provider 专属声音；不等同于 GPT 权重路径。</summary>
public sealed record TtsVoiceInfo(string Id, string DisplayName, string Language);

/// <summary>启动时冻结技术类型、连接实例和预设声音。</summary>
public sealed record TtsProviderSettings(string ProviderId = "gpt-sovits", string InstanceId = "local",
    string VoiceId = "");

/// <summary>源编码显式标记，播放器只接收正规化后的 PCM16 WAV。</summary>
public enum TtsAudioEncoding { Pcm16Wav, Mp3, Pcm16 }

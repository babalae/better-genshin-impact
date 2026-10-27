using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;

/// <summary>集中注册内置引擎；不动态加载 DLL，不持有活动会话。</summary>
public static class TtsProviderRegistry
{
    /// <summary>每个条目提供描述和每轮独立实例的构造函数。</summary>
    private static readonly (TtsProviderInfo Info, Func<ITtsProvider> Create)[] Entries =
    [
        (new("gpt-sovits", "GPT-SoVITS", new(true, true, true, false, true),
            "本地或远程 api_v2；完整/流式合成，沿用安全切权与取消排空。"),
            () => new GptSovitsEngine(CreateHttpClient, new ReferenceAudioSelector(), new WavProcessor())),
        (new("huoshan", "火山翻译（在线预设音色）", new(PresetVoices: true),
            "无需账号或模型；发送对白文本到火山翻译，完整接收 MP3 后播放。非公开端点可能变更或限流。"),
            () => new HuoshanTtsProvider(CreateHttpClient))
    ];

    /// <summary>页面可读列表，不暴露注册数组的修改权限。</summary>
    public static IReadOnlyList<TtsProviderInfo> Providers { get; } = Array.AsReadOnly(Entries.Select(x => x.Info).ToArray());
    /// <summary>拒绝未知引擎，不能静默切换现有用户。</summary>
    public static TtsProviderInfo GetInfo(string id) => Entries.FirstOrDefault(x => x.Info.Id == id).Info
        ?? throw new VoiceException(VoiceErrorCode.Configuration, "未知 TTS 引擎：" + id);
    /// <summary>由调用者在合成完全结束后释放本轮引擎。</summary>
    public static ITtsProvider Create(string id)
    {
        GetInfo(id);
        return Entries.Single(x => x.Info.Id == id).Create();
    }
    /// <summary>所有 HTTP 客户端显式使用连接超时，保留系统代理。</summary>
    private static HttpClient CreateHttpClient(TimeoutSettings timeouts) => new(new SocketsHttpHandler
        { ConnectTimeout = TimeSpan.FromSeconds(timeouts.ConnectSeconds) });
}

using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>与角色解耦的声线草稿，普通 Provider 不要求填写 GPT 专用资产。</summary>
public partial class QuestVoiceManagementProfile : ObservableObject
{
    /// <summary>载入时身份，用于保存前迁移角色和兜底引用。</summary>
    public string OriginalId { get; }
    /// <summary>声线唯一身份。</summary>
    [ObservableProperty] private string _id;
    /// <summary>声线展示名称。</summary>
    [ObservableProperty] private string _name;
    /// <summary>注册表中的引擎身份。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesGptAssets))]
    private string _providerId;
    /// <summary>本地或远程实例身份。</summary>
    [ObservableProperty] private string _instanceId;
    /// <summary>Provider 自己的音色身份。</summary>
    [ObservableProperty] private string _providerVoiceId;
    /// <summary>GPT 权重路径。</summary>
    [ObservableProperty] private string _gptWeightsPath;
    /// <summary>SoVITS 权重路径。</summary>
    [ObservableProperty] private string _sovitsWeightsPath;
    /// <summary>参考音频路径。</summary>
    [ObservableProperty] private string _referenceAudioPath;
    /// <summary>参考音频逐字原文。</summary>
    [ObservableProperty] private string _promptText;
    /// <summary>请求情绪，空白表示未指定。</summary>
    [ObservableProperty] private string _requestedEmotion;
    /// <summary>参考音频情绪，空白表示未指定。</summary>
    [ObservableProperty] private string _referenceEmotion;
    /// <summary>只为具有资产能力的 Provider 展示 GPT 字段。</summary>
    public bool UsesGptAssets => TtsProviderRegistry.Providers.FirstOrDefault(x => x.Id == ProviderId)?.Capabilities.VoiceAssets == true;

    /// <summary>从不可变生产 profile 创建草稿。</summary>
    public QuestVoiceManagementProfile(RoutingVoice voice)
    {
        OriginalId = _id = voice.Id; _name = voice.Name;
        _providerId = voice.ProviderId; _instanceId = voice.InstanceId ?? "";
        _providerVoiceId = voice.ProviderVoiceId ?? "";
        _gptWeightsPath = voice.GptWeightsPath; _sovitsWeightsPath = voice.SovitsWeightsPath;
        _referenceAudioPath = voice.ReferenceAudioPath; _promptText = voice.PromptText;
        _requestedEmotion = voice.RequestedEmotion ?? ""; _referenceEmotion = voice.ReferenceEmotion ?? "";
    }

    /// <summary>生成独立不可变试听或保存对象。</summary>
    public RoutingVoice Build() => new(Id.Trim(), Name.Trim(), GptWeightsPath.Trim(), SovitsWeightsPath.Trim(),
        ReferenceAudioPath.Trim(), PromptText, Optional(RequestedEmotion), Optional(ReferenceEmotion),
        ProviderId, Optional(InstanceId), Optional(ProviderVoiceId));

    /// <summary>可选字段不输出空字符串。</summary>
    private static string? Optional(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>每个角色独立保留输入草稿，切换列表选中项不会丢失编辑。</summary>
public partial class QuestVoiceManagementSpeaker : ObservableObject
{
    /// <summary>载入时的身份，用于显式保存时迁移引用。</summary>
    public string OriginalId { get; }
    /// <summary>角色身份。</summary>
    [ObservableProperty] private string _id;
    /// <summary>角色正式名称，一行一项。</summary>
    [ObservableProperty] private string _names;
    /// <summary>角色别名，一行一项。</summary>
    [ObservableProperty] private string _aliases;
    /// <summary>OCR 易错名称，一行一项。</summary>
    [ObservableProperty] private string _corrections;
    /// <summary>声线引用，空值表示没有角色专用声线。</summary>
    [ObservableProperty] private string _voiceId;

    /// <summary>从不可变目录创建独立草稿。</summary>
    public QuestVoiceManagementSpeaker(SpeakerDefinition speaker)
    {
        OriginalId = _id = speaker.Id;
        _names = string.Join("\n", speaker.Names);
        _aliases = string.Join("\n", speaker.Aliases);
        _corrections = string.Join("\n", speaker.Corrections);
        _voiceId = speaker.VoiceId ?? "";
    }
}

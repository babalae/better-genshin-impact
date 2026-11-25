using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>给当前选中剧情的角色声线面板提供明确的加载、失败、成功和空闲状态。</summary>
public partial class QuestVoiceStoryWorkspace
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadedStoryRoles))]
    [NotifyPropertyChangedFor(nameof(HasStoryLoadError))]
    [NotifyPropertyChangedFor(nameof(IsStoryAwaitingSelection))]
    [NotifyPropertyChangedFor(nameof(CanGenerateSelectedStory))]
    private bool _isStoryLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadedStoryRoles))]
    [NotifyPropertyChangedFor(nameof(HasStoryLoadError))]
    [NotifyPropertyChangedFor(nameof(IsStoryAwaitingSelection))]
    [NotifyPropertyChangedFor(nameof(CanGenerateSelectedStory))]
    private string _storyLoadFailure = "";

    [ObservableProperty]
    private string _storyLoadingText = "正在加载剧情角色…";

    /// <summary>仅在已成功加载整个所选系列时展示角色列表；网络失败不展示空白列表。</summary>
    public bool HasLoadedStoryRoles => !IsStoryLoading && string.IsNullOrEmpty(StoryLoadFailure)
        && _loadedPairs.Count > 0;

    public bool HasStoryLoadError => !IsStoryLoading && !string.IsNullOrEmpty(StoryLoadFailure);

    public bool IsStoryAwaitingSelection => !IsStoryLoading && !HasStoryLoadError && _loadedPairs.Count == 0;

    public bool CanGenerateSelectedStory => IsEditable && HasLoadedStoryRoles
        && Lines.Any(line => line.CanSynthesize);

    /// <summary>ObservableCollection 的内容就位后，还需要通知由数据集合计算的面板状态。</summary>
    private void NotifyStoryDisplayChanged()
    {
        OnPropertyChanged(nameof(HasLoadedStoryRoles));
        OnPropertyChanged(nameof(HasStoryLoadError));
        OnPropertyChanged(nameof(IsStoryAwaitingSelection));
        OnPropertyChanged(nameof(CanGenerateSelectedStory));
    }
}

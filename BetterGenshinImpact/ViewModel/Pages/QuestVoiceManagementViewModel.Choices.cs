using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>旅行者、NPC、旁白及已知角色共用唯一声线目录，不复制模型资源。</summary>
public partial class QuestVoiceManagementViewModel
{
    /// <summary>把引擎声音注册为可共享的稳定 Profile，避免在三条规则中重复输入 token。</summary>
    public async Task RegisterProviderVoiceAsync(string providerId, string instanceId, string voiceId, string displayName)
    {
        await EnsureLoadedAsync();
        RequireEditable();
            if (string.IsNullOrWhiteSpace(voiceId) || string.IsNullOrWhiteSpace(displayName)) throw new InvalidOperationException("请先填写声音与显示名称。");
            var existing = Profiles.FirstOrDefault(x => x.ProviderId == providerId && x.InstanceId == instanceId && x.ProviderVoiceId == voiceId);
            if (existing is not null) { EditStatus = "此声音已在通用声线库中：" + existing.Name; return; }
            var id = UniqueId("voice", Profiles.SelectMany(x => new[] { x.Id, x.OriginalId }));
            var item = new QuestVoiceManagementProfile(new(id, displayName.Trim(), "", "", "", "", ProviderId: providerId,
                InstanceId: Optional(instanceId), ProviderVoiceId: voiceId.Trim()));
            AddProfileItem(item); MarkDraftChanged(); NotifyLists();
            EditStatus = "已加入通用声线库，可在配置与映射中选择。";
    }
    /// <summary>远程资源只接受服务器路径，浏览入口仅对本机可用。</summary>
    public bool CanBrowseSelectedProfileAssets => SelectedProfile is { } item && item.InstanceId != "remote"
        && !(item.InstanceId == "" && _factory.RoutingServerSidePaths);
    /// <summary>静态资源诊断不发网络请求；不以尚未测试连接冒充服务可用。</summary>
    public string SelectedProfileDiagnostic => SelectedProfile is { } item
        ? (VoiceChoices.FirstOrDefault(x => x.Id == item.OriginalId)?.DisplayName ?? item.Name) + " · 服务可用性请通过连接测试或试听确认"
        : "请选择或新建声线；未完成资源保留在草稿中。";
    /// <summary>GPT 页面只显示 GPT 资源，其他引擎通过其设置页提供声音。</summary>
    public IEnumerable<QuestVoiceManagementProfile> GptProfiles => Profiles.Where(x => x.ProviderId == "gpt-sovits");
    /// <summary>空声线库只显示紧凑选择框，选中或新建后才展示资源编辑器。</summary>
    public bool HasSelectedGptProfile => SelectedGptProfile is not null;
    /// <summary>列表刷新时的暂时空选择不清空正在编辑的 GPT 草稿。</summary>
    public QuestVoiceManagementProfile? SelectedGptProfile
    {
        get => SelectedProfile?.ProviderId == "gpt-sovits" ? SelectedProfile : null;
        set { if (value?.ProviderId == "gpt-sovits") SelectedProfile = value; }
    }
    /// <summary>统一的可选目录，含继承默认及失效引用，不把空选择回写成第一项。</summary>
    public IEnumerable<QuestVoiceVoiceChoice> VoiceChoices
    {
        get
        {
            var choices = new List<QuestVoiceVoiceChoice> { new("", "使用默认配音规则") };
            foreach (var item in Profiles)
            {
                var info = TtsProviderRegistry.Providers.FirstOrDefault(x => x.Id == item.ProviderId);
                var diagnostic = info is null ? " · 引擎不可用" : "";
                if (item.UsesGptAssets && item.InstanceId != "remote" && !(item.InstanceId == "" && _factory.RoutingServerSidePaths))
                {
                    try
                    {
                        var voice = BuildProfile(item);
                        if (new[] { voice.GptWeightsPath, voice.SovitsWeightsPath, voice.ReferenceAudioPath }.Any(p => !File.Exists(p))) diagnostic = " · 资源缺失";
                    }
                    catch { diagnostic = " · 路径待修正"; }
                }
                choices.Add(new(item.OriginalId, $"{item.Name} · {info?.DisplayName ?? item.ProviderId}{diagnostic}"));
            }
            foreach (var id in Speakers.Select(x => x.VoiceId).Concat(new[] { FallbackVoiceId, NarratorVoiceId }).Where(x => !string.IsNullOrEmpty(x)).Distinct())
                if (!choices.Any(x => Same(x.Id, id))) choices.Add(new(id, $"声线缺失：{id}（绑定保留）"));
            return choices;
        }
    }
    /// <summary>只编辑昵称即可，内部保留已有主角身份。</summary>
    public string TravelerVoiceId
    {
        get => Speakers.FirstOrDefault(x => Same(x.OriginalId, TravelerId) || Same(x.Id, TravelerId))?.VoiceId ?? "";
        set
        {
            if (!CanEditDraft || value is null) return;
            var traveler = Speakers.FirstOrDefault(x => Same(x.OriginalId, TravelerId) || Same(x.Id, TravelerId));
            if (traveler is null)
            {
                var id = UniqueId("traveler", Speakers.Select(x => x.Id));
                traveler = new(new(id, new[] { "旅行者" }, Array.Empty<string>(), Array.Empty<string>(), null));
                AddSpeakerItem(traveler);
                TravelerId = id;
            }
            traveler.VoiceId = value;
            OnPropertyChanged();
        }
    }
    /// <summary>组合列表重建时统一通知；不向用户暴露内部 ID 作为主要标签。</summary>
    private void NotifyVoiceChoices()
    {
        OnPropertyChanged(nameof(VoiceChoices));
        OnPropertyChanged(nameof(TravelerVoiceId));
        OnPropertyChanged(nameof(GptProfiles));
        OnPropertyChanged(nameof(CanBrowseSelectedProfileAssets));
        OnPropertyChanged(nameof(SelectedProfileDiagnostic));
        OnPropertyChanged(nameof(SelectedFallbackVoiceId));
        OnPropertyChanged(nameof(SelectedNarratorVoiceId));
        OnPropertyChanged(nameof(SelectedSpeakerVoiceId));
    }
    /// <summary>GPT 库新增自由命名的独立资源，不依赖默认引擎。</summary>
    [RelayCommand(CanExecute = nameof(CanEditDraft))]
    private void AddGptProfile() => Edit(() =>
    {
        var id = UniqueId("gpt.voice", Profiles.SelectMany(x => new[] { x.Id, x.OriginalId }));
        var item = new QuestVoiceManagementProfile(new(id, "新声线", "", "", "", "", ProviderId: "gpt-sovits",
            InstanceId: _owner.Config.UseRemoteService ? "remote" : "local"));
        AddProfileItem(item); SelectedProfile = item; MarkDraftChanged(); NotifyLists();
    });
    /// <summary>文件选择仅用于本机资源；远程路径不得自动上传或本机验证。</summary>
    [RelayCommand]
    private void BrowseProfileAsset(string kind)
    {
        Edit(() =>
        {
            if (SelectedProfile is not { } item) return;
            if (item.InstanceId == "remote" || item.InstanceId == "" && _factory.RoutingServerSidePaths)
                throw new InvalidOperationException("远程实例请填写服务器已有路径。");
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = kind == "gpt" ? "GPT 权重 (*.ckpt)|*.ckpt" : kind == "sovits" ? "SoVITS 权重 (*.pth)|*.pth"
                    : "参考音频 (*.wav;*.mp3;*.flac;*.ogg)|*.wav;*.mp3;*.flac;*.ogg"
            };
            if (dialog.ShowDialog() != true) return;
            if (kind == "gpt") item.GptWeightsPath = dialog.FileName;
            else if (kind == "sovits") item.SovitsWeightsPath = dialog.FileName;
            else item.ReferenceAudioPath = dialog.FileName;
        });
    }
}

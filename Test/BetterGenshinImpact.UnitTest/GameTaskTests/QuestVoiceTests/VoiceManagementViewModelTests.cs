using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.QuestVoice;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>通过生产命令和真实文件验证管理草稿，不启动 App、游戏、网络合成或音频设备。</summary>
public sealed class VoiceManagementViewModelTests
{
    /// <summary>编辑和切换只改变草稿，显式保存才写盘，历史继承实例保持为空。</summary>
    [Fact]
    public async Task Selection_retains_each_draft_and_explicit_save_roundtrips_legacy_instance()
    {
        using var fixture = new RoutingFixture();
        await fixture.WriteAsync();
        using var page = new PassivePage(fixture);
        var model = page.Owner.Management;
        await model.LoadCommand.ExecuteAsync(null);
        Assert.True(model.IsLoaded);
        var original = await File.ReadAllBytesAsync(fixture.CatalogPath);
        var ganyu = model.Speakers.Single(x => x.Id == "genshin.ganyu");
        var keqing = model.Speakers.Single(x => x.Id == "genshin.keqing");
        model.SelectedSpeaker = ganyu;
        ganyu.Aliases = "椰羊\n甘雨小姐";
        model.SelectedSpeaker = keqing;
        keqing.Corrections = "刻睛";
        model.SelectedSpeaker = ganyu;
        Assert.Equal("椰羊\n甘雨小姐", model.SelectedSpeaker.Aliases);
        Assert.Equal("刻睛", keqing.Corrections);
        Assert.True(model.HasChanges);
        Assert.Contains("未保存", model.DraftNotice);
        Assert.Equal(original, await File.ReadAllBytesAsync(fixture.CatalogPath));
        Assert.All(model.Profiles, x => Assert.Equal("", x.InstanceId));
        await model.SaveCommand.ExecuteAsync(null);
        Assert.False(model.HasChanges);
        Assert.Empty(page.Owner.CommandError);
        var saved = (await new RoutingCatalogStore().LoadAsync(fixture.CatalogPath)).Catalog;
        Assert.Equal(new[] { "椰羊", "甘雨小姐" }, saved.Speakers.Single(x => x.Id == "genshin.ganyu").Aliases);
        Assert.Equal("刻睛", Assert.Single(saved.Speakers.Single(x => x.Id == "genshin.keqing").Corrections));
        Assert.All(saved.Voices, x => Assert.Null(x.InstanceId));
        Assert.Equal(original, await File.ReadAllBytesAsync(fixture.CatalogPath + ".bak"));
    }

    /// <summary>角色和 profile 改名在保存时同时迁移旅行者、角色、旁白及兜底引用。</summary>
    [Fact]
    public async Task Renaming_identities_migrates_all_references_on_save()
    {
        using var fixture = new RoutingFixture();
        await fixture.WriteAsync();
        using var page = new PassivePage(fixture);
        var model = page.Owner.Management;
        await model.LoadCommand.ExecuteAsync(null);
        model.Speakers.Single(x => x.Id == "genshin.aether").Id = "traveler.aether";
        model.Profiles.Single(x => x.Id == "voice.ganyu").Id = "shared.ganyu";
        model.Profiles.Single(x => x.Id == "voice.fallback").Id = "shared.fallback";
        model.Profiles.Single(x => x.Id == "voice.narrator").Id = "shared.narrator";
        await model.SaveCommand.ExecuteAsync(null);
        Assert.Empty(page.Owner.CommandError);
        var saved = (await new RoutingCatalogStore().LoadAsync(fixture.CatalogPath)).Catalog;
        Assert.Equal("traveler.aether", saved.ProtagonistId);
        Assert.Equal("shared.fallback", saved.FallbackVoiceId);
        Assert.Equal("shared.narrator", saved.NarratorVoiceId);
        Assert.Equal("shared.ganyu", saved.Speakers.Single(x => x.Id == "genshin.ganyu").VoiceId);
        Assert.Equal("shared.ganyu", saved.Speakers.Single(x => x.Id == "traveler.aether").VoiceId);
    }

    /// <summary>加载不补齐荧，明确选择才新增，并阻止删除当前旅行者。</summary>
    [Fact]
    public async Task Traveler_is_added_only_on_user_selection_and_cannot_be_deleted_while_selected()
    {
        using var fixture = new RoutingFixture();
        await fixture.WriteAsync();
        using var page = new PassivePage(fixture);
        var model = page.Owner.Management;
        await model.LoadCommand.ExecuteAsync(null);
        Assert.Equal(3, model.Speakers.Count);
        Assert.DoesNotContain(model.Speakers, x => x.Names == "荧");
        model.SelectedTravelerId = "lumine";
        var lumine = Assert.Single(model.Speakers.Where(x => x.Names == "荧"));
        Assert.Equal(4, model.Speakers.Count);
        model.SelectedSpeaker = lumine;
        model.DeleteSpeakerCommand.Execute(null);
        Assert.Contains("旅行者", page.Owner.CommandError);
        Assert.Contains(lumine, model.Speakers);
        model.SelectedTravelerId = "genshin.aether";
        model.DeleteSpeakerCommand.Execute(null);
        Assert.DoesNotContain(lumine, model.Speakers);
        Assert.Empty(page.Owner.CommandError);
        Assert.True(model.HasChanges);
    }

    /// <summary>角色、兜底和旁白引用均阻止误删除；没有引用的新 profile 可以删除。</summary>
    [Fact]
    public async Task Referenced_profiles_are_protected_and_unused_profile_can_be_deleted()
    {
        using var fixture = new RoutingFixture();
        await fixture.WriteAsync();
        using var page = new PassivePage(fixture);
        var model = page.Owner.Management;
        await model.LoadCommand.ExecuteAsync(null);
        foreach (var profile in model.Profiles.ToArray())
        {
            model.SelectedProfile = profile;
            model.DeleteProfileCommand.Execute(null);
            Assert.Contains("引用", page.Owner.CommandError);
            Assert.Contains(profile, model.Profiles);
        }
        model.AddProfileCommand.Execute(null);
        var unused = model.SelectedProfile;
        Assert.NotNull(unused);
        Assert.Equal(4, model.Profiles.Count);
        model.DeleteProfileCommand.Execute(null);
        Assert.Equal(3, model.Profiles.Count);
        Assert.DoesNotContain(unused, model.Profiles);
        Assert.Empty(page.Owner.CommandError);
    }

    /// <summary>外部变更导致保存冲突时保留原文件、编辑对象及未保存状态。</summary>
    [Fact]
    public async Task External_sha_conflict_preserves_file_and_unsaved_draft()
    {
        using var fixture = new RoutingFixture();
        await fixture.WriteAsync();
        using var page = new PassivePage(fixture);
        var model = page.Owner.Management;
        await model.LoadCommand.ExecuteAsync(null);
        var speaker = model.Speakers.First();
        model.SelectedSpeaker = speaker;
        speaker.Aliases = "尚未提交的别名";
        await File.AppendAllTextAsync(fixture.CatalogPath, "\n ");
        var external = await File.ReadAllBytesAsync(fixture.CatalogPath);
        await model.SaveCommand.ExecuteAsync(null);
        Assert.True(model.HasChanges);
        Assert.Same(speaker, model.SelectedSpeaker);
        Assert.Equal("尚未提交的别名", speaker.Aliases);
        Assert.Contains("修改", page.Owner.CommandError);
        Assert.Equal(external, await File.ReadAllBytesAsync(fixture.CatalogPath));
        Assert.False(File.Exists(fixture.CatalogPath + ".bak"));
    }

    /// <summary>下拉临时空选择与切换 profile 不得清空引用或手输音色。</summary>
    [Fact]
    public async Task Transient_null_selections_and_profile_switching_preserve_inputs()
    {
        using var fixture = new RoutingFixture();
        await fixture.WriteAsync();
        using var page = new PassivePage(fixture);
        var model = page.Owner.Management;
        await model.LoadCommand.ExecuteAsync(null);
        var speaker = model.Speakers.First();
        model.SelectedSpeaker = speaker;
        model.SelectedTravelerId = null;
        model.SelectedFallbackVoiceId = null;
        model.SelectedNarratorVoiceId = null;
        model.SelectedSpeakerVoiceId = null;
        Assert.Equal("genshin.aether", model.TravelerId);
        Assert.Equal("voice.fallback", model.FallbackVoiceId);
        Assert.Equal("voice.narrator", model.NarratorVoiceId);
        Assert.Equal("voice.ganyu", speaker.VoiceId);
        var profile = model.Profiles.First();
        model.SelectedProfile = profile;
        profile.ProviderId = "huoshan";
        profile.ProviderVoiceId = "manual.voice.id";
        model.SelectedPresetVoice = null;
        model.SelectedProfile = model.Profiles.Last();
        model.SelectedProfile = profile;
        Assert.Equal("manual.voice.id", profile.ProviderVoiceId);
        model.SelectedPresetVoice = HuoshanTtsProvider.Voices.First();
        Assert.Equal(HuoshanTtsProvider.Voices.First().Id, profile.ProviderVoiceId);
    }

    /// <summary>直接执行被禁用命令仍检查忙碌守卫，不依赖 XAML 禁用状态。</summary>
    [Fact]
    public async Task Busy_state_rejects_programmatic_write_commands()
    {
        using var fixture = new RoutingFixture();
        await fixture.WriteAsync();
        using var page = new PassivePage(fixture);
        var model = page.Owner.Management;
        await model.LoadCommand.ExecuteAsync(null);
        var before = await File.ReadAllBytesAsync(fixture.CatalogPath);
        page.Owner.Status = page.Owner.Status with { IsBusy = true };
        Assert.False(model.CanEditDraft);
        model.AddProfileCommand.Execute(null);
        Assert.Equal(3, model.Profiles.Count);
        Assert.Contains("繁忙", page.Owner.CommandError);
        await model.SaveCommand.ExecuteAsync(null);
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.CatalogPath));
        Assert.False(model.IsWorking);
    }

    /// <summary>普通 profile 的预设列表不应依赖全局 GPT 服务地址或默认引擎资产。</summary>
    [Fact]
    public async Task Ordinary_profile_presets_ignore_invalid_global_gpt_service_url()
    {
        using var fixture = new RoutingFixture();
        fixture.Json["Version"] = 2;
        var voice = fixture.Json["Voices"]![0]!.AsObject();
        voice["ProviderId"] = "huoshan";
        voice["ProviderVoiceId"] = HuoshanTtsProvider.Voices.First().Id;
        await fixture.WriteAsync();
        using var page = new PassivePage(fixture);
        page.Owner.Config.ServiceUrl = "这不是合法的 GPT 服务地址";
        var model = page.Owner.Management;
        await model.LoadCommand.ExecuteAsync(null);
        model.SelectedProfile = model.Profiles.Single(x => x.ProviderId == "huoshan");
        Assert.Equal(3, model.PresetVoices.Count);
        Assert.Equal(HuoshanTtsProvider.Voices.Select(x => x.Id), model.PresetVoices.Select(x => x.Id));
        Assert.Empty(page.Owner.CommandError);
        Assert.DoesNotContain("URL", model.EditStatus, StringComparison.OrdinalIgnoreCase);
    }
    /// <summary>预设选择必须投影当前 profile 的音色，手输和切换都同步展示且空回写不清除。</summary>
    [Fact]
    public async Task Preset_selection_reflects_loaded_voice_id_and_tracks_manual_changes()
    {
        using var fixture = new RoutingFixture();
        fixture.Json["Version"] = 2;
        var voice = fixture.Json["Voices"]![0]!.AsObject();
        voice["ProviderId"] = "huoshan";
        voice["ProviderVoiceId"] = "zh_male_zhubo";
        await fixture.WriteAsync();
        using var page = new PassivePage(fixture);
        var model = page.Owner.Management;
        await model.LoadCommand.ExecuteAsync(null);
        var profile = model.Profiles.Single(x => x.ProviderId == "huoshan");
        model.SelectedProfile = profile;
        Assert.Equal("zh_male_zhubo", model.SelectedPresetVoice?.Id);
        var selectionChanges = 0;
        model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(model.SelectedPresetVoice)) selectionChanges++; };
        profile.ProviderVoiceId = "zh_female_qingxin";
        Assert.Equal("zh_female_qingxin", model.SelectedPresetVoice?.Id);
        Assert.True(selectionChanges > 0);
        model.SelectedPresetVoice = null;
        Assert.Equal("zh_female_qingxin", profile.ProviderVoiceId);
        model.SelectedProfile = model.Profiles.Last();
        Assert.Null(model.SelectedPresetVoice);
        model.SelectedProfile = profile;
        Assert.Equal("zh_female_qingxin", model.SelectedPresetVoice?.Id);
        var selected = model.SelectedPresetVoice;
        model.PresetVoices.Clear();
        Assert.Null(model.SelectedPresetVoice);
        model.PresetVoices.Add(selected!);
        Assert.Same(selected, model.SelectedPresetVoice);
    }
    /// <summary>重命名未保存 profile 后新增并分配第二条，保存不能把角色引用迁到第一条。</summary>
    [Fact]
    public async Task Rename_then_add_profile_keeps_second_assignment_after_save()
    {
        using var fixture = new RoutingFixture();
        await fixture.WriteAsync();
        using var page = new PassivePage(fixture);
        var model = page.Owner.Management;
        await model.LoadCommand.ExecuteAsync(null);
        model.AddProfileCommand.Execute(null);
        var first = model.SelectedProfile!;
        first.Id = "shared.custom";
        first.ProviderId = "huoshan";
        first.ProviderVoiceId = "zh_female_qingxin";
        model.AddProfileCommand.Execute(null);
        var second = model.SelectedProfile!;
        second.ProviderId = "huoshan";
        second.ProviderVoiceId = "zh_male_zhubo";
        var assignedId = second.Id;
        model.SelectedSpeaker = model.Speakers.Single(x => x.Id == "genshin.keqing");
        model.SelectedSpeakerVoiceId = second.OriginalId;
        await model.SaveCommand.ExecuteAsync(null);
        Assert.Empty(page.Owner.CommandError);
        var saved = (await new RoutingCatalogStore().LoadAsync(fixture.CatalogPath)).Catalog;
        var referenced = saved.Speakers.Single(x => x.Id == "genshin.keqing").VoiceId;
        Assert.Equal(assignedId, referenced);
        Assert.Equal("zh_male_zhubo", saved.Voices.Single(x => x.Id == referenced).ProviderVoiceId);
    }

    /// <summary>重命名未保存角色后新增并选择第二条旅行者，保存不能迁移到第一条角色。</summary>
    [Fact]
    public async Task Rename_then_add_speaker_keeps_second_traveler_after_save()
    {
        using var fixture = new RoutingFixture();
        await fixture.WriteAsync();
        using var page = new PassivePage(fixture);
        var model = page.Owner.Management;
        await model.LoadCommand.ExecuteAsync(null);
        model.AddSpeakerCommand.Execute(null);
        var first = model.SelectedSpeaker!;
        first.Id = "actor.custom";
        first.Names = "角色甲";
        model.AddSpeakerCommand.Execute(null);
        var second = model.SelectedSpeaker!;
        second.Names = "角色乙";
        var assignedId = second.Id;
        model.SelectedTravelerId = second.OriginalId;
        await model.SaveCommand.ExecuteAsync(null);
        Assert.Empty(page.Owner.CommandError);
        var saved = (await new RoutingCatalogStore().LoadAsync(fixture.CatalogPath)).Catalog;
        Assert.Equal(assignedId, saved.ProtagonistId);
        Assert.Equal("角色乙", Assert.Single(saved.Speakers.Single(x => x.Id == saved.ProtagonistId).Names));
    }
    /// <summary>真实服务使用被动未初始化宿主，只供文件管理命令读取空闲状态。</summary>
    private sealed class PassivePage : IDisposable
    {
        /// <summary>控制器由此测试夹具统一释放。</summary>
        private readonly QuestVoiceSessionController _controller;
        /// <summary>服务不连接游戏、不创建合成器或播放器。</summary>
        private readonly QuestVoiceService _service;
        /// <summary>按生产构造函数创建共享页面模型。</summary>
        public QuestVoicePageViewModel Owner { get; }

        /// <summary>沿用隔离 TEMP 中的真实路由文件和默认本地 GPT 配置。</summary>
        public PassivePage(RoutingFixture fixture)
        {
            var all = new AllConfig();
            all.QuestVoiceConfig.RoutingCatalogPath = fixture.CatalogPath;
            var factory = new QuestVoiceSessionFactory(all.QuestVoiceConfig, fixture.Root);
            _controller = new(factory);
            _service = new((GameRuntimeService)RuntimeHelpers.GetUninitializedObject(typeof(GameRuntimeService)),
                (TaskTriggerDispatcher)RuntimeHelpers.GetUninitializedObject(typeof(TaskTriggerDispatcher)),
                all, factory, _controller, NullLogger<QuestVoiceService>.Instance);
            Owner = new(all, _service, factory);
        }

        /// <summary>按宿主资源所有权顺序释放服务和控制器。</summary>
        public void Dispose() { _service.Dispose(); _controller.Dispose(); }
    }
}

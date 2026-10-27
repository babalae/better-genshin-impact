using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.QuestVoice;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Routing;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>Phase 8 设置页的真实草稿、自动保存和混合声线路由回归，不启动游戏或音频设备。</summary>
public sealed class Phase8SettingsTests
{
    /// <summary>首轮写盘期间继续输入并并发排空，最终必须保存最新输入而不替换编辑对象。</summary>
    [Fact]
    public async Task Concurrent_flushes_save_latest_revision_and_preserve_edit_objects()
    {
        using var fixture = new RoutingFixture();
        await fixture.WriteAsync();
        using var page = new PassivePage(fixture);
        var model = page.Owner.Management;
        await model.LoadCommand.ExecuteAsync(null);
        var speaker = model.Speakers.Single(x => x.Id == "genshin.ganyu");
        model.SelectedSpeaker = speaker;
        var editedDuringSave = false;
        model.PropertyChanged += (_, e) =>
        {
            // 在首个不可变快照已构造后确定性插入新输入，避免以计时猜测 I/O 是否正在进行。
            if (e.PropertyName != nameof(model.SaveStatus) || model.SaveStatus != "正在保存" || editedDuringSave) return;
            editedDuringSave = true;
            speaker.Aliases = "写盘期间新增别名";
            model.PlayerNames = "保存期间的昵称";
        };
        speaker.Aliases = "首轮别名";
        var pending = new List<Task<bool>> { model.FlushAutoSaveAsync() };
        for (var index = 0; index < 12; index++)
        {
            speaker.Aliases = $"连续输入{index}";
            model.PlayerNames = $"最新昵称{index}";
            pending.Add(model.FlushAutoSaveAsync());
        }
        await Task.WhenAll(pending);

        Assert.True(editedDuringSave);
        Assert.True(await model.FlushAutoSaveAsync());
        Assert.False(model.HasChanges);
        Assert.Same(speaker, model.SelectedSpeaker);
        Assert.Equal("连续输入11", speaker.Aliases);
        Assert.Empty(page.Owner.CommandError);
        var saved = (await new RoutingCatalogStore().LoadAsync(fixture.CatalogPath)).Catalog;
        Assert.Equal("连续输入11", Assert.Single(saved.Speakers.Single(x => x.Id == speaker.Id).Aliases));
        Assert.Equal("最新昵称11", Assert.Single(saved.PlayerNames));
        Assert.DoesNotContain(Directory.EnumerateFiles(Path.GetDirectoryName(fixture.CatalogPath)!), x => x.Contains(".tmp-", StringComparison.Ordinal));
    }

    /// <summary>不完整名称或音色编号不得覆盖文件，纠正同一草稿后可直接再次排空。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Invalid_input_keeps_file_and_draft_until_corrected(bool invalidName)
    {
        using var fixture = new RoutingFixture();
        ManagedVoiceRoutingTests.MakePresetCatalog(fixture);
        await fixture.WriteAsync();
        using var page = new PassivePage(fixture);
        var model = page.Owner.Management;
        await model.LoadCommand.ExecuteAsync(null);
        var original = await File.ReadAllBytesAsync(fixture.CatalogPath);
        var profile = model.Profiles.First();
        model.SelectedProfile = profile;
        if (invalidName) profile.Name = "";
        else profile.ProviderVoiceId = "";

        Assert.False(await model.FlushAutoSaveAsync());
        Assert.True(model.HasChanges);
        Assert.Same(profile, model.SelectedProfile);
        Assert.Equal("", invalidName ? profile.Name : profile.ProviderVoiceId);
        Assert.NotEmpty(page.Owner.CommandError);
        Assert.Equal(original, await File.ReadAllBytesAsync(fixture.CatalogPath));
        Assert.False(File.Exists(fixture.CatalogPath + ".bak"));

        if (invalidName) profile.Name = "修正后的声线名称";
        else profile.ProviderVoiceId = "zh_male_zhubo";
        Assert.True(await model.FlushAutoSaveAsync());
        Assert.False(model.HasChanges);
        Assert.Empty(page.Owner.CommandError);
        var saved = (await new RoutingCatalogStore().LoadAsync(fixture.CatalogPath)).Catalog;
        var voice = saved.Voices.Single(x => x.Id == profile.Id);
        Assert.Equal(invalidName ? "修正后的声线名称" : "zh_male_zhubo", invalidName ? voice.Name : voice.ProviderVoiceId);
    }

    /// <summary>新增声线或替换既有声线的本机资源缺失时不落盘，补正后保留原对象继续保存。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task New_or_changed_missing_local_asset_blocks_autosave_and_can_be_corrected(bool addProfile)
    {
        using var fixture = new RoutingFixture();
        await fixture.WriteAsync();
        using var page = new PassivePage(fixture);
        var model = page.Owner.Management;
        await model.LoadCommand.ExecuteAsync(null);
        var original = await File.ReadAllBytesAsync(fixture.CatalogPath);
        if (addProfile) model.AddGptProfileCommand.Execute(null);
        else model.SelectedProfile = model.Profiles.Single(x => x.Id == "voice.ganyu");
        var profile = model.SelectedProfile!;
        profile.Name = "新本机声线";
        profile.InstanceId = "local";
        profile.GptWeightsPath = fixture.Asset("missing.ckpt");
        profile.SovitsWeightsPath = fixture.Asset("ganyu.pth");
        profile.ReferenceAudioPath = fixture.Asset("ganyu.wav");
        profile.PromptText = "参考原文。";
        model.SelectedFallbackVoiceId = profile.OriginalId;

        Assert.False(await model.FlushAutoSaveAsync());
        Assert.True(model.HasChanges);
        Assert.Same(profile, model.SelectedProfile);
        Assert.Equal(profile.OriginalId, model.FallbackVoiceId);
        Assert.Equal(original, await File.ReadAllBytesAsync(fixture.CatalogPath));
        Assert.False(File.Exists(fixture.CatalogPath + ".bak"));

        profile.GptWeightsPath = fixture.Asset("ganyu.ckpt");
        Assert.True(await model.FlushAutoSaveAsync());
        var saved = (await new RoutingCatalogStore().LoadAsync(fixture.CatalogPath)).Catalog;
        Assert.Equal(profile.Id, saved.FallbackVoiceId);
        Assert.Equal(fixture.Asset("ganyu.ckpt"), saved.Voices.Single(x => x.Id == profile.Id).GptWeightsPath);
    }

    /// <summary>既存模型在本机失效不应阻塞其他设置输入，原绑定仍显示且原样保存。</summary>
    [Fact]
    public async Task Existing_missing_asset_keeps_binding_and_allows_unrelated_autosave()
    {
        using var fixture = new RoutingFixture();
        await fixture.WriteAsync();
        File.Delete(fixture.Asset("ganyu.ckpt"));
        using var page = new PassivePage(fixture);
        var model = page.Owner.Management;
        await model.LoadCommand.ExecuteAsync(null);
        Assert.True(model.IsLoaded);
        var profile = model.Profiles.Single(x => x.Id == "voice.ganyu");
        var speaker = model.Speakers.Single(x => x.Id == "genshin.ganyu");
        Assert.Contains(model.VoiceChoices, x => x.Id == profile.OriginalId && x.DisplayName.Contains("资源缺失", StringComparison.Ordinal));
        speaker.Aliases = "保留旧失效绑定";

        Assert.True(await model.FlushAutoSaveAsync());
        Assert.False(model.HasChanges);
        Assert.Equal(profile.OriginalId, speaker.VoiceId);
        var saved = (await new RoutingCatalogStore().LoadAsync(fixture.CatalogPath)).Catalog;
        Assert.Equal(profile.Id, saved.Speakers.Single(x => x.Id == speaker.Id).VoiceId);
        Assert.Equal(fixture.Asset("ganyu.ckpt"), saved.Voices.Single(x => x.Id == profile.Id).GptWeightsPath);
        Assert.False(File.Exists(fixture.Asset("ganyu.ckpt")));
    }

    /// <summary>自动保存必须继续执行真实 SHA 冲突检查，保留外部文件和当前编辑对象。</summary>
    [Fact]
    public async Task Autosave_sha_conflict_preserves_external_file_and_current_draft()
    {
        using var fixture = new RoutingFixture();
        await fixture.WriteAsync();
        using var page = new PassivePage(fixture);
        var model = page.Owner.Management;
        await model.LoadCommand.ExecuteAsync(null);
        var speaker = model.Speakers.First();
        model.SelectedSpeaker = speaker;
        speaker.Aliases = "等待处理冲突的别名";
        await File.AppendAllTextAsync(fixture.CatalogPath, "\n ");
        var external = await File.ReadAllBytesAsync(fixture.CatalogPath);

        Assert.False(await model.FlushAutoSaveAsync());
        Assert.True(model.HasChanges);
        Assert.Same(speaker, model.SelectedSpeaker);
        Assert.Equal("等待处理冲突的别名", speaker.Aliases);
        Assert.Contains("冲突", model.SaveStatus);
        Assert.Equal(external, await File.ReadAllBytesAsync(fixture.CatalogPath));
        Assert.False(File.Exists(fixture.CatalogPath + ".bak"));
    }

    /// <summary>排空前或提交状态通知期间切换路径均不得写入任一目标，切回原路径可继续保存。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Changed_catalog_path_refuses_both_targets_and_returning_to_original_recovers(bool changeDuringSave)
    {
        using var fixture = new RoutingFixture();
        await fixture.WriteAsync();
        using var page = new PassivePage(fixture);
        var model = page.Owner.Management;
        await model.LoadCommand.ExecuteAsync(null);
        var original = await File.ReadAllBytesAsync(fixture.CatalogPath);
        var otherPath = Path.Combine(fixture.Root, "other", "speakers.json");
        Directory.CreateDirectory(Path.GetDirectoryName(otherPath)!);
        await File.WriteAllBytesAsync(otherPath, original);
        model.PlayerNames = "保留旧目录昵称";
        if (changeDuringSave)
        {
            var changed = false;
            model.PropertyChanged += (_, e) =>
            {
                // 提交前状态属于真实可观察回调，路径守卫不能只检查排空入口。
                if (changed || e.PropertyName != nameof(model.SaveStatus) || model.SaveStatus != "正在保存") return;
                changed = true;
                page.Owner.Config.RoutingCatalogPath = otherPath;
            };
        }
        else page.Owner.Config.RoutingCatalogPath = otherPath;

        Assert.False(await model.FlushAutoSaveAsync());
        Assert.True(model.HasChanges);
        Assert.Equal("保留旧目录昵称", model.PlayerNames);
        Assert.Contains("路径", page.Owner.CommandError);
        Assert.Equal(original, await File.ReadAllBytesAsync(fixture.CatalogPath));
        Assert.Equal(original, await File.ReadAllBytesAsync(otherPath));
        Assert.False(File.Exists(fixture.CatalogPath + ".bak"));
        Assert.False(File.Exists(otherPath + ".bak"));

        page.Owner.Config.RoutingCatalogPath = fixture.CatalogPath;
        Assert.True(await model.FlushAutoSaveAsync());
        Assert.Equal(original, await File.ReadAllBytesAsync(otherPath));
        var saved = (await new RoutingCatalogStore().LoadAsync(fixture.CatalogPath)).Catalog;
        Assert.Equal("保留旧目录昵称", Assert.Single(saved.PlayerNames));
    }

    /// <summary>磁盘目标被占用时保留草稿；释放占用后无需重载即可恢复自动保存。</summary>
    [Fact]
    public async Task Locked_catalog_preserves_draft_and_saves_after_lock_is_released()
    {
        using var fixture = new RoutingFixture();
        await fixture.WriteAsync();
        using var page = new PassivePage(fixture);
        var model = page.Owner.Management;
        await model.LoadCommand.ExecuteAsync(null);
        var original = await File.ReadAllBytesAsync(fixture.CatalogPath);
        model.PlayerNames = "文件解锁后恢复";
        using (var occupied = new FileStream(fixture.CatalogPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.False(await model.FlushAutoSaveAsync());
            Assert.True(model.HasChanges);
            Assert.Contains("失败", model.SaveStatus);
            Assert.Equal("文件解锁后恢复", model.PlayerNames);
        }
        Assert.Equal(original, await File.ReadAllBytesAsync(fixture.CatalogPath));
        Assert.False(File.Exists(fixture.CatalogPath + ".bak"));

        Assert.True(await model.FlushAutoSaveAsync());
        Assert.False(model.HasChanges);
        Assert.Empty(page.Owner.CommandError);
        var saved = (await new RoutingCatalogStore().LoadAsync(fixture.CatalogPath)).Catalog;
        Assert.Equal("文件解锁后恢复", Assert.Single(saved.PlayerNames));
    }

    /// <summary>同一声线供多个规则共用，连续改展示名称后引用及资源身份都保持不变。</summary>
    [Fact]
    public async Task Renaming_shared_profile_keeps_all_rule_references_across_autosaves()
    {
        using var fixture = new RoutingFixture();
        await fixture.WriteAsync();
        using var page = new PassivePage(fixture);
        var model = page.Owner.Management;
        await model.LoadCommand.ExecuteAsync(null);
        var profile = model.Profiles.Single(x => x.Id == "voice.ganyu");
        model.SelectedFallbackVoiceId = profile.OriginalId;
        model.SelectedNarratorVoiceId = profile.OriginalId;
        model.TravelerVoiceId = profile.OriginalId;
        model.SelectedSpeaker = model.Speakers.Single(x => x.Id == "genshin.keqing");
        model.SelectedSpeakerVoiceId = profile.OriginalId;
        Assert.True(await model.FlushAutoSaveAsync());
        foreach (var name in new[] { "共用声线第一次改名", "共用声线最终名称" })
        {
            profile.Name = name;
            Assert.True(await model.FlushAutoSaveAsync());
            var saved = (await new RoutingCatalogStore().LoadAsync(fixture.CatalogPath)).Catalog;
            Assert.Equal("voice.ganyu", profile.Id);
            Assert.Equal(profile.Id, saved.FallbackVoiceId);
            Assert.Equal(profile.Id, saved.NarratorVoiceId);
            Assert.All(saved.Speakers, x => Assert.Equal(profile.Id, x.VoiceId));
            Assert.Equal(name, saved.Voices.Single(x => x.Id == profile.Id).Name);
            Assert.Equal(3, saved.Voices.Count);
        }
    }

    /// <summary>无需选择空或荧，直接设置旅行者声线和昵称即可保存，并与其他引擎规则共同生效。</summary>
    [Fact]
    public async Task Traveler_voice_creates_internal_speaker_and_mixed_provider_rules_route_correctly()
    {
        using var fixture = new RoutingFixture();
        fixture.Json["Version"] = 2;
        fixture.Json["ProtagonistId"] = null;
        fixture.Json["Speakers"]!.AsArray().RemoveAt(2);
        var fallback = fixture.Json["Voices"]![1]!.AsObject();
        fallback["ProviderId"] = "huoshan";
        fallback["ProviderVoiceId"] = "zh_male_zhubo";
        var narrator = fixture.Json["Voices"]![2]!.AsObject();
        narrator["ProviderId"] = "openai-speech";
        narrator["InstanceId"] = "remote";
        narrator["ProviderVoiceId"] = "custom.narrator";
        await fixture.WriteAsync();
        using var page = new PassivePage(fixture);
        var model = page.Owner.Management;
        await model.LoadCommand.ExecuteAsync(null);
        Assert.Empty(model.TravelerId);
        model.PlayerNames = "星海旅人\n旅行者";
        model.TravelerVoiceId = "voice.ganyu";

        Assert.True(await model.FlushAutoSaveAsync());
        var saved = (await new RoutingCatalogStore().LoadAsync(fixture.CatalogPath)).Catalog;
        var traveler = Assert.Single(saved.Speakers.Where(x => x.Id == saved.ProtagonistId));
        Assert.Equal(3, saved.Speakers.Count);
        Assert.Equal(model.TravelerId, traveler.Id);
        Assert.Equal("voice.ganyu", traveler.VoiceId);
        Assert.DoesNotContain(saved.Speakers, x => x.Names.Contains("空") || x.Names.Contains("荧"));
        Assert.Equal(new[] { "星海旅人", "旅行者" }, saved.PlayerNames);

        // 使用真实路由服务读取已保存文件，避免夹具的 ResolveAsync 重新写回原始 JSON。
        var router = new VoiceRoutingService(new SpeakerCatalogLoader(), new SpeakerRouter());
        foreach (var nickname in saved.PlayerNames)
        {
            var routed = await router.ResolveAsync(new(new(fixture.CatalogPath), new(nickname, SpeakerNameStatus.Accepted), fixture.LegacyVoice), default);
            Assert.Equal(traveler.Id, routed.Speaker.CanonicalId);
            Assert.Equal("voice.ganyu", routed.VoiceId);
            Assert.Equal("gpt-sovits", routed.Voice.Provider!.ProviderId);
        }
        var known = await router.ResolveAsync(new(new(fixture.CatalogPath), new("甘雨", SpeakerNameStatus.Accepted), fixture.LegacyVoice), default);
        Assert.Equal("genshin.ganyu", known.Speaker.CanonicalId);
        Assert.Equal("voice.ganyu", known.VoiceId);
        Assert.Equal("gpt-sovits", known.Voice.Provider!.ProviderId);
        var unknown = await router.ResolveAsync(new(new(fixture.CatalogPath), new("未知NPC", SpeakerNameStatus.Accepted), fixture.LegacyVoice), default);
        Assert.Equal("voice.fallback", unknown.VoiceId);
        Assert.Equal("huoshan", unknown.Voice.Provider!.ProviderId);
        Assert.Equal("zh_male_zhubo", unknown.Voice.Provider.VoiceId);
        var narration = await router.ResolveAsync(new(new(fixture.CatalogPath), new(null, SpeakerNameStatus.Missing, DialogueContext.Narration), fixture.LegacyVoice), default);
        Assert.Equal("voice.narrator", narration.VoiceId);
        Assert.Equal("openai-speech", narration.Voice.Provider!.ProviderId);
        Assert.Equal("remote", narration.Voice.Provider.InstanceId);
        Assert.Equal("custom.narrator", narration.Voice.Provider.VoiceId);
    }

    /// <summary>刷新列表造成的暂时空选择不清除已有规则，显式默认条目仍能清除单条绑定。</summary>
    [Fact]
    public async Task Transient_null_choices_preserve_rules_but_explicit_default_clears_only_target()
    {
        using var fixture = new RoutingFixture();
        ManagedVoiceRoutingTests.MakePresetCatalog(fixture);
        await fixture.WriteAsync();
        using var page = new PassivePage(fixture);
        var model = page.Owner.Management;
        await model.LoadCommand.ExecuteAsync(null);
        var speaker = model.Speakers.Single(x => x.Id == "genshin.ganyu");
        model.SelectedSpeaker = speaker;
        model.SelectedProfile = model.Profiles.Single(x => x.Id == "voice.ganyu");
        model.TravelerVoiceId = null!;
        model.SelectedFallbackVoiceId = null;
        model.SelectedNarratorVoiceId = null;
        model.SelectedSpeakerVoiceId = null;
        model.SelectedPresetVoice = null;
        Assert.False(model.HasChanges);
        Assert.Equal("voice.ganyu", model.TravelerVoiceId);
        Assert.Equal("voice.fallback", model.FallbackVoiceId);
        Assert.Equal("voice.narrator", model.NarratorVoiceId);
        Assert.Equal("voice.ganyu", speaker.VoiceId);
        Assert.Equal("zh_female_qingxin", model.SelectedProfile.ProviderVoiceId);

        model.SelectedSpeakerVoiceId = "";
        Assert.True(await model.FlushAutoSaveAsync());
        var saved = (await new RoutingCatalogStore().LoadAsync(fixture.CatalogPath)).Catalog;
        Assert.Null(saved.Speakers.Single(x => x.Id == speaker.Id).VoiceId);
        Assert.Equal("voice.ganyu", saved.Speakers.Single(x => x.Id == saved.ProtagonistId).VoiceId);
        Assert.Equal("voice.fallback", saved.FallbackVoiceId);
        Assert.Equal("voice.narrator", saved.NarratorVoiceId);
    }

    /// <summary>旧配置仍由工厂按安装目录解析，删除旧编辑器不改变快照和全局引擎。</summary>
    [Fact]
    public void Local_default_relative_paths_use_program_directory_in_provider_snapshot()
    {
        using var fixture = new RoutingFixture();
        var config = CreateDefaultVoiceConfig(fixture);
        var factory = new QuestVoiceSessionFactory(config, fixture.Root);
        var settings = factory.CreateProviderSettings("gpt-sovits", false);
        Assert.Equal(fixture.Asset("ganyu.ckpt"), settings.Voice.GptWeightsPath);
        Assert.Equal(fixture.Asset("ganyu.pth"), settings.Voice.SovitsWeightsPath);
        Assert.Equal(fixture.Asset("ganyu.wav"), settings.Voice.ReferenceAudioPath);
        Assert.Equal(config.PromptText, settings.Voice.PromptText);
        Assert.Equal("gpt-sovits", settings.Provider!.ProviderId);
        Assert.Equal("huoshan", config.ProviderId);
    }

    /// <summary>新建项立即成为下拉选中项，列表临时空回写不清空其独立草稿。</summary>
    [Fact]
    public async Task New_gpt_profile_is_selected_in_shared_picker_and_survives_transient_null()
    {
        using var fixture = new RoutingFixture();
        await fixture.WriteAsync();
        using var page = new PassivePage(fixture);
        var model = page.Owner.Management;
        await model.LoadCommand.ExecuteAsync(null);
        model.AddGptProfileCommand.Execute(null);
        var created = model.SelectedProfile;
        Assert.NotNull(created);
        Assert.Same(created, model.SelectedGptProfile);
        Assert.Contains(created, model.GptProfiles);
        Assert.True(model.HasSelectedGptProfile);
        model.SelectedGptProfile = null;
        Assert.Same(created, model.SelectedGptProfile);
        model.DeleteProfileCommand.Execute(null);
        Assert.False(model.HasSelectedGptProfile);
        Assert.DoesNotContain(created, model.GptProfiles);
    }

    /// <summary>没有声音时不显示空白资源编辑器，新增后才展开对应草稿。</summary>
    [Fact]
    public async Task Empty_gpt_library_exposes_editor_only_after_creating_profile()
    {
        using var fixture = new RoutingFixture();
        fixture.Json["Voices"]!.AsArray().Clear();
        fixture.Json["FallbackVoiceId"] = null;
        fixture.Json["NarratorVoiceId"] = null;
        foreach (var speaker in fixture.Json["Speakers"]!.AsArray()) speaker!["VoiceId"] = null;
        await fixture.WriteAsync();
        using var page = new PassivePage(fixture);
        var model = page.Owner.Management;
        await model.LoadCommand.ExecuteAsync(null);
        Assert.Empty(model.GptProfiles);
        Assert.False(model.HasSelectedGptProfile);
        model.AddGptProfileCommand.Execute(null);
        Assert.Single(model.GptProfiles);
        Assert.True(model.HasSelectedGptProfile);
    }
    /// <summary>引擎声线注册必须把非法输入异常交给调用页处理，不能吞错后添加空声音或改动目录。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Registering_empty_provider_voice_throws_without_adding_profile_or_writing_catalog(string voiceId)
    {
        using var fixture = new RoutingFixture();
        await fixture.WriteAsync();
        using var page = new PassivePage(fixture);
        var model = page.Owner.Management;
        await model.LoadCommand.ExecuteAsync(null);
        var profiles = model.Profiles.ToArray();
        var original = await File.ReadAllBytesAsync(fixture.CatalogPath);

        await Assert.ThrowsAsync<InvalidOperationException>(() => model.RegisterProviderVoiceAsync("huoshan", "local", voiceId, "不应添加的声线"));
        Assert.Equal(profiles, model.Profiles.ToArray());
        Assert.False(model.HasChanges);
        Assert.True(await model.FlushAutoSaveAsync());
        Assert.Equal(original, await File.ReadAllBytesAsync(fixture.CatalogPath));
        Assert.False(File.Exists(fixture.CatalogPath + ".bak"));
    }

    /// <summary>两实例都拥有有效初始成套资源，避免测试结果依赖宿主旧配置或进程当前目录。</summary>
    private static QuestVoiceConfig CreateDefaultVoiceConfig(RoutingFixture fixture) => new()
    {
        ProviderId = "huoshan",
        VoiceName = "原有默认声线",
        GptWeightsPath = Path.GetRelativePath(fixture.Root, fixture.Asset("ganyu.ckpt")),
        SovitsWeightsPath = Path.GetRelativePath(fixture.Root, fixture.Asset("ganyu.pth")),
        ReferenceAudioPath = Path.GetRelativePath(fixture.Root, fixture.Asset("ganyu.wav")),
        PromptText = "原有本机参考台词。",
        RemoteGptWeightsPath = "/server/original.ckpt",
        RemoteSovitsWeightsPath = "/server/original.pth",
        RemoteReferenceAudioPath = "/server/original.wav",
        RemotePromptText = "原有远程参考台词。"
    };

    /// <summary>仅为设置模型提供空闲宿主和真实存储路径，沿用已有管理测试的资源所有权。</summary>
    private sealed class PassivePage : IDisposable
    {
        /// <summary>测试拥有的真实会话控制器，不创建会话。</summary>
        private readonly QuestVoiceSessionController _controller;
        /// <summary>测试拥有的被动服务，不连接游戏或语音引擎。</summary>
        private readonly QuestVoiceService _service;
        /// <summary>生产页面模型及其懒加载管理编辑器。</summary>
        public QuestVoicePageViewModel Owner { get; }

        /// <summary>把 AllConfig 路由路径绑定到每例独立的 TEMP 目录。</summary>
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

        /// <summary>按服务先于控制器的顺序释放测试宿主。</summary>
        public void Dispose() { _service.Dispose(); _controller.Dispose(); }
    }
}

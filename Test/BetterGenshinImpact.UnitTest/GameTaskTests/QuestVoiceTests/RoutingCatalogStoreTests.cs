using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Routing;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>真实磁盘验证升级、备份、冲突和导出路径，不模拟文件系统。</summary>
public sealed class RoutingCatalogStoreTests
{
    /// <summary>旧目录保存为版本二后，原始文件被备份且原角色与资产解析不变。</summary>
    [Fact]
    public async Task Legacy_roundtrip_and_export_preserve_assets_and_backup()
    {
        using var fixture = new RoutingFixture();
        await fixture.WriteAsync();
        var original = await File.ReadAllBytesAsync(fixture.CatalogPath);
        var store = new RoutingCatalogStore();
        var loaded = await store.LoadAsync(fixture.CatalogPath);
        var saved = await store.SaveAsync(fixture.CatalogPath, Upgrade(loaded.Catalog), loaded.CatalogSha256);
        Assert.Equal(original, await File.ReadAllBytesAsync(fixture.CatalogPath + ".bak"));
        Assert.Equal(2, saved.Catalog.Version);
        Assert.Equal(loaded.Catalog.Voices, saved.Catalog.Voices);
        var exported = await store.SaveAsync(Path.Combine(fixture.Root, "export", "voices.json"), saved.Catalog, null);
        Assert.Equal(saved.Catalog.Voices, exported.Catalog.Voices);
        Assert.Equal("genshin.aether", exported.Catalog.ProtagonistId);
    }

    /// <summary>编辑期间的外部更新不能被旧草稿覆盖。</summary>
    [Fact]
    public async Task Concurrent_external_edit_and_cancel_do_not_corrupt_existing_data()
    {
        using var fixture = new RoutingFixture();
        await fixture.WriteAsync();
        var store = new RoutingCatalogStore();
        var loaded = await store.LoadAsync(fixture.CatalogPath);
        await File.AppendAllTextAsync(fixture.CatalogPath, " ");
        var changed = await File.ReadAllBytesAsync(fixture.CatalogPath);
        await Assert.ThrowsAsync<VoiceException>(() => store.SaveAsync(fixture.CatalogPath, Upgrade(loaded.Catalog), loaded.CatalogSha256));
        Assert.Equal(changed, await File.ReadAllBytesAsync(fixture.CatalogPath));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(fixture.CatalogPath, Upgrade(loaded.Catalog), null, new(true)));
        Assert.Equal(changed, await File.ReadAllBytesAsync(fixture.CatalogPath));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(fixture.CatalogPath)!, "*.tmp-*"));
    }

    /// <summary>已有目标必须以读到的版本为条件，非法引用不能损坏目录。</summary>
    [Fact]
    public async Task Existing_target_and_invalid_references_are_rejected_before_writing()
    {
        using var fixture = new RoutingFixture();
        await fixture.WriteAsync();
        var store = new RoutingCatalogStore();
        var loaded = await store.LoadAsync(fixture.CatalogPath);
        await Assert.ThrowsAsync<VoiceException>(() => store.SaveAsync(fixture.CatalogPath, Upgrade(loaded.Catalog), null));
        var bad = new RoutingCatalog(2, "genshin", null, [], null, "missing", MissingVoicePolicy.Fallback, [], []);
        await Assert.ThrowsAsync<VoiceException>(() => store.SaveAsync(fixture.CatalogPath, bad, loaded.CatalogSha256));
        Assert.Equal(loaded.CatalogSha256, (await store.LoadAsync(fixture.CatalogPath)).CatalogSha256);
    }

    /// <summary>选择新版本但不改变原数据内容。</summary>
    private static RoutingCatalog Upgrade(RoutingCatalog catalog) => new(2, catalog.Game, catalog.ProtagonistId,
        catalog.PlayerNames, catalog.NarratorVoiceId, catalog.FallbackVoiceId, catalog.MissingVoicePolicy, catalog.Speakers, catalog.Voices);
}

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Routing;

/// <summary>角色目录的读取、校验及带并发保护的持久化边界。</summary>
public sealed class RoutingCatalogStore
{
    /// <summary>读取真实目录，保留其内容散列用于后续冲突检测。</summary>
    public Task<RoutingCatalogSnapshot> LoadAsync(string path, CancellationToken token = default, bool serverSidePaths = false)
        => new SpeakerCatalogLoader().LoadAsync(path, token, serverSidePaths);

    /// <summary>只验证结构和引用，资源可用性由运行与试听入口检查。</summary>
    public void Validate(RoutingCatalog catalog) => RoutingCatalogValidation.Validate(catalog);

    /// <summary>仅在原内容仍匹配时保存，原文件作为备份保留。</summary>
    public async Task<RoutingCatalogSnapshot> SaveAsync(string path, RoutingCatalog catalog, string? expectedSha256,
        CancellationToken token = default, bool serverSidePaths = false)
    {
        token.ThrowIfCancellationRequested();
        Validate(catalog);
        path = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(path)!;
        var bytes = Serialize(catalog, directory, serverSidePaths);
        // 在触碰目标文件前，用同一生产解析器验证序列化结果。
        using (var document = JsonDocument.Parse(bytes))
            RoutingCatalogValidation.Validate(RoutingCatalogJson.Parse(document.RootElement, directory, serverSidePaths));
        Directory.CreateDirectory(directory);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using var saveLock = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            // 打开的原文件禁止外部写入，同时允许本次原子替换；摘要拒绝陈旧草稿。
            await using var original = File.Exists(path)
                ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete) : null;
            var actual = original is null ? null : Convert.ToHexString(await SHA256.HashDataAsync(original, token).ConfigureAwait(false));
            if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw RoutingPath.Invalid("Catalog", "文件已被其他操作修改，或导出目标已存在。请重新加载并核对后保存。");
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await output.WriteAsync(bytes, token).ConfigureAwait(false);
                await output.FlushAsync(token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            if (original is null) File.Move(temporary, path, overwrite: false);
            else File.Replace(temporary, path, path + ".bak", ignoreMetadataErrors: true);
            // 已提交后不再报告取消，调用方必须获得刚保存的有效版本。
            return await LoadAsync(path, CancellationToken.None, serverSidePaths).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new VoiceException(VoiceErrorCode.Configuration, "角色目录保存失败（文件占用或权限不足）：" + exception.Message,
                innerException: exception);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>升级为版本二；本地资产随导出目录重定位，服务器资产不作本机路径转换。</summary>
    private static byte[] Serialize(RoutingCatalog catalog, string directory, bool serverSidePaths)
    {
        var voices = catalog.Voices.Select(voice =>
        {
            var remote = voice.InstanceId is null ? serverSidePaths : voice.InstanceId == "remote";
            var assets = Tts.TtsProviderRegistry.GetInfo(voice.ProviderId).Capabilities.VoiceAssets;
            string? Asset(string value) => !assets ? null : remote ? value
                : Path.GetRelativePath(directory, Path.GetFullPath(value, directory)).Replace('\\', '/');
            return new
            {
                voice.Id, voice.Name, voice.ProviderId, voice.InstanceId, voice.ProviderVoiceId,
                GptWeightsPath = Asset(voice.GptWeightsPath), SovitsWeightsPath = Asset(voice.SovitsWeightsPath),
                ReferenceAudioPath = Asset(voice.ReferenceAudioPath), PromptText = assets ? voice.PromptText : null,
                voice.RequestedEmotion, voice.ReferenceEmotion
            };
        });
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = 2, catalog.Game, catalog.ProtagonistId, catalog.PlayerNames, catalog.NarratorVoiceId,
            catalog.FallbackVoiceId, MissingVoicePolicy = catalog.MissingVoicePolicy.ToString(), catalog.Speakers, Voices = voices
        }, new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
    }
}

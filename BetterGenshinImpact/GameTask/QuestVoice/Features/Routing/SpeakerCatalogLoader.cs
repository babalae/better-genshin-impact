// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text.Json;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Routing;

/// <summary>SpeakerCatalogLoader：原神自动配音的业务类型。</summary>
public sealed class SpeakerCatalogLoader
{
    /// <summary>异步读取并严格校验外置配置或布局。</summary>
    public async Task<RoutingCatalogSnapshot> LoadAsync(string catalogPath, CancellationToken cancellationToken = default,
        bool serverSidePaths = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var path = Path.GetFullPath(catalogPath, AppContext.BaseDirectory);
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            // The optional UTF-8 BOM belongs to the hashed bytes, not the JSON document.
            var offset = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
            using var document = JsonDocument.Parse(bytes.AsMemory(offset));
            var catalog = RoutingCatalogJson.Parse(document.RootElement, Path.GetDirectoryName(path)!, serverSidePaths);
            RoutingCatalogValidation.Validate(catalog);
            cancellationToken.ThrowIfCancellationRequested();
            return new(catalog, hash, path);
        }
        catch (VoiceException exception)
        {
            throw new VoiceException(VoiceErrorCode.Configuration, $"Catalog '{catalogPath}': {exception.Message}", innerException: exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException
            or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            throw new VoiceException(VoiceErrorCode.Configuration, $"Catalog '{catalogPath}': {exception.Message}", innerException: exception);
        }
    }
}

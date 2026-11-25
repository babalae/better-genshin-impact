using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Core.TTS;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Features.TTS;
using BetterGenshinImpact.Service;
using JsonConvert = Newtonsoft.Json.JsonConvert;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.PreSynthesis;

/// <summary>单条剧情原文配音请求。</summary>
public sealed record QuestPreSynthesisLine(string Key, string Speaker, string Text);
/// <summary>后台预合成的逐条进度，不包含音频内容。</summary>
public sealed record QuestPreSynthesisProgress(string Key, string State, int Completed, int Total, int CacheHits, int Failed);
/// <summary>本次任务的真实合成统计。</summary>
public sealed record QuestPreSynthesisSummary(int Completed, int Total, int CacheHits, int Failed);

/// <summary>BetterGI 语音 Provider 驱动的安全离线预合成，独立 WAV 缓存，不引入视频测试功能。</summary>
public sealed class QuestVoicePreSynthesisService(
    QuestVoiceSessionFactory factory, QuestVoiceConfig config, QuestVoiceService liveService)
{
    /// <summary>缓存始终位于安装目录下的 User 区域。</summary>
    public string CacheDirectory => Path.Combine(factory.ProgramDirectory, "User", "QuestVoice", "cache", "pre-synthesis");

    /// <summary>以全局节点和语言查找上次成功生成并已落盘的音频；禁止使用索引中的绝对路径。</summary>
    public string? FindCachedWAV(string nodeKey, string language)
    {
        var index = ReadIndex();
        if (!index.TryGetValue(language + "|" + nodeKey, out var fileName) ||
            fileName != Path.GetFileName(fileName) ||
            !fileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return null;
        var file = Path.Combine(CacheDirectory, fileName);
        return File.Exists(file) ? file : null;
    }

    /// <summary>索引仅保存 Hash WAV 文件名，不暴露工作目录以外的路径。</summary>
    private Dictionary<string, string> ReadIndex()
    {
        var path = Path.Combine(CacheDirectory, "line-index.json");
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? []
                : [];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }

    /// <summary>完成或命中时原子登记，重启后仍可点播且不会读取半写入索引。</summary>
    private async Task RegisterCachedWAVAsync(Dictionary<string, string> index,
        string key, string language, string wavPath, CancellationToken token)
    {
        index[language + "|" + key] = Path.GetFileName(wavPath);
        var path = Path.Combine(CacheDirectory, "line-index.json");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(index), token);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>串行生成当前选中原文，并使用设置编辑租约防止与实时配音竞争服务权重。</summary>
    public async Task<QuestPreSynthesisSummary> GenerateAsync(IReadOnlyList<QuestPreSynthesisLine> lines,
        string speechLanguage, Func<CancellationToken, Task> waitWhilePaused,
        IProgress<QuestPreSynthesisProgress> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (speechLanguage is not ("jpn" or "chs"))
            throw new ArgumentException("仅支持 CHS/JP 剧情原文。", nameof(speechLanguage));
        Directory.CreateDirectory(CacheDirectory);
        // 配置和权重映射在批次开始时冻结，用户编辑其他 Tab 不改变在途请求的模型/服务。
        var frozenConfig = JsonConvert.DeserializeObject<QuestVoiceConfig>(JsonConvert.SerializeObject(config))
            ?? throw new InvalidOperationException("无法冻结本次语音配置。");
        var runtimeFactory = new QuestVoiceSessionFactory(frozenConfig, factory.ProgramDirectory);
        // P1 不再抢占 P0：仅在每条实际需要网络合成的请求期间申请暂停租约。
        // P0 恢复请求在当前 HTTP 安全排空后可重新启动，不必等待整个章节生成完毕。
        await runtimeFactory.EnsureResourcesAsync(cancellationToken);
        RoutingCatalog? catalog = null;
        if (frozenConfig.RoutingEnabled)
            catalog = (await new SpeakerCatalogLoader().LoadAsync(runtimeFactory.RoutingCatalogPath,
                cancellationToken, runtimeFactory.RoutingServerSidePaths)).Catalog;

        using var provider = runtimeFactory.CreateRoutingProvider();
        var router = new SpeakerRouter();
        var cacheIndex = ReadIndex();
        int completed = 0, hits = 0, failed = 0, failuresInARow = 0;
        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await waitWhilePaused(cancellationToken);
            try
            {
                if (string.IsNullOrWhiteSpace(line.Text))
                    throw new InvalidDataException("当前目标语言台词为空。");
                RoutingVoice? mapped = null;
                if (catalog is not null)
                {
                    var route = router.Route(new SpeakerRoutingInput(line.Speaker, SpeakerNameStatus.Accepted), catalog);
                    mapped = catalog.Voices.FirstOrDefault(x => x.Id == route.VoiceId);
                    if (mapped is null && catalog.MissingVoicePolicy == MissingVoicePolicy.Fail)
                        throw new InvalidOperationException($"角色「{line.Speaker}」未分配声线；请先在角色映射中绑定。");
                    if (mapped is null && route.VoiceId is { Length: > 0 } missingId &&
                        missingId != RoutingCatalog.ConfiguredVoiceId)
                        throw new InvalidOperationException($"角色「{line.Speaker}」引用不存在的声线 {missingId}。");
                }

                var settings = mapped is null ? runtimeFactory.CreateTtsSettings() : runtimeFactory.CreateVoiceSettings(mapped);
                var language = speechLanguage == "jpn" ? "ja" : "zh";
                settings = settings with
                {
                    Protocol = (settings.Protocol ?? new TtsProtocolSettings()) with
                    { TextLanguage = language, PromptLanguage = TtsLanguageDetector.DetectFromText(settings.Voice.PromptText), StreamingMode = false, StreamPlayback = false }
                };
                var binding = new VoiceBinding(settings.Voice.Name, settings.Voice.GptWeightsPath,
                    settings.Voice.SovitsWeightsPath,
                    [new ReferenceAudio(settings.Voice.ReferenceAudioPath, settings.Voice.PromptText)],
                    settings.Provider);

                var hash = QuestVoiceCacheIdentity.ComputeHash(settings, binding,
                    line.Speaker, line.Text, speechLanguage);
                var wavPath = Path.Combine(CacheDirectory, hash + ".wav");
                if (await IsValidCachedWavAsync(wavPath, cancellationToken))
                {
                    await RegisterCachedWAVAsync(cacheIndex, line.Key, speechLanguage, wavPath, cancellationToken);
                    completed++; hits++; failuresInARow = 0;
                    progress.Report(new(line.Key, "缓存命中", completed, lines.Count, hits, failed));
                    continue;
                }

                progress.Report(new(line.Key, "正在合成", completed, lines.Count, hits, failed));
                byte[] wavBytes;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await waitWhilePaused(cancellationToken);
                    if (liveService.IsRealtimeRequested)
                    {
                        progress.Report(new(line.Key, "P0 实时配音优先，P1 等待空闲", completed, lines.Count, hits, failed));
                        await Task.Delay(400, cancellationToken);
                        continue;
                    }

                    // 抢占只发生在请求边界，绝不终止在途 GPT-SoVITS HTTP/切权操作。
                    using (await liveService.BeginBackgroundSynthesisRequestAsync(cancellationToken))
                    {
                        for (var retry = 0; retry < 80 && (liveService.Status.IsRunning || liveService.Status.IsBusy); retry++)
                            await Task.Delay(150, cancellationToken);
                        if (liveService.IsRealtimeRequested) continue;
                        if (liveService.Status.IsRunning || liveService.Status.IsBusy)
                            throw new InvalidOperationException("P0 仍在排空，已阻止 P1 同时切换声线权重。");

                        // 无论用户何时点取消，已发出的请求必须排空，成功完成的这条仍写入缓存。
                        var result = await provider.SynthesizeAsync(settings,
                            new TtsRequest(line.Text, binding), null, CancellationToken.None);
                        wavBytes = new TtsAudioDecoder().Decode(result.Wav, result.SourceEncoding, CancellationToken.None).Bytes;
                    }
                    break;
                }

                var temporary = wavPath + "." + Guid.NewGuid().ToString("N") + ".partial";
                try
                {
                    await File.WriteAllBytesAsync(temporary, wavBytes, CancellationToken.None);
                    File.Move(temporary, wavPath, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                await RegisterCachedWAVAsync(cacheIndex, line.Key, speechLanguage, wavPath, CancellationToken.None);
                completed++; failuresInARow = 0;
                progress.Report(new(line.Key, "已生成", completed, lines.Count, hits, failed));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                failed++; failuresInARow++;
                progress.Report(new(line.Key, "失败：" + error.Message, completed, lines.Count, hits, failed));
                if (error is VoiceException { BackendUnknown: true } || failuresInARow >= 5)
                    throw new InvalidOperationException("后端状态不确定或连续五条合成失败；已停止后续请求。", error);
            }
        }
        return new(completed, lines.Count, hits, failed);
    }

    /// <summary>模型仅比对文件大小、修改时间与路径，不反复哈希 GB 级权重。</summary>
    private static string VersionOf(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}" : path;
        }
        catch (Exception) { return path; }
    }

    /// <summary>只有现有 WAV 完整通过格式检查才标记缓存命中。</summary>
    private static async Task<bool> IsValidCachedWavAsync(string path, CancellationToken token)
    {
        if (!File.Exists(path)) return false;
        try
        {
            _ = new WavProcessor().ValidateCompletedResponse(await File.ReadAllBytesAsync(path, token));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or VoiceException or InvalidDataException)
        {
            return false;
        }
    }
}

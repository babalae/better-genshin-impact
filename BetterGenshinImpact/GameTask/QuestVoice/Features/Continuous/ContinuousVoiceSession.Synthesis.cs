// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Serilog;
using Newtonsoft.Json;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Audio;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Continuous;

/// <summary>ContinuousVoiceSession：原神自动配音的业务类型。</summary>
public sealed partial class ContinuousVoiceSession
{
    /// <summary>串行处理最新候选，旧 HTTP 请求排空前不发出新的切权请求。</summary>
    private async Task SynthesisPumpAsync()
    {
        try
        {
            while (true)
            {
                ContinuousVoiceWork? work; CancellationToken token = default; Task wake;
                lock (_gate)
                {
                    work = _pending; wake = _changed.Task;
                    if (work is null && !_accepting) return;
                    if (work is not null)
                    {
                        _pending = null; _synthesizing = work;
                        _synthesisCancellation = new(); token = _synthesisCancellation.Token;
                    }
                }
                if (work is null) { await wake.ConfigureAwait(false); continue; }
                await PrepareAsync(work, token).ConfigureAwait(false);
                lock (_gate)
                {
                    _synthesizing = null;
                    _synthesisCancellation!.Dispose(); _synthesisCancellation = null;
                    Pulse();
                }
            }
        }
        finally { lock (_gate) { _synthesisDone = true; Pulse(); } }
    }

    /// <summary>在可取消后台流程中准备候选音频或图像张量。</summary>
    private async Task PrepareAsync(ContinuousVoiceWork work, CancellationToken token)
    {
        try
        {
            var resolved = await ResolveAsync(work, token).ConfigureAwait(false);
            DialogueKey key;
            lock (_gate)
            {
                CheckCurrent(work, token);
                work = resolved.Work; _synthesizing = work;
                Emit(ContinuousVoiceEventKind.Routed, work);
                CheckCurrent(work, token);
                key = CreateDialogueKey(work, resolved.Request);
                if (_currentKey == key && _currentPlayed)
                {
                    Emit(ContinuousVoiceEventKind.Duplicate, work); return;
                }
                if (_currentKey != key) { _currentKey = key; _currentPlayed = false; }
                Emit(ContinuousVoiceEventKind.SynthesisStarted, work);
                CheckCurrent(work, token);
            }
            var progress = new PhaseProgress(phase =>
            {
                if (phase != TtsPhase.Draining) return;
                lock (_gate) Emit(ContinuousVoiceEventKind.SynthesisDraining, work);
            });
            var streaming = settings.Protocol?.StreamPlayback == true
                && (engine is not IStreamingTtsEngine selectable || selectable.CanStream(resolved.Request));
            if (streaming && (engine is not IStreamingTtsEngine || player is not IStreamingAudioPlayer))
                throw new VoiceException(VoiceErrorCode.Configuration, "当前配音引擎或播放器不支持流式，请关闭流式开关。");
            // Ready回调只移交一个播放引用，合成泵仍等待同一HTTP正常EOF。
            // 候选失效不会使已经播放的旧流停止；它只禁止新的Ready进入槽位。
            void StreamingReady(StreamingPcmAudio source)
            {
                lock (_gate)
                {
                    if (IsDiscarded(work, token)) return;
                    source.Retain();
                    _ready = new(work, key, [], source);
                    _error = null; _errorDetail = null;
                    Emit(ContinuousVoiceEventKind.AudioReady, work, "流式首段已就绪，后续音频接收中");
                    Pulse();
                }
            }
            var result = streaming
                ? await ((IStreamingTtsEngine)engine).SynthesizeStreamingAsync(settings, resolved.Request, StreamingReady, progress, token).ConfigureAwait(false)
                : await engine.SynthesizeAsync(settings, resolved.Request, progress, token).ConfigureAwait(false);
            lock (_gate)
            {
                CheckCurrent(work, token);
                Emit(ContinuousVoiceEventKind.SynthesisCompleted, work, streaming ? "流式音频已全部接收" : null);
                CheckCurrent(work, token);
            }
            var audio = wav.ValidateCompletedResponse(result.Wav);
            lock (_gate)
            {
                CheckCurrent(work, token);
                Emit(ContinuousVoiceEventKind.Saving, work);
                CheckCurrent(work, token);
            }
            var path = await artifacts.SaveAsync(settings.Output.AudioDirectory, audio.Bytes, token).ConfigureAwait(false);
            lock (_gate)
            {
                work = work with { ArtifactPath = path };
                CheckCurrent(work, token);
                _error = null; _errorDetail = null;
                if (!streaming)
                {
                    _ready = new(work, key, audio.Bytes);
                    Emit(ContinuousVoiceEventKind.AudioReady, work);
                }
                // Reentrant callbacks may discard _ready. Playback checks revision and observation
                // leases itself before requesting any stop or issuing PlayAsync.
                Pulse();
            }
        }
        catch (OperationCanceledException) when (IsDiscarded(work, token))
        {
            lock (_gate) Emit(ContinuousVoiceEventKind.Discarded, work, "Candidate invalidated before playback");
        }
        catch (Exception exception)
        {
            // 仅补充文件诊断异常链，候选失效、后端未知与排空策略完全沿用下方逻辑。
            (logger ?? Log.Logger).Warning(exception, "QuestVoice synthesis failed revision={Revision} epoch={Epoch}",
                work.Candidate.Revision, work.Candidate.Epoch);
            lock (_gate)
            {
                // 失败源不可再接替旧句；只停止本次失败流，不误停其他候选的旧语音。
                if (_ready is { Stream: not null } failedReady && failedReady.Work.Candidate == work.Candidate)
                {
                    _ready = null; failedReady.Stream.Dispose();
                }
                if (_playing is { Stream: not null } failedPlaying && failedPlaying.Work.Candidate == work.Candidate)
                    RequestStop(failedPlaying, "Streaming response failed");
                var voice = exception as VoiceException;
                if (voice?.BackendUnknown != true && IsDiscarded(work, token))
                {
                    Emit(ContinuousVoiceEventKind.Discarded, work, exception.Message, voice?.Code ?? VoiceErrorCode.Internal);
                    return;
                }
                _error = voice?.Code ?? VoiceErrorCode.Internal; _errorDetail = exception.Message;
                if (voice?.BackendUnknown == true)
                {
                    _backendUnknown = true;
                    var pending = _pending; _pending = null;
                    if (pending is not null) Emit(ContinuousVoiceEventKind.Discarded, pending, "Backend state unknown");
                }
                Emit(ContinuousVoiceEventKind.CandidateFailed, work, exception.Message, _error);
                Pulse();
            }
        }
    }

    /// <summary>判断当前工作是否已取消或过期。</summary>
    private bool IsDiscarded(ContinuousVoiceWork work, CancellationToken token)
    {
        lock (_gate) return token.IsCancellationRequested || !IsCurrent(work.Candidate);
    }

    /// <summary>按接受姓名解析角色和实际声线，保留兜底原因。</summary>
    private async Task<(ContinuousVoiceWork Work, TtsRequest Request)> ResolveAsync(ContinuousVoiceWork work, CancellationToken token, bool notify = true)
    {
        lock (_gate) { CheckCurrent(work, token); if (notify) Emit(ContinuousVoiceEventKind.RoutingStarted, work); CheckCurrent(work, token); }
        var candidate = work.Candidate;
        if (settings.Routing is not { } routingSettings)
        {
            if (settings.RoutingConfigurationError is { } error) throw new VoiceException(VoiceErrorCode.Configuration, error);
            var configured = settings.Voice;
            var identity = new ContinuousVoiceIdentity(ContinuousVoiceIdentityKind.FixedVoice, acceptedName: AcceptedName(candidate));
            return (work with { Identity = identity }, new(candidate.Dialogue,
                new(configured.Name, configured.GptWeightsPath, configured.SovitsWeightsPath,
                    [new(configured.ReferenceAudioPath, configured.PromptText, configured.ReferenceEmotion)]), configured.RequestedEmotion));
        }
        if (routing is null) throw new VoiceException(VoiceErrorCode.Configuration, "Routing service unavailable.");
        var result = await routing.ResolveAsync(new(routingSettings,
            new(candidate.AcceptedName, candidate.NameStatus), settings.Voice), token).ConfigureAwait(false);
        lock (_gate) CheckCurrent(work, token);
        var canonical = result.Speaker.CanonicalId;
        var identityKey = StringComparer.OrdinalIgnoreCase.Equals(canonical, "default_npc")
            ? new ContinuousVoiceIdentity(ContinuousVoiceIdentityKind.UnknownSpeaker, canonical, AcceptedName(candidate))
            : new ContinuousVoiceIdentity(ContinuousVoiceIdentityKind.Canonical, canonical);
        return (work with { Identity = identityKey, Routing = result }, new(candidate.Dialogue, result.Voice, result.RequestedEmotion));
    }

    /// <summary>比较当前语义所有权的真实路由；新名字不会先取消仍有效的合成或Ready。</summary>
    public async Task<bool> MatchesCurrentDialogueAsync(ContinuousDialogueCandidate candidate, CancellationToken cancellationToken = default)
    {
        ContinuousVoiceWork? owner; DialogueKey? previousKey;
        lock (_gate)
        {
            if (!_accepting || !IsCurrent(candidate) || _backendUnknown) return false;
            owner = new[] { _pending, _synthesizing, _ready?.Work, _playing?.Work }
                .FirstOrDefault(work => work is not null && IsCurrent(work.Candidate));
            previousKey = _currentKey;
        }
        if (owner is not null && DialogueTextComparison.Compare(owner.Candidate.Dialogue, candidate.Dialogue)
            != DialogueTextRelation.Equivalent) return false;
        if (owner is null && previousKey is { } previous
            && DialogueTextComparison.Compare(previous.Text, candidate.Dialogue) != DialogueTextRelation.Equivalent) return false;
        // 路由只读本地目录及配置，不触碰串行合成泵的HTTP/模型所有权。
        var incoming = await ResolveAsync(new(candidate), cancellationToken, notify: false).ConfigureAwait(false);
        DialogueKey? ownedKey = previousKey;
        if (owner is not null)
        {
            if (owner.Identity is null)
            {
                var original = await ResolveAsync(owner, cancellationToken, notify: false).ConfigureAwait(false);
                ownedKey = CreateDialogueKey(original.Work, original.Request);
            }
            else
            {
                ownedKey = CreateOwnedDialogueKey(owner);
            }
        }
        var incomingKey = CreateDialogueKey(incoming.Work, incoming.Request);
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_accepting || !IsCurrent(candidate) || ownedKey is null) return false;
            // Pending可能在上述await期间已完成实际路由，最后以该冻结结果复核，避免目录切换竞态。
            var currentOwner = new[] { _pending, _synthesizing, _ready?.Work, _playing?.Work }
                .FirstOrDefault(work => work is not null && IsCurrent(work.Candidate));
            if (currentOwner is { Identity: not null }) ownedKey = CreateOwnedDialogueKey(currentOwner);
            else if (currentOwner is null && _currentKey is not null && _currentKey != previousKey) ownedKey = _currentKey;
            // 正文等价由Frame与原所有权共同验证；其原文和TTS请求均不改写。
            // OCR 姓名一侧缺失时，空名不应否定同一实际声线；两侧都有已接受姓名时仍按姓名区分。
            return SameOwnership(incomingKey, ownedKey);
        }
    }

    /// <summary>同一话轮、实际声线与文本的所有权；只把单侧缺失的 OCR 姓名视为可忽略。</summary>
    private static bool SameOwnership(DialogueKey incoming, DialogueKey owned)
    {
        if (incoming with { Identity = owned.Identity, Text = owned.Text } != owned) return false;
        if (incoming.Identity.Kind != owned.Identity.Kind
            || !StringComparer.OrdinalIgnoreCase.Equals(incoming.Identity.CanonicalId, owned.Identity.CanonicalId)) return false;
        if (string.IsNullOrEmpty(incoming.Identity.AcceptedName) || string.IsNullOrEmpty(owned.Identity.AcceptedName)) return true;
        return StringComparer.OrdinalIgnoreCase.Equals(incoming.Identity.AcceptedName, owned.Identity.AcceptedName);
    }

    /// <summary>已解析工作使用当时冻结的实际路由，不能重新读目录后假装旧声线已切换。</summary>
    private DialogueKey CreateOwnedDialogueKey(ContinuousVoiceWork owner)
    {
        var voice = owner.Routing?.Voice ?? new VoiceBinding(settings.Voice.Name, settings.Voice.GptWeightsPath,
            settings.Voice.SovitsWeightsPath, [new(settings.Voice.ReferenceAudioPath, settings.Voice.PromptText,
                settings.Voice.ReferenceEmotion)]);
        return CreateDialogueKey(owner, new(owner.Candidate.Dialogue, voice,
            owner.Routing is { } routed ? routed.RequestedEmotion : settings.Voice.RequestedEmotion));
    }
    /// <summary>真实话轮、规范角色和最终声线参数共同标识语音；与视觉版本无关。</summary>
    private static DialogueKey CreateDialogueKey(ContinuousVoiceWork work, TtsRequest request) => new(
        work.Candidate.Epoch, work.Candidate.DialogueOccurrence, work.Identity!,
        DialogueTextComparison.CreateComparisonKey(work.Candidate.Dialogue), request.Voice.GptWeightsPath,
        request.Voice.SovitsWeightsPath, JsonConvert.SerializeObject(request.Voice.References),
        request.Voice.Provider, request.Emotion);
    /// <summary>只采用通过 OCR 接受判定的姓名。</summary>
    private static string AcceptedName(ContinuousDialogueCandidate candidate) =>
        candidate.NameStatus == SpeakerNameStatus.Accepted ? Normalize(candidate.AcceptedName ?? "") : "";
    /// <summary>执行规范化与边缘空白处理，不作模糊角色猜测。</summary>
    private static string Normalize(string value) => value.Trim().Normalize(NormalizationForm.FormC);
    /// <summary>PhaseProgress：原神自动配音的业务类型。</summary>
    private sealed class PhaseProgress(Action<TtsPhase> report) : IProgress<TtsPhase>
    {
        /// <summary>Report：执行ContinuousVoiceSession.Synthesis中的对应校验或计算步骤。</summary>
        public void Report(TtsPhase value) => report(value);
    }
}

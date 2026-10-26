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
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;

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
                key = new(work.Candidate.Epoch, work.Identity!, Normalize(work.Candidate.Dialogue));
                if (_currentKey == key && _currentPlayed)
                {
                    Emit(ContinuousVoiceEventKind.Duplicate, work); return;
                }
                if (_currentKey != key) { _currentKey = key; _currentPlayed = false; }
                Emit(ContinuousVoiceEventKind.SynthesisStarted, work);
                CheckCurrent(work, token);
            }
            var result = await engine.SynthesizeAsync(settings, resolved.Request, new PhaseProgress(phase =>
            {
                if (phase != TtsPhase.Draining) return;
                lock (_gate) Emit(ContinuousVoiceEventKind.SynthesisDraining, work);
            }), token).ConfigureAwait(false);
            lock (_gate)
            {
                CheckCurrent(work, token);
                Emit(ContinuousVoiceEventKind.SynthesisCompleted, work);
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
                _ready = new(work, key, audio.Bytes);
                Emit(ContinuousVoiceEventKind.AudioReady, work);
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
    private async Task<(ContinuousVoiceWork Work, TtsRequest Request)> ResolveAsync(ContinuousVoiceWork work, CancellationToken token)
    {
        lock (_gate) { CheckCurrent(work, token); Emit(ContinuousVoiceEventKind.RoutingStarted, work); CheckCurrent(work, token); }
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

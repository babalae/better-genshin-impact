using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;

/// <summary>使用已安装的 Windows SAPI 声音，在专用 STA 中生成内存 PCM16 WAV。</summary>
public sealed class WindowsSapiTtsProvider : ITtsProvider
{
    /// <summary>SAPI SAFT22kHz16BitMono：22050 Hz、单声道、16 位 PCM。</summary>
    private const int AudioFormatType = 22;
    /// <summary>固定输出采样率，与内存流格式和 WAV 首部保持一致。</summary>
    private const int SampleRate = 22050;
    /// <summary>限制交给播放器的完整 PCM 数据体积。</summary>
    private const int MaxPcmBytes = 32 * 1024 * 1024;
    /// <summary>自动化绑定和系统语音token可能共享RCW；跨实例串行访问COM，等待支持取消。</summary>
    private static readonly SemaphoreSlim ComGate = new(1, 1);
    /// <summary>每个实例独立取消全部工作，不影响其他声音实例。</summary>
    private readonly CancellationTokenSource _shutdown = new();
    /// <summary>统一从注册表读取离线能力描述。</summary>
    public TtsProviderInfo Info => TtsProviderRegistry.GetInfo("windows-sapi");

    /// <summary>校验选音、倍率和合成期限；实际 token 必须在本次 STA 中再次精确匹配。</summary>
    public void Validate(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Provider?.VoiceId))
            throw Configuration("请选择已安装的 Windows SAPI 声音。");
        var rate = settings.Extra?.Rate ?? 1;
        if (!double.IsFinite(rate) || rate is < 0.5 or > 2)
            throw Configuration("Windows SAPI 语速倍率必须为 0.5～2。");
        if (settings.Timeouts.SynthesisSeconds is < 1 or > 600)
            throw Configuration("Windows SAPI 合成超时应为 1～600 秒。");
    }

    /// <summary>只枚举系统声音，无需有效的选中声音，也不访问任何服务器或资产。</summary>
    public async Task<IReadOnlyList<TtsVoiceInfo>> GetVoicesAsync(AppSettings settings, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
        return await RunOnSta(() => EnumerateVoices(linked.Token), linked.Token).ConfigureAwait(false);
    }

    /// <summary>只读检查所选声音仍已安装；不播放或生成测试音频。</summary>
    public async Task<string> ProbeAsync(AppSettings settings, CancellationToken token)
    {
        Validate(settings);
        var voices = await GetVoicesAsync(settings, token).ConfigureAwait(false);
        foreach (var voice in voices)
            if (string.Equals(voice.Id, settings.Provider!.VoiceId, StringComparison.OrdinalIgnoreCase))
                return $"Windows SAPI 已就绪：{voice.DisplayName}；实际合成请使用测试语音。";
        throw Configuration("所选 Windows SAPI 声音已不存在，请刷新声音列表后重新选择。");
    }

    /// <summary>合成完全结束并释放原生对象后才交付音频；取消和超时都在原 STA 中 purge。</summary>
    public async Task<TtsResult> SynthesizeAsync(AppSettings settings, TtsRequest request,
        IProgress<TtsPhase>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(settings);
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 2000)
            throw Configuration("Windows SAPI 合成台词长度必须为 1～2000 字符。");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        linked.CancelAfter(TimeSpan.FromSeconds(settings.Timeouts.SynthesisSeconds));
        var watch = Stopwatch.StartNew();
        try
        {
            return await RunOnSta(() => Synthesize(settings, request.Text, progress, watch, linked.Token), linked.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _shutdown.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken.IsCancellationRequested ? cancellationToken : _shutdown.Token);
        }
        catch (OperationCanceledException exception)
        {
            throw new VoiceException(VoiceErrorCode.Timeout, "Windows SAPI 合成超时，可重试或更换系统声音。", false, exception);
        }
    }

    /// <summary>所有 SAPI RCW 均在本次工作线程创建、使用并释放，不跨线程保存原生引用。</summary>
    private static TtsResult Synthesize(AppSettings settings, string text, IProgress<TtsPhase>? progress,
        Stopwatch watch, CancellationToken token)
    {
        dynamic? voice = null;
        dynamic? tokens = null;
        dynamic? stream = null;
        dynamic? format = null;
        try
        {
            token.ThrowIfCancellationRequested();
            voice = CreateCom("SAPI.SpVoice");
            tokens = voice.GetVoices("", "");
            var found = false;
            for (var index = 0; index < (int)tokens.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                object item = tokens.Item(index);
                try
                {
                    if (!string.Equals((string)((dynamic)item).Id, settings.Provider!.VoiceId, StringComparison.OrdinalIgnoreCase)) continue;
                    voice.Voice = item;
                    found = true;
                    break;
                }
                finally { ReleaseCom(item); }
            }
            if (!found) throw Configuration("所选 Windows SAPI 声音已不存在，请刷新声音列表后重新选择。");

            // 必须先连接内存输出再 Speak；不允许 SAPI 把声音送往默认扬声器。
            stream = CreateCom("SAPI.SpMemoryStream");
            format = CreateCom("SAPI.SpAudioFormat");
            format.Type = AudioFormatType;
            stream.Format = format;
            voice.AllowAudioOutputFormatChangesOnNextSet = false;
            voice.AudioOutputStream = stream;
            // SAPI 的 -10..10 是引擎相关等级；按对数映射倍率，1 倍为 0，0.5/2 倍为两端。
            voice.Rate = (int)Math.Round(Math.Clamp(10 * Math.Log2(settings.Extra?.Rate ?? 1), -10, 10));
            token.ThrowIfCancellationRequested();
            progress?.Report(TtsPhase.Synthesizing);
            // SVSFlagsAsync | SVSFIsNotXML：对白按纯文本合成，不执行文本中的 XML 指令。
            voice.Speak(text, 1 | 16);
            while (!(bool)voice.WaitUntilDone(50)) token.ThrowIfCancellationRequested();
            token.ThrowIfCancellationRequested();

            var pcm = (byte[])stream.GetData();
            if (pcm.Length == 0 || pcm.Length > MaxPcmBytes || pcm.Length % 2 != 0)
                throw new VoiceException(VoiceErrorCode.InvalidAudio, "Windows SAPI 返回的 PCM 音频为空、过大或不完整。");
            var audio = new WavProcessor().ValidateCompletedResponse(WrapPcm(pcm));
            token.ThrowIfCancellationRequested();
            return new(audio.Bytes, watch.Elapsed.TotalSeconds, watch.Elapsed.TotalSeconds,
                audio.DurationSeconds, audio.SampleRate, audio.Channels, ReferenceFallback.None);
        }
        finally
        {
            // 即使取消/超时，也要先停止该 voice，避免释放内存流时仍有原生写入。
            if (voice is not null)
            {
                try { voice.Speak("", 1 | 2 | 16); voice.WaitUntilDone(1000); }
                catch (COMException) { /* 原生对象已失败时仍继续释放本次全部 RCW。 */ }
            }
            ReleaseCom((object?)voice);
            ReleaseCom((object?)stream);
            ReleaseCom((object?)format);
            ReleaseCom((object?)tokens);
        }
    }

    /// <summary>读取实际安装的 SAPI token，逐个释放枚举项并保留可序列化的稳定 ID。</summary>
    private static IReadOnlyList<TtsVoiceInfo> EnumerateVoices(CancellationToken token)
    {
        dynamic? voice = null;
        dynamic? tokens = null;
        try
        {
            voice = CreateCom("SAPI.SpVoice");
            tokens = voice.GetVoices("", "");
            var result = new List<TtsVoiceInfo>();
            for (var index = 0; index < (int)tokens.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                object item = tokens.Item(index);
                try
                {
                    dynamic entry = item;
                    result.Add(new((string)entry.Id, (string)entry.GetDescription(0), GetLanguage((string)entry.GetAttribute("Language"))));
                }
                finally { ReleaseCom(item); }
            }
            return result.AsReadOnly();
        }
        finally { ReleaseCom((object?)tokens); ReleaseCom((object?)voice); }
    }

    /// <summary>SAPI Language 属性使用十六进制 LCID；未知值仍保留声音并标记未定语言。</summary>
    private static string GetLanguage(string language)
    {
        if (!int.TryParse(language.Split(';')[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var lcid)) return "und";
        try { return CultureInfo.GetCultureInfo(lcid).Name; }
        catch (CultureNotFoundException) { return "und"; }
    }

    /// <summary>SpMemoryStream 返回原始 PCM；补上固定格式 RIFF 首部后复用播放器校验。</summary>
    private static byte[] WrapPcm(byte[] pcm)
    {
        using var memory = new MemoryStream(pcm.Length + 44);
        using var writer = new BinaryWriter(memory);
        writer.Write("RIFF"u8);
        writer.Write(pcm.Length + 36);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(pcm.Length);
        writer.Write(pcm);
        return memory.ToArray();
    }

    /// <summary>为单次操作建立 STA；只有操作和 finally 结束后才完成 Task，调用方可安全释放实例。</summary>
    private static async Task<T> RunOnSta<T>(Func<T> action, CancellationToken token)
    {
        await ComGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try { token.ThrowIfCancellationRequested(); var result = action(); token.ThrowIfCancellationRequested(); completion.TrySetResult(result); }
                catch (OperationCanceledException) { completion.TrySetCanceled(token); }
                catch (COMException exception)
                {
                    completion.TrySetException(new VoiceException(VoiceErrorCode.SynthesisRejected,
                        "Windows SAPI 调用失败，请检查系统语音组件或重新选择声音。", false, exception));
                }
                catch (Exception exception) { completion.TrySetException(exception); }
            }) { IsBackground = true, Name = "QuestVoice Windows SAPI" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return await completion.Task.ConfigureAwait(false);
        }
        finally { ComGate.Release(); }
    }

    /// <summary>延迟创建系统自带 COM，不引用额外互操作程序集或安装外部软件。</summary>
    private static object CreateCom(string programId)
    {
        var type = Type.GetTypeFromProgID(programId)
            ?? throw new VoiceException(VoiceErrorCode.Unreachable, "Windows SAPI 语音组件不可用。");
        return Activator.CreateInstance(type)
            ?? throw new VoiceException(VoiceErrorCode.Unreachable, "无法创建 Windows SAPI 语音组件。");
    }

    /// <summary>只释放本次COM获取的引用；系统token的RCW可由其他并发枚举共享，不能FinalRelease。</summary>
    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }

    /// <summary>统一生成可恢复的配置错误。</summary>
    private static VoiceException Configuration(string message) => new(VoiceErrorCode.Configuration, message);
    /// <summary>只取消本实例工作；每个工作线程自行 purge，避免跨线程调用 COM。</summary>
    public void Shutdown() => _shutdown.Cancel();
    /// <summary>调用方须先等待在途请求完成，再释放取消源。</summary>
    public void Dispose() { _shutdown.Cancel(); _shutdown.Dispose(); }
}

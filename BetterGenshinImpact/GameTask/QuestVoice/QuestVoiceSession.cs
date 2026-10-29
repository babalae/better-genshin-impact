using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Audio;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Ocr;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;
using Serilog;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Diagnostics;

namespace BetterGenshinImpact.GameTask.QuestVoice;

/// <summary>一轮管线资源的所有者，先排空核心，再释放 OCR/音频和本轮临时文件。</summary>
public sealed class QuestVoiceSession(ContinuousFrameSession frames, ContinuousVoiceSession voice,
    ITtsProvider engine, DirectMlOcrEngine ocr, WasapiAudioPlayer player, string scratch) : IQuestVoiceSession
{
    /// <summary>订阅已经排好序的识别事件。</summary>
    public event Action<ContinuousFrameEvent>? FrameEvent { add => frames.Event += value; remove => frames.Event -= value; }
    /// <summary>订阅配音与播放事件。</summary>
    public event Action<ContinuousVoiceEvent>? VoiceEvent { add => voice.Event += value; remove => voice.Event -= value; }
    /// <summary>启动两个有界泵。</summary>
    public Task StartAsync(CancellationToken token) => frames.StartAsync(token);
    /// <summary>原帧稳定判定、OCR 与候选发布沿用来源项目。</summary>
    public Task ObserveAsync(long sequence, TimeSpan at, ReadOnlyMemory<byte> bytes, CancellationToken token, bool dialogueExitConfirmed = false)
        => frames.ObserveAsync(sequence, at, bytes, token, dialogueExitConfirmed);
    /// <summary>停止会立即取消播放，并排空已发出的 HTTP 响应。</summary>
    public Task StopAsync() => frames.StopAsync();
    /// <summary>应用退出时关闭在途 HTTP，正常停止不使用。</summary>
    public void AbortBackend() => engine.Shutdown();
    /// <summary>所有依赖均在它们的最后一个使用者完成后释放。</summary>
    public async ValueTask DisposeAsync()
    {
        try { await frames.DisposeAsync().ConfigureAwait(false); }
        finally
        {
            try { await voice.DisposeAsync().ConfigureAwait(false); }
            finally
            {
                player.Dispose();
                ocr.Dispose();
                engine.Dispose();
                // scratch 仅由工厂以固定 cache 根和随机 GUID 创建，不能来自外部配置。
                try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                { QuestVoiceLog.Diagnostic.Warning(exception, "QuestVoice 会话缓存清理失败：{Directory}", scratch); }
            }
        }
    }
}

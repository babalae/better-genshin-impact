using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;

namespace BetterGenshinImpact.GameTask.QuestVoice;

/// <summary>一轮配音所拥有的 OCR、合成和播放资源；无需游戏输入。</summary>
public interface IQuestVoiceSession : IAsyncDisposable
{
    /// <summary>帧识别的真实事件。</summary>
    event Action<ContinuousFrameEvent>? FrameEvent;
    /// <summary>配音协调的真实事件。</summary>
    event Action<ContinuousVoiceEvent>? VoiceEvent;
    /// <summary>启动单轮管线。</summary>
    Task StartAsync(CancellationToken token);
    /// <summary>接收已经复制的编码帧。</summary>
    Task ObserveAsync(long sequence, TimeSpan at, ReadOnlyMemory<byte> bytes, CancellationToken token, bool dialogueExitConfirmed = false);
    /// <summary>取消待播工作并排空已发出的后端请求。</summary>
    Task StopAsync();
    /// <summary>应用退出时关闭本地连接，正常停止不关闭后端。</summary>
    void AbortBackend();
}

/// <summary>在每次启动时创建新一轮资源，外置配置在此时重新读取。</summary>
public interface IQuestVoiceSessionFactory
{
    /// <summary>校验配置并创建资源，失败时不得留下后台任务。</summary>
    Task<IQuestVoiceSession> CreateAsync(CancellationToken token);
}

using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.QuestVoice.Core.Audio;

/// <summary>在既有单设备生命周期中播放增量PCM，不为每片创建输出。</summary>
public interface IStreamingAudioPlayer : IAudioPlayer
{
    /// <summary>播放至正常EOF；取消立即停止本地输出，不拥有HTTP取消权。</summary>
    Task PlayStreamingAsync(StreamingPcmAudio source, CancellationToken cancellationToken);
}

// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
namespace BetterGenshinImpact.GameTask.QuestVoice.Core.Audio;

/// <summary>IAudioPlayer：原神自动配音的业务类型。</summary>
public interface IAudioPlayer
{
    /// <summary>在工作线程使用默认共享输出播放完整 WAV，可取消并等待实际结束。</summary>
    Task PlayAsync(byte[] wav, CancellationToken cancellationToken);
    /// <summary>请求当前输出设备停止。</summary>
    void Stop();
}

// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;
/// <summary>AudioArtifactStore：原神自动配音的业务类型。</summary>
public sealed class AudioArtifactStore : IAudioArtifactStore
{
    /// <summary>将完整音频先写临时文件，再原子移动到本轮目录。</summary>
    public async Task<string> SaveAsync(string directory, byte[] wav, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.Combine(directory, $"voice-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.wav");
        var temporary = path + ".partial";
        try
        {
            await Task.Run(() => Directory.CreateDirectory(directory), cancellationToken).ConfigureAwait(false);
            await File.WriteAllBytesAsync(temporary, wav, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() => File.Move(temporary, path), cancellationToken).ConfigureAwait(false);
            return path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new VoiceException(VoiceErrorCode.SaveFailed, $"无法保存音频：{path}；{exception.Message}", false, exception);
        }
        finally
        {
            await Task.Run(() => { if (File.Exists(temporary)) File.Delete(temporary); }).ConfigureAwait(false);
        }
    }
}

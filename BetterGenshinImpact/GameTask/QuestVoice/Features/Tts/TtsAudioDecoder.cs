using System;
using System.IO;
using System.Threading;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using NAudio.Wave;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;

/// <summary>在进入现有播放器前，将明确声明的编码正规化为有界 PCM16 WAV。</summary>
public sealed class TtsAudioDecoder
{
    /// <summary>限制压缩音频和解码后数据，避免异常响应耗尽内存。</summary>
    public const int MaxEncodedBytes = 16 * 1024 * 1024, MaxDecodedBytes = 64 * 1024 * 1024;

    /// <summary>不猜测 JSON 或 MP3 为 PCM；必须完整接收后显式解码。</summary>
    public WavAudio Decode(byte[] bytes, TtsAudioEncoding encoding, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (bytes.Length == 0 || bytes.Length > MaxEncodedBytes) throw Invalid("音频为空或超出大小限制。");
        if (encoding == TtsAudioEncoding.Pcm16Wav) return new WavProcessor().ValidateCompletedResponse(bytes);
        if (encoding != TtsAudioEncoding.Mp3) throw Invalid("不支持的音频编码。");
        try
        {
            using var input = new MemoryStream(bytes, writable: false);
            using var reader = new Mp3FileReader(input);
            using var output = new MemoryStream();
            using (var writer = new WaveFileWriter(output, reader.WaveFormat))
            {
                var block = new byte[16384];
                int count;
                while ((count = reader.Read(block, 0, block.Length)) != 0)
                {
                    token.ThrowIfCancellationRequested();
                    if (output.Length + count > MaxDecodedBytes) throw Invalid("解码音频超出大小限制。");
                    writer.Write(block, 0, count);
                }
                writer.Flush();
            }
            return new WavProcessor().ValidateCompletedResponse(output.ToArray());
        }
        catch (OperationCanceledException) { throw; }
        catch (VoiceException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { throw new VoiceException(VoiceErrorCode.InvalidAudio, "MP3 音频解码失败。", false, exception); }
    }
    /// <summary>格式失败属于响应错误，不推断 GPT 后端状态。</summary>
    private static VoiceException Invalid(string detail) => new(VoiceErrorCode.InvalidAudio, detail);
}

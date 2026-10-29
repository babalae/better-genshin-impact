// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;
using SkiaSharp;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Ocr;

// Owned bytes are never exposed as mutable memory; all stages consume this one read.
internal sealed record ImageSnapshot(ReadOnlyMemory<byte> Bytes, ImageSourceSnapshot Source)
{
    /// <summary>异步读取并校验 OCR 执行诊断或图像快照。</summary>
    public static async Task<ImageSnapshot> ReadAsync(string path, CancellationToken token)
    {
        if (!new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            throw new OcrException(OcrErrorCode.Image, "图片只支持 PNG、JPEG 或 BMP。");
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
            return await Task.Run(() => Inspect(bytes, token), token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { throw new OcrException(OcrErrorCode.Image, $"无法读取图片：{exception.Message}", exception); }
    }

    /// <summary>Inspect：执行ImageSnapshot中的对应校验或计算步骤。</summary>
    private static ImageSnapshot Inspect(byte[] bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var stream = new SKMemoryStream(bytes);
        using var codec = SKCodec.Create(stream);
        if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Bmp))
            throw new OcrException(OcrErrorCode.Image, "图片无法解码或格式不支持。");
        if (codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > 80_000_000)
            throw new OcrException(OcrErrorCode.Image, "图片尺寸无效或超过 8000 万像素。");
        var source = new ImageSourceSnapshot(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), codec.Info.Width, codec.Info.Height);
        token.ThrowIfCancellationRequested();
        return new(bytes, source);
    }
}

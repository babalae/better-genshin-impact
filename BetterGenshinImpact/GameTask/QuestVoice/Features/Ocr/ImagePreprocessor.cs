// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;
using SkiaSharp;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Ocr;

/// <summary>ImagePreprocessor：原神自动配音的业务类型。</summary>
public sealed class ImagePreprocessor
{
    /// <summary>在可取消后台流程中准备候选音频或图像张量。</summary>
    public async Task<(int Width, int Height, IReadOnlyList<PixelRect> Bounds, IReadOnlyList<OcrTensor> Tensors)> PrepareAsync(
        string path, SceneProfile profile, CancellationToken cancellationToken)
    {
        var snapshot = await ImageSnapshot.ReadAsync(path, cancellationToken).ConfigureAwait(false);
        return await PrepareAsync(snapshot.Bytes, profile, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>在可取消后台流程中准备候选音频或图像张量。</summary>
    internal Task<(int Width, int Height, IReadOnlyList<PixelRect> Bounds, IReadOnlyList<OcrTensor> Tensors)> PrepareAsync(
        ReadOnlyMemory<byte> bytes, SceneProfile profile, CancellationToken cancellationToken, SpeakerInkPolicy? speakerInk = null) =>
        Task.Run(() => Prepare(bytes, profile, cancellationToken, speakerInk), cancellationToken);

    /// <summary>裁剪选定区域并生成推理张量。</summary>
    private static (int, int, IReadOnlyList<PixelRect>, IReadOnlyList<OcrTensor>) Prepare(ReadOnlyMemory<byte> bytes,
        SceneProfile profile, CancellationToken token, SpeakerInkPolicy? speakerInk)
    {
        using var stream = SKData.CreateCopy(bytes.Span);
        using var codec = SKCodec.Create(stream);
        if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Bmp))
            throw new OcrException(OcrErrorCode.Image, "图片无法解码或格式不支持。");
        if (codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > 80_000_000)
            throw new OcrException(OcrErrorCode.Image, "图片尺寸无效或超过 8000 万像素。");
        using var bitmap = new SKBitmap(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
            throw new OcrException(OcrErrorCode.Image, "图片数据不完整。");
        var bounds = profile.Regions.Select(r=>Scale(r.Bounds,profile.Width,profile.Height,bitmap.Width,bitmap.Height)).ToArray();
        var tensors = new List<OcrTensor>();
        for (var index = 0; index < bounds.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var rectangle = bounds[index];
            using var crop = new SKBitmap();
            if (!bitmap.ExtractSubset(crop,new SKRectI(rectangle.Left,rectangle.Top,rectangle.Right,rectangle.Bottom)))
                throw new OcrException(OcrErrorCode.Image,"无法裁剪布局区域。");
            using var foreground = speakerInk is not null && profile.Regions[index].Kind == OcrRegionKind.Speaker
                ? IsolateSpeaker(crop, speakerInk, token) : null;
            tensors.Add(ToTensor(foreground ?? crop));
        }
        return (bitmap.Width,bitmap.Height,bounds,tensors);
    }

    /// <summary>只保留指定金色姓名墨迹，减少背景对姓名识别的干扰。</summary>
    private static SKBitmap IsolateSpeaker(SKBitmap crop, SpeakerInkPolicy policy, CancellationToken token)
    {
        var range = new SceneColorRange(policy.MinRed, policy.MinGreen, policy.MinBlue, policy.MaxBlue,
            policy.MinRedGreen, policy.MinGreenBlue, policy.MinRedBlue, policy.MaxSpread);
        var foreground = new SKBitmap(crop.Width, crop.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        try
        {
            for (var y = 0; y < crop.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < crop.Width; x++)
                    foreground.SetPixel(x, y, ScenePixelAnalysis.IsColor(crop.GetPixel(x, y), range) ? SKColors.Black : SKColors.White);
            }
            return foreground;
        }
        catch { foreground.Dispose(); throw; }
    }

    /// <summary>按参考分辨率缩放识别区域并检查合法边界。</summary>
    public static PixelRect Scale(PixelRect box,int referenceWidth,int referenceHeight,int width,int height)
    {
        if (referenceWidth <= 0 || referenceHeight <= 0 || width <= 0 || height <= 0 || box.Left < 0 || box.Top < 0
            || box.Right > referenceWidth || box.Bottom > referenceHeight || box.Left >= box.Right || box.Top >= box.Bottom)
            throw new OcrException(OcrErrorCode.Template,"无法缩放非法区域。");
        return new((int)Math.Floor((double)box.Left*width/referenceWidth),(int)Math.Floor((double)box.Top*height/referenceHeight),
            (int)Math.Ceiling((double)box.Right*width/referenceWidth),(int)Math.Ceiling((double)box.Bottom*height/referenceHeight));
    }

    /// <summary>将裁剪图像转为 BGR、NCHW、48 高度的归一化张量。</summary>
    public static OcrTensor ToTensor(SKBitmap crop)
    {
        var ratio = (double)crop.Width / crop.Height;
        if (!double.IsFinite(ratio) || crop.Width <= 0 || crop.Height <= 0 || 48 * ratio > 32768)
            throw new OcrException(OcrErrorCode.Image,"区域为空或宽高比过大。");
        var width = (int)(48 * Math.Max(320.0 / 48, ratio));
        var resizedWidth = Math.Min(width,(int)Math.Ceiling(48 * ratio));
        using var resized = new SKBitmap(resizedWidth,48,SKColorType.Bgra8888,SKAlphaType.Premul);
        using (var canvas = new SKCanvas(resized))
        {
            canvas.Clear(SKColors.White);
            using var image = SKImage.FromBitmap(crop);
            canvas.DrawImage(image,new SKRect(0,0,resizedWidth,48),new SKSamplingOptions(SKFilterMode.Linear));
        }
        var data = new float[3*48*width];
        for (var y=0;y<48;y++)
            for (var x=0;x<resizedWidth;x++)
            {
                var pixel = resized.GetPixel(x,y);
                var index = y*width+x;
                data[index] = (pixel.Blue/255f-.5f)/.5f;
                data[48*width+index] = (pixel.Green/255f-.5f)/.5f;
                data[96*width+index] = (pixel.Red/255f-.5f)/.5f;
            }
        return new(data,width);
    }
}

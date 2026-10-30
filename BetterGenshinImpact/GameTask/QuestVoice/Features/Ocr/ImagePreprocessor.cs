// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;
using OpenCvSharp;
using SkiaSharp;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Ocr;

/// <summary>按选中布局裁剪姓名和正文行，输出交给内置 OCR 的三通道图像。</summary>
public sealed class ImagePreprocessor
{
    /// <summary>在后台解码同一帧并裁剪布局区域；返回的 Mat 由调用方释放。</summary>
    internal Task<(int Width, int Height, IReadOnlyList<PixelRect> Bounds, IReadOnlyList<Mat> Lines)> PrepareAsync(
        ReadOnlyMemory<byte> bytes, SceneProfile profile, CancellationToken cancellationToken, SpeakerInkPolicy? speakerInk = null) =>
        Task.Run(() => Prepare(bytes, profile, cancellationToken, speakerInk), cancellationToken);

    /// <summary>裁剪选定区域；姓名区可按场景给出的金色墨迹策略去掉背景。</summary>
    private static (int, int, IReadOnlyList<PixelRect>, IReadOnlyList<Mat>) Prepare(ReadOnlyMemory<byte> bytes,
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
        var lines = new List<Mat>();
        try
        {
            for (var index = 0; index < bounds.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                var rectangle = bounds[index];
                using var crop = new SKBitmap();
                if (!bitmap.ExtractSubset(crop,new SKRectI(rectangle.Left,rectangle.Top,rectangle.Right,rectangle.Bottom)))
                    throw new OcrException(OcrErrorCode.Image,"无法裁剪布局区域。");
                using var foreground = speakerInk is not null && profile.Regions[index].Kind == OcrRegionKind.Speaker
                    ? IsolateSpeaker(crop, speakerInk, token) : null;
                lines.Add(ToMat(foreground ?? crop));
            }
            return (bitmap.Width,bitmap.Height,bounds,lines);
        }
        catch
        {
            foreach (var line in lines) line.Dispose();
            throw;
        }
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

    /// <summary>把 BGRA 位图（可以是父图的子区域）复制为独立的 BGR Mat。</summary>
    public static Mat ToMat(SKBitmap bitmap)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            throw new OcrException(OcrErrorCode.Image, "区域为空。");
        using var bgra = Mat.FromPixelData(bitmap.Height, bitmap.Width, MatType.CV_8UC4, bitmap.GetPixels(), bitmap.RowBytes);
        return bgra.CvtColor(ColorConversionCodes.BGRA2BGR);
    }
}

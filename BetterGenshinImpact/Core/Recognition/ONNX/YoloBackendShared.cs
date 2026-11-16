using System;
using System.Collections.Generic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;
using Size = SixLabors.ImageSharp.Size;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

/// <summary>模型输入坐标系下的候选框（解码与 NMS 的中间表示）。</summary>
internal readonly record struct RawBox(int ClassIndex, float Confidence, float X, float Y, float W, float H);

/// <summary>
///     YOLO 后端共享的预处理与解码逻辑：ncnn（NcnnYoloPredictor）与 OpenVINO（OpenVinoYoloPredictor）
///     逐条复刻 YoloSharp 6.0.3（Yolo11RawBoundingBoxParser / NonMaxSuppressionService /
///     ImageAdjustmentService / SessionRunnerService），保证与被替换路线行为一致：
///     - 最近邻缩放到模型输入的内接矩形（ImageSharp ResizeMode.Max），内容区写入清零张量，四周补 0；
///     - RGB/255 归一化，CHW 平面布局；
///     - 输出 [1, 4+nc, N]：cx=T[i], cy=T[N+i], w=T[2N+i], h=T[3N+i]，类别 c 置信度=T[(4+c)N+i]；
///     - 贪心 NMS：按置信度降序，仅同类且 IoU &gt; 阈值时抑制；无最大框数限制；
///     - 坐标按 1/缩放比映射回原图并截断取整（与 YoloSharp 的 (int) 转换一致）。
/// </summary>
internal static class YoloBackendShared
{
    /// <summary>与 YoloConfiguration.Default 一致（生产调用 Detect 时不传配置）。</summary>
    public const float DefaultConfidence = 0.3f;
    public const float DefaultIou = 0.45f;

    /// <summary>
    ///     解析检测输出布局：优先用推理侧返回的 w/h/c（[1,4+nc,N] 通常落成 w=N、h=4+nc、c=1），
    ///     无法与类别数对齐时回退为「元素数 / (4+nc)」。
    /// </summary>
    public static (int AnchorCount, int AttributeCount) ResolveOutputLayout(int? infoWidth, int? infoHeight, int? infoChannels,
        int count, int classCount)
    {
        var expectedAttributes = 4 + classCount;
        if (infoWidth is > 0 && infoHeight is > 0)
        {
            (int Anchors, int Attributes)[] candidates =
            [
                (infoWidth.Value, infoHeight.Value * Math.Max(infoChannels ?? 1, 1)),
                (infoWidth.Value, infoHeight.Value),
                (infoWidth.Value * Math.Max(infoHeight.Value, 1), Math.Max(infoChannels ?? 1, 1))
            ];
            foreach (var (anchors, attributes) in candidates)
            {
                if (anchors > 0 && attributes == expectedAttributes && (long)anchors * attributes == count)
                    return (anchors, attributes);
            }
        }

        if (count > 0 && count % expectedAttributes == 0) return (count / expectedAttributes, expectedAttributes);
        throw new InvalidOperationException(
            $"YOLO 输出维度异常: {count} 不能被 {expectedAttributes} 整除");
    }

    /// <summary>按显式 anchor / 属性数解码（元信息路径），避免用元素总数反推布局。</summary>
    public static List<RawBox> DecodeAndNmsCore(float[] output, int anchorCount, int attributeCount,
        int classCount, float confidenceThreshold, float iouThreshold)
    {
        if (anchorCount <= 0 || attributeCount < 4 + classCount)
            throw new InvalidOperationException(
                $"YOLO 输出布局异常: anchors={anchorCount} attributes={attributeCount} 类别={classCount}");

        var candidates = new List<RawBox>();
        for (var i = 0; i < anchorCount; i++)
        {
            var cx = output[i];
            var cy = output[anchorCount + i];
            var w = output[2 * anchorCount + i];
            var h = output[3 * anchorCount + i];
            if (w == 0 || h == 0) continue;
            for (var c = 0; c < classCount; c++)
            {
                var confidence = output[(4 + c) * anchorCount + i];
                // 与 YoloSharp 一致：小于等于阈值直接排除
                if (confidence <= confidenceThreshold) continue;
                candidates.Add(new RawBox(c, confidence, cx - w / 2, cy - h / 2, w, h));
            }
        }

        // 按置信度降序，贪心保留：仅同类且 IoU 大于阈值时抑制
        candidates.Sort(static (a, b) => b.Confidence.CompareTo(a.Confidence));
        var kept = new List<RawBox>();
        foreach (var box in candidates)
        {
            var suppressed = false;
            foreach (var other in kept)
            {
                if (other.ClassIndex != box.ClassIndex) continue;
                if (IoU(box, other) > iouThreshold)
                {
                    suppressed = true;
                    break;
                }
            }

            if (!suppressed) kept.Add(box);
        }

        return kept;
    }

    /// <summary>交并比（与 YoloSharp 一致：面积非正时返回 0，交集面积/并集面积）。</summary>
    private static float IoU(RawBox a, RawBox b)
    {
        if (a.W <= 0 || a.H <= 0 || b.W <= 0 || b.H <= 0) return 0;
        var interX = Math.Max(0, Math.Min(a.X + a.W, b.X + b.W) - Math.Max(a.X, b.X));
        var interY = Math.Max(0, Math.Min(a.Y + a.H, b.Y + b.H) - Math.Max(a.Y, b.Y));
        var intersection = interX * interY;
        return intersection / (a.W * a.H + b.W * b.H - intersection);
    }

    /// <summary>
    ///     模型坐标 → 原图坐标的映射参数（复刻 ImageAdjustmentService）：
    ///     内接缩放比 reduction = min(modelW/w, modelH/h)，填充 = (model - 原图*reduction)/2，
    ///     回映射比 = max(w/modelW, h/modelH)。
    /// </summary>
    public static (int PadX, int PadY, float Ratio) ComputeAdjustment(Size imageSize, int modelWidth, int modelHeight)
    {
        var reductionRatio = Math.Min(modelWidth / (float)imageSize.Width, modelHeight / (float)imageSize.Height);
        var padX = (int)((modelWidth - imageSize.Width * reductionRatio) / 2);
        var padY = (int)((modelHeight - imageSize.Height * reductionRatio) / 2);
        var ratio = Math.Max(imageSize.Width / (float)modelWidth, imageSize.Height / (float)modelHeight);
        return (padX, padY, ratio);
    }

    /// <summary>与 YoloSharp SessionRunnerService.ResizeImage 一致：Max 内接 + 最近邻。</summary>
    public static Image<Rgb24> ResizeLetterbox(Image<Rgb24> image, int modelWidth, int modelHeight, out int padX, out int padY)
    {
        var resized = image.Clone(context => context.Resize(new ResizeOptions
        {
            Size = new Size(modelWidth, modelHeight),
            Mode = ResizeMode.Max,
            Sampler = KnownResamplers.NearestNeighbor,
        }));
        padX = (modelWidth - resized.Width) / 2;
        padY = (modelHeight - resized.Height) / 2;
        return resized;
    }

    /// <summary>RGB/255 写入 CHW 张量的内容区（四周保持 0，与 YoloSharp 清零后的张量一致）。YOLO 输入恒为正方形。</summary>
    public static void WriteToTensor(Image<Rgb24> image, float[] tensor, int modelWidth, int padX, int padY)
    {
        var plane = modelWidth * modelWidth;
        var width = image.Width;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                var rowBase = (y + padY) * modelWidth + padX;
                for (var x = 0; x < width; x++)
                {
                    tensor[rowBase + x] = row[x].R / 255f;
                    tensor[plane + rowBase + x] = row[x].G / 255f;
                    tensor[2 * plane + rowBase + x] = row[x].B / 255f;
                }
            }
        });
    }
}

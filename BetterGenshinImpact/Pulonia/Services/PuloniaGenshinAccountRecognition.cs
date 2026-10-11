using System;
using System.Collections.Generic;
using System.IO;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>一次切号拥有的模板集合，按当前截图尺寸缩放并释放全部图像。</summary>
internal sealed class PuloniaGenshinAccountRecognition : IDisposable
{
    /// <summary>模板所在的程序资源目录。</summary>
    private readonly string _directory = Path.Combine(AppContext.BaseDirectory, "GameTask", "GenshinAccountSwitch", "Assets", "1920x1080");
    /// <summary>当前尺寸下由本次操作独占的模板对象。</summary>
    private readonly Dictionary<string, RecognitionObject> _templates = new(StringComparer.Ordinal);
    /// <summary>模板当前适配的截图宽度。</summary>
    private int _width;

    /// <summary>从账号脚本迁移的模板中查找指定按钮；结果必须在截图释放前使用。</summary>
    internal Region Find(ImageRegion frame, string name, Rect? region = null)
    {
        if (_width != frame.Width) { DisposeTemplates(); _width = frame.Width; }
        if (!_templates.TryGetValue(name, out var template))
        {
            using var stream = File.OpenRead(Path.Combine(_directory, name + ".png"));
            using var original = Mat.FromStream(stream, ImreadModes.Color);
            var scaled = new Mat();
            try
            {
                Cv2.Resize(original, scaled, new Size(Math.Max(1, (int)Math.Round(original.Width * frame.Width / 1920d)),
                    Math.Max(1, (int)Math.Round(original.Height * frame.Width / 1920d))));
                template = RecognitionObject.TemplateMatch(scaled);
                template.Threshold = 0.9;
                template.Name = name;
                _templates.Add(name, template);
            }
            catch { scaled.Dispose(); throw; }
        }
        template.RegionOfInterest = region is { } rectangle ? Scale(frame, rectangle) : default;
        return frame.Find(template);
    }

    /// <summary>将 1080p 参考区域转换到当前截图，避免直接使用固定像素。</summary>
    internal static Rect Scale(ImageRegion frame, Rect rectangle)
    {
        var scale = frame.Width / 1920d;
        var x = Math.Clamp((int)Math.Round(rectangle.X * scale), 0, frame.Width - 1);
        var y = Math.Clamp((int)Math.Round(rectangle.Y * scale), 0, frame.Height - 1);
        return new Rect(x, y, Math.Max(1, Math.Min(frame.Width - x, (int)Math.Round(rectangle.Width * scale))),
            Math.Max(1, Math.Min(frame.Height - y, (int)Math.Round(rectangle.Height * scale))));
    }

    /// <summary>释放由模板识别初始化产生的灰度图、彩色图和绘制对象。</summary>
    private void DisposeTemplates()
    {
        foreach (var template in _templates.Values)
        {
            template.TemplateImageGreyMat?.Dispose();
            template.TemplateImageMat?.Dispose();
            template.MaskMat?.Dispose();
            template.DrawOnWindowPen.Dispose();
        }
        _templates.Clear();
    }

    /// <summary>结束一次识别操作，不持有跨账号模板或截图。</summary>
    public void Dispose() => DisposeTemplates();
}

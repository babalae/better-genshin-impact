using System;
using System.Text.RegularExpressions;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.MapMask;

/// <summary>从右下角场景标题选择独立地图；只在后台定位线程执行 OCR。</summary>
internal sealed class BigMapSceneReader : IDisposable
{
    /// <summary>相同标题沿用已识别场景，拖动地图不会反复调用 OCR。</summary>
    private Mat? _lastCaption;

    /// <summary>最近一次可靠读取的场景；没有标题时保留场景，避免详情面板造成误切图。</summary>
    private MapTypes? _lastScene;

    /// <summary>识别可注入，测试与生产使用相同的标题裁剪和场景解释流程。</summary>
    internal Func<Mat, string> Recognize { get; init; } = image => OcrFactory.Paddle.OcrWithoutDetector(image);

    /// <summary>只裁剪场景名，排除右侧地球按钮及底部 UID。</summary>
    internal static Rect GetCaptionArea(Size size)
    {
        var scale = Math.Min(size.Width / 1920d, size.Height / 1080d);
        return new Rect((int)(size.Width - 500 * scale), (int)(size.Height - 106 * scale),
            (int)(380 * scale), Math.Max(1, (int)(46 * scale)))
            .Intersect(new Rect(0, 0, size.Width, size.Height));
    }

    /// <summary>将中英文场景名归一；普通地区标题属于提瓦特，空标题不改变场景。</summary>
    internal static MapTypes? Parse(string text)
    {
        text = Regex.Replace(text, @"\s+", "").Replace("宮", "宫");
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.Contains("渊下宫") || text.Contains("Enkanomiya", StringComparison.OrdinalIgnoreCase)) return MapTypes.Enkanomiya;
        if (text.Contains("霜月") || text.Contains("Frostmoon", StringComparison.OrdinalIgnoreCase)) return MapTypes.MoonCanon;
        if (text.Contains("层岩巨渊") || text.Contains("Chasm", StringComparison.OrdinalIgnoreCase)) return MapTypes.TheChasm;
        if (text.Contains("旧日之海") || text.Contains("Bygone", StringComparison.OrdinalIgnoreCase)) return MapTypes.SeaOfBygoneEras;
        if (text.Contains("远古圣山") || text.Contains("Sacred", StringComparison.OrdinalIgnoreCase)) return MapTypes.AncientSacredMountain;
        if (text.Contains("空之神殿") || text.Contains("TempleofSpace", StringComparison.OrdinalIgnoreCase)) return MapTypes.TempleOfSpace;
        return MapTypes.Teyvat;
    }

    /// <summary>缓存标题白色字形，背景变化时仍重新确认；无文字时不沿用缓存图片。</summary>
    internal MapTypes? Read(Mat gray)
    {
        // 详情面板会覆盖场景标题，不能把面板正文当作提瓦特地区名。
        if (BigMapUiLayout.GetMapArea(gray).Width < gray.Width) return _lastScene;
        using var caption = new Mat(gray, GetCaptionArea(gray.Size()));
        using var letters = caption.Threshold(210, 255, ThresholdTypes.Binary);
        if (_lastCaption != null && _lastCaption.Size() == letters.Size())
        {
            using var difference = new Mat();
            Cv2.Absdiff(letters, _lastCaption, difference);
            if (Cv2.CountNonZero(difference) < letters.Total() * 0.005) return _lastScene;
        }
        using var enlarged = new Mat();
        var scale = Math.Max(1, 46d / caption.Height);
        Cv2.Resize(caption, enlarged, default, scale, scale, InterpolationFlags.Cubic);
        var scene = Parse(Recognize(enlarged));
        if (scene.HasValue)
        {
            _lastCaption?.Dispose();
            _lastCaption = letters.Clone();
            _lastScene = scene;
        }
        return scene;
    }

    /// <summary>释放本实例拥有的标题缓存。</summary>
    public void Dispose() => _lastCaption?.Dispose();
}

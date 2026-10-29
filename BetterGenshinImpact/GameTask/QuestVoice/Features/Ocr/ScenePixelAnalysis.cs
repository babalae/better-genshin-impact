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

/// <summary>SceneInkComponent：原神自动配音的业务类型。</summary>
internal sealed record SceneInkComponent(int Left, int Top, int Right, int Bottom, int Pixels)
{
    /// <summary>Height：当前业务对象的配置、身份或状态值。</summary>
    public int Height => Bottom - Top;
}

// All measurements use reference pixels. Sampling preserves the same coordinate space at supported scales.
internal sealed class ScenePixelAnalysis(SKBitmap bitmap, SceneCatalogDefinition catalog, CancellationToken token)
{
    /// <summary>Pixel：执行ScenePixelAnalysis中的对应校验或计算步骤。</summary>
    private SKColor Pixel(int x, int y) => bitmap.GetPixel(
        Math.Min(bitmap.Width - 1, (int)((x + 0.5) * bitmap.Width / catalog.Width)),
        Math.Min(bitmap.Height - 1, (int)((y + 0.5) * bitmap.Height / catalog.Height)));

    /// <summary>IsColor：执行ScenePixelAnalysis中的对应校验或计算步骤。</summary>
    internal static bool IsColor(SKColor color, SceneColorRange range) => color.Alpha == byte.MaxValue
        && color.Red >= range.MinRed && color.Green >= range.MinGreen
        && color.Blue >= range.MinBlue && color.Blue <= range.MaxBlue
        && color.Red - color.Green >= range.MinRedGreen && color.Green - color.Blue >= range.MinGreenBlue
        && color.Red - color.Blue >= range.MinRedBlue
        && Math.Max(color.Red, Math.Max(color.Green, color.Blue)) - Math.Min(color.Red, Math.Min(color.Green, color.Blue)) <= range.MaxSpread;

    /// <summary>HasBorder：执行ScenePixelAnalysis中的对应校验或计算步骤。</summary>
    public bool HasBorder(Dictionary<string, double> evidence)
    {
        var t = catalog.Transform;
        var edges = new[] { new PixelRect(0, 0, catalog.Width, t.BorderDepth),
            new PixelRect(0, catalog.Height - t.BorderDepth, catalog.Width, catalog.Height),
            new PixelRect(0, 0, t.BorderDepth, catalog.Height),
            new PixelRect(catalog.Width - t.BorderDepth, 0, catalog.Width, catalog.Height) };
        var hasBorder = false;
        for (var index = 0; index < edges.Length; index++)
        {
            var edge = edges[index];
            var dark = 0;
            for (var y = edge.Top; y < edge.Bottom; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = edge.Left; x < edge.Right; x++)
                {
                    var pixel = Pixel(x, y);
                    if (Math.Max(pixel.Red, Math.Max(pixel.Green, pixel.Blue)) <= t.BorderDarkMaximum) dark++;
                }
            }
            var fraction = (double)dark / (edge.Width * edge.Height);
            evidence["Border" + index + ".DarkFraction"] = fraction;
            hasBorder |= fraction >= t.BorderDarkFraction;
        }
        return hasBorder;
    }

    /// <summary>HasSeparator：执行ScenePixelAnalysis中的对应校验或计算步骤。</summary>
    public bool HasSeparator(SceneSeparator separator, Dictionary<string, double> evidence)
    {
        var bounds = separator.Bounds;
        var center = (bounds.Left + bounds.Right) / 2;
        var rows = 0;
        var longest = 0;
        for (var y = bounds.Top; y < bounds.Bottom; y++)
        {
            token.ThrowIfCancellationRequested();
            var left = LongestRun(bounds.Left, center - separator.CenterGap, y, separator);
            var right = LongestRun(center + separator.CenterGap, bounds.Right, y, separator);
            longest = Math.Max(longest, Math.Min(left, right));
            if (left >= separator.MinimumSideRun && right >= separator.MinimumSideRun) rows++;
        }
        var prefix = separator.HasTitle ? "TitleSeparator" : "StandardSeparator";
        evidence[prefix + ".Rows"] = rows;
        evidence[prefix + ".SideRun"] = longest;
        return rows >= separator.MinimumRows && rows <= separator.MaximumThickness;
    }

    /// <summary>LongestRun：执行ScenePixelAnalysis中的对应校验或计算步骤。</summary>
    private int LongestRun(int start, int end, int y, SceneSeparator separator)
    {
        var longest = 0;
        var run = 0;
        for (var x = start; x < end; x++)
        {
            var pixel = Pixel(x, y);
            var surrounding = Math.Min(GoldIntensity(Pixel(x, y - separator.ContrastOffset)),
                GoldIntensity(Pixel(x, y + separator.ContrastOffset)));
            run = IsColor(pixel, catalog.Colors.SeparatorGold)
                && GoldIntensity(pixel) - surrounding >= separator.MinimumContrast ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }
        return longest;
    }

    /// <summary>GoldIntensity：执行ScenePixelAnalysis中的对应校验或计算步骤。</summary>
    private static int GoldIntensity(SKColor pixel) => pixel.Red + pixel.Green - pixel.Blue;

    /// <summary>Components：执行ScenePixelAnalysis中的对应校验或计算步骤。</summary>
    public List<SceneInkComponent> Components(PixelRect area, SceneColorRange range)
    {
        var mask = new bool[area.Width * area.Height];
        for (var y = 0; y < area.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < area.Width; x++) mask[y * area.Width + x] = IsColor(Pixel(area.Left + x, area.Top + y), range);
        }
        var result = new List<SceneInkComponent>();
        var stack = new Stack<int>();
        for (var offset = 0; offset < mask.Length; offset++)
        {
            if (offset % area.Width == 0) token.ThrowIfCancellationRequested();
            if (!mask[offset]) continue;
            mask[offset] = false;
            stack.Push(offset);
            var left = area.Width;
            var right = 0;
            var top = area.Height;
            var bottom = 0;
            var count = 0;
            while (stack.TryPop(out var current))
            {
                if ((count++ & 255) == 0) token.ThrowIfCancellationRequested();
                var x = current % area.Width;
                var y = current / area.Width;
                left = Math.Min(left, x); right = Math.Max(right, x);
                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var nx = x + dx;
                        var ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= area.Width || ny >= area.Height) continue;
                        var next = ny * area.Width + nx;
                        if (!mask[next]) continue;
                        mask[next] = false;
                        stack.Push(next);
                    }
            }
            var g = catalog.Geometry;
            if (count >= g.ComponentMinimumPixels && right - left + 1 <= g.ComponentMaximumWidth
                && bottom - top + 1 >= g.ComponentMinimumHeight && bottom - top + 1 <= g.ComponentMaximumHeight)
                result.Add(new(area.Left + left, area.Top + top, area.Left + right + 1, area.Top + bottom + 1, count));
        }
        return result;
    }
}

using BetterGenshinImpact.Core.Recognition.OCR;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.InventoryMaterialStats;

/// <summary>
/// 背包 OCR：格子数量是深色字（数量识别器已抽黑字），右侧详情标题是浅色字，先抽白字再送给 Paddle。
/// </summary>
internal static class InventoryMaterialStatsOcr
{
    public static string ReadWhiteTitle(Mat bgr, IOcrService ocr)
    {
        using var prepared = PrepareWhiteTextOnLightBackground(bgr);
        var text = Clean(ocr.OcrWithoutDetector(prepared));
        if (IsUsefulTitle(text))
        {
            return text;
        }

        text = Clean(ocr.Ocr(prepared));
        if (IsUsefulTitle(text))
        {
            return text;
        }

        return Clean(ocr.Ocr(bgr));
    }

    private static Mat PrepareWhiteTextOnLightBackground(Mat bgr)
    {
        using var hsv = bgr.CvtColor(ColorConversionCodes.BGR2HSV);
        // 低饱和、高明度：白字及抗锯齿浅灰，滤掉紫底和绿「食物」标签。
        using var mask = hsv.InRange(new Scalar(0, 0, 170), new Scalar(180, 80, 255));
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(2, 2));
        using var cleaned = new Mat();
        Cv2.MorphologyEx(mask, cleaned, MorphTypes.Open, kernel);

        var bounds = Cv2.BoundingRect(cleaned);
        Mat binary;
        if (bounds.Width > 4 && bounds.Height > 4)
        {
            const int pad = 4;
            var x = Math.Max(0, bounds.X - pad);
            var y = Math.Max(0, bounds.Y - pad);
            var w = Math.Min(cleaned.Width - x, bounds.Width + pad * 2);
            var h = Math.Min(cleaned.Height - y, bounds.Height + pad * 2);
            using var crop = cleaned.SubMat(y, y + h, x, x + w);
            binary = new Mat();
            Cv2.BitwiseNot(crop, binary);
        }
        else
        {
            binary = new Mat();
            Cv2.BitwiseNot(cleaned, binary);
        }

        var scaled = binary.Resize(new Size(binary.Width * 2, binary.Height * 2), interpolation: InterpolationFlags.Linear);
        binary.Dispose();
        return scaled;
    }

    private static string Clean(string? text)
    {
        return InventoryMaterialStatsNameMatcher.StripWhiteSpace(text);
    }

    private static bool IsUsefulTitle(string text)
    {
        return !string.IsNullOrWhiteSpace(text) && !text.All(char.IsDigit);
    }

    /// <summary>
    /// 养成道具页左下：四角星右侧是原石数字（再右侧是带 + 的圆，不能进框），金币右侧是摩拉。
    /// 药丸是深色底白字。
    /// </summary>
    public static CurrencyOcrResult ReadPrimogemAndMora(Mat screen, IOcrService ocr)
    {
        var y0 = Math.Clamp((int)(screen.Height * 0.88), 0, Math.Max(screen.Height - 1, 0));
        var x1 = Math.Clamp((int)(screen.Width * 0.42), 16, screen.Width);
        var strip = screen.SubMat(y0, screen.Height, 0, x1).Clone();

        var source = "pills";
        if (!TryCropByPills(strip, out var primogemRoi, out var moraRoi) &&
            !TryCropByIcons(strip, out primogemRoi, out moraRoi))
        {
            source = "fallback-roi";
            primogemRoi = Crop(screen, 0.092, 0.918, 0.058, 0.050);
            moraRoi = Crop(screen, 0.215, 0.918, 0.110, 0.050);
        }

        var primogem = ReadBrightDigitsOnDark(primogemRoi, ocr);
        var mora = ReadBrightDigitsOnDark(moraRoi, ocr);
        return new CurrencyOcrResult(primogem.Value, mora.Value, primogem.Raw, mora.Raw, source, strip,
            primogemRoi, moraRoi, primogem.Binary, mora.Binary);
    }

    public static (int? Value, string Raw, Mat? Binary) ReadBrightDigitsOnDark(Mat bgr, IOcrService ocr)
    {
        if (bgr.Empty() || bgr.Width < 8 || bgr.Height < 8)
        {
            return (null, string.Empty, null);
        }

        (int? Value, string Raw, Mat? Binary) best = (null, string.Empty, null);
        foreach (var (minV, maxS) in new[] { (210, 50), (195, 70), (180, 90) })
        {
            var attempt = ReadBrightDigitsOnce(bgr, ocr, minV, maxS);
            if (IsBetterDigitRead(attempt.Value, best.Value))
            {
                best.Binary?.Dispose();
                best = attempt;
            }
            else
            {
                attempt.Binary?.Dispose();
            }
        }

        return best;
    }

    private static (int? Value, string Raw, Mat? Binary) ReadBrightDigitsOnce(
        Mat bgr, IOcrService ocr, int minValue, int maxSaturation)
    {
        using var hsv = bgr.CvtColor(ColorConversionCodes.BGR2HSV);
        using var whiteText = hsv.InRange(new Scalar(0, 0, minValue), new Scalar(180, maxSaturation, 255));
        // 不要 Open：6 的细开口会被抠掉，Paddle 会当成 0。
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(2, 1));
        using var cleaned = new Mat();
        Cv2.MorphologyEx(whiteText, cleaned, MorphTypes.Close, kernel);

        var bounds = Cv2.BoundingRect(cleaned);
        Mat binary;
        if (bounds.Width > 4 && bounds.Height > 4)
        {
            const int pad = 3;
            var x = Math.Max(0, bounds.X - pad);
            var y = Math.Max(0, bounds.Y - pad);
            var w = Math.Min(cleaned.Width - x, bounds.Width + pad * 2);
            var h = Math.Min(cleaned.Height - y, bounds.Height + pad * 2);
            using var crop = cleaned.SubMat(y, y + h, x, x + w);
            binary = new Mat();
            Cv2.BitwiseNot(crop, binary);
        }
        else
        {
            binary = new Mat();
            Cv2.BitwiseNot(cleaned, binary);
        }

        using var scaled = binary.Resize(
            new Size(Math.Max(binary.Width * 3, 8), Math.Max(binary.Height * 3, 8)),
            interpolation: InterpolationFlags.Cubic);
        var rawNoDet = ocr.OcrWithoutDetector(scaled) ?? string.Empty;
        var rawDet = ocr.Ocr(scaled) ?? string.Empty;
        TryParseDigits(rawNoDet, out var v1);
        TryParseDigits(rawDet, out var v2);
        var value = IsBetterDigitRead(v2 >= 0 ? v2 : null, v1 >= 0 ? v1 : null)
            ? v2
            : v1;
        var raw = $"nodet={rawNoDet} det={rawDet} hsvV>={minValue},S<={maxSaturation}";
        return (value >= 0 ? value : null, raw, binary);
    }

    private static bool IsBetterDigitRead(int? candidate, int? current)
    {
        if (candidate is null or < 0)
        {
            return false;
        }

        if (current is null or < 0)
        {
            return true;
        }

        var cLen = DigitLength(candidate.Value);
        var nLen = DigitLength(current.Value);
        if (cLen != nLen)
        {
            return cLen > nLen;
        }

        return false;
    }

    private static int DigitLength(int value) => value == 0 ? 1 : (int)Math.Log10(value) + 1;

    private static bool TryCropByPills(Mat strip, out Mat primogemRoi, out Mat moraRoi)
    {
        primogemRoi = null!;
        moraRoi = null!;
        using var hsv = strip.CvtColor(ColorConversionCodes.BGR2HSV);
        // 胶囊比草地/青空都暗，色相会随地图变，不能锁死青色。
        using var mask = hsv.InRange(new Scalar(0, 0, 30), new Scalar(180, 200, 170));
        var kx = Math.Clamp(strip.Width / 80, 7, 15);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(kx, 5));
        using var closed = new Mat();
        Cv2.MorphologyEx(mask, closed, MorphTypes.Close, kernel);
        var contours = closed.FindContoursAsArray(RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        if (contours == null || contours.Length == 0)
        {
            return false;
        }

        var minH = Math.Max(14, strip.Height / 8);
        var maxH = Math.Max(minH + 1, strip.Height / 2);
        var pills = contours
            .Select(Cv2.BoundingRect)
            .Where(r => r.Height >= minH && r.Height <= maxH
                        && r.Width >= r.Height * 2.8
                        && r.Y + r.Height / 2 > strip.Height * 0.25)
            .OrderByDescending(r => r.Width * r.Height)
            .Take(2)
            .OrderBy(r => r.X)
            .ToList();
        if (pills.Count < 2)
        {
            return false;
        }

        var a = pills[0];
        var b = pills[1];
        if (Math.Abs(a.Height - b.Height) > Math.Max(a.Height, b.Height) * 0.35
            || a.Width * a.Height < b.Width * b.Height * 0.45)
        {
            return false;
        }

        primogemRoi = CropPillDigits(strip, pills[0], skipRightPlus: true);
        moraRoi = CropPillDigits(strip, pills[1], skipRightPlus: false);
        return primogemRoi.Width >= 8 && moraRoi.Width >= 8;
    }

    private static Mat CropPillDigits(Mat strip, Rect pill, bool skipRightPlus)
    {
        var iconW = Math.Max(8, (int)(pill.Height * 1.05));
        var x0 = Math.Clamp(pill.X + iconW, 0, strip.Width - 8);
        var x1 = pill.X + pill.Width;
        if (skipRightPlus)
        {
            x1 -= (int)(pill.Height * 1.2);
        }

        x1 = Math.Clamp(x1, x0 + 8, strip.Width);
        var y0 = Math.Clamp(pill.Y - 2, 0, strip.Height - 1);
        var y1 = Math.Clamp(pill.Bottom + 2, y0 + 8, strip.Height);
        return strip.SubMat(y0, y1, x0, x1).Clone();
    }

    private static bool TryCropByIcons(Mat strip, out Mat primogemRoi, out Mat moraRoi)
    {
        primogemRoi = null!;
        moraRoi = null!;
        using var hsv = strip.CvtColor(ColorConversionCodes.BGR2HSV);
        if (!TryFindColoredBlob(hsv, new Scalar(80, 40, 90), new Scalar(130, 255, 255), out var star) ||
            !TryFindColoredBlob(hsv, new Scalar(8, 70, 80), new Scalar(40, 255, 255), out var coin) ||
            coin.X <= star.Right)
        {
            return false;
        }

        var y = Math.Clamp(Math.Min(star.Y, coin.Y) - 6, 0, strip.Height - 1);
        var bottom = Math.Min(strip.Height, Math.Max(star.Bottom, coin.Bottom) + 6);
        var h = Math.Max(8, bottom - y);

        var digitLeft = Math.Min(strip.Width - 8, star.Right + 2);
        var plusLeft = TryFindPlusCircle(strip, digitLeft, coin.X, out var plus)
            ? plus.X - 3
            : coin.X - 8;
        var digitRight = Math.Clamp(plusLeft, digitLeft + 8, strip.Width);

        primogemRoi = strip.SubMat(y, y + h, digitLeft, digitRight).Clone();
        var moraLeft = Math.Min(strip.Width - 8, coin.Right + 2);
        moraRoi = strip.SubMat(y, y + h, moraLeft, strip.Width).Clone();
        return primogemRoi.Width >= 8 && moraRoi.Width >= 8;
    }

    private static bool TryFindColoredBlob(Mat hsv, Scalar low, Scalar high, out Rect rect)
    {
        rect = default;
        using var mask = hsv.InRange(low, high);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
        using var closed = new Mat();
        Cv2.MorphologyEx(mask, closed, MorphTypes.Close, kernel);
        var contours = closed.FindContoursAsArray(RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        if (contours == null || contours.Length == 0)
        {
            return false;
        }

        var minSide = Math.Max(10, hsv.Height / 8);
        var maxSide = Math.Max(minSide + 1, hsv.Height / 2);
        var best = contours
            .Select(Cv2.BoundingRect)
            .Where(r =>
            {
                var ar = r.Width / (double)Math.Max(r.Height, 1);
                return r.Width >= minSide && r.Height >= minSide
                       && r.Width <= maxSide && r.Height <= maxSide
                       && ar is > 0.65 and < 1.5
                       && r.Y > hsv.Height * 0.15;
            })
            .OrderByDescending(r => r.Width * r.Height)
            .FirstOrDefault();
        if (best.Width <= 0)
        {
            return false;
        }

        rect = best;
        return true;
    }

    private static bool TryFindPlusCircle(Mat strip, int x0, int x1, out Rect plus)
    {
        plus = default;
        if (x1 - x0 < 16)
        {
            return false;
        }

        using var roi = strip.ColRange(x0, x1);
        using var hsv = roi.CvtColor(ColorConversionCodes.BGR2HSV);
        // + 圆是浅米色，数字是更亮的白；上限避开纯白以免把 0 当成加号。
        using var light = hsv.InRange(new Scalar(0, 0, 145), new Scalar(180, 80, 228));
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
        using var closed = new Mat();
        Cv2.MorphologyEx(light, closed, MorphTypes.Close, kernel);
        var contours = closed.FindContoursAsArray(RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        if (contours == null || contours.Length == 0)
        {
            return false;
        }

        var minSide = Math.Max(12, roi.Height / 4);
        var cand = contours
            .Select(Cv2.BoundingRect)
            .Where(r =>
            {
                var ar = r.Width / (double)Math.Max(r.Height, 1);
                return ar is > 0.7 and < 1.35
                       && r.Width >= minSide && r.Height >= minSide
                       && r.Height <= roi.Height * 0.95;
            })
            .OrderByDescending(r => r.X)
            .FirstOrDefault();
        if (cand.Width <= 0)
        {
            return false;
        }

        plus = new Rect(x0 + cand.X, cand.Y, cand.Width, cand.Height);
        return true;
    }

    private static Mat Crop(Mat screen, double x, double y, double w, double h)
    {
        var rx = (int)(screen.Width * x);
        var ry = (int)(screen.Height * y);
        var rw = (int)(screen.Width * w);
        var rh = (int)(screen.Height * h);
        rx = Math.Clamp(rx, 0, Math.Max(screen.Width - 1, 0));
        ry = Math.Clamp(ry, 0, Math.Max(screen.Height - 1, 0));
        rw = Math.Clamp(rw, 1, screen.Width - rx);
        rh = Math.Clamp(rh, 1, screen.Height - ry);
        return screen.SubMat(ry, ry + rh, rx, rx + rw).Clone();
    }

    private static bool TryParseDigits(string? text, out int value)
    {
        value = -1;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var best = string.Empty;
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch is >= '0' and <= '9')
            {
                sb.Append(ch);
                continue;
            }

            if (sb.Length > best.Length)
            {
                best = sb.ToString();
            }

            sb.Clear();
        }

        if (sb.Length > best.Length)
        {
            best = sb.ToString();
        }

        return best.Length > 0 && int.TryParse(best, out value);
    }
}

public sealed class CurrencyOcrResult(
    int? primogem,
    int? mora,
    string primogemRaw,
    string moraRaw,
    string source,
    Mat strip,
    Mat primogemRoi,
    Mat moraRoi,
    Mat? primogemBinary,
    Mat? moraBinary) : IDisposable
{
    public int? Primogem { get; } = primogem;
    public int? Mora { get; } = mora;
    public string PrimogemRaw { get; } = primogemRaw;
    public string MoraRaw { get; } = moraRaw;
    public string Source { get; } = source;
    public Mat Strip { get; } = strip;
    public Mat PrimogemRoi { get; } = primogemRoi;
    public Mat MoraRoi { get; } = moraRoi;
    public Mat? PrimogemBinary { get; } = primogemBinary;
    public Mat? MoraBinary { get; } = moraBinary;

    public void Dispose()
    {
        Strip.Dispose();
        PrimogemRoi.Dispose();
        MoraRoi.Dispose();
        PrimogemBinary?.Dispose();
        MoraBinary?.Dispose();
    }
}

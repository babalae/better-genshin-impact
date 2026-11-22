using System;
using System.IO;
using BetterGenshinImpact.Core.Config;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Music.Service;

internal readonly record struct ScoreReading(bool FullScore, bool RemainingScore, uint Hits, uint Red, uint PressedKeys = 0);

/// <summary>已标注的杜林三页曲谱。所有位置均为原生 1920×1080 捕获坐标。</summary>
internal sealed class DurinScoreVision
{
    private static readonly int[] KeyX = [458, 626, 792, 960, 1128, 1295, 1462];
    private static readonly int[] KeyY = [674, 809, 944];
    private readonly Sheet[] _sheets =
    [
        Load(1, 560, 666, [155, 180, 155, 165, 172, 180, 172, 180], 0),
        Load(2, 524, 624, [155, 165, 172, 180, 172, 165, 180, 201, 209], 0b110000000),
        Load(3, 476, 582, [202, 176, 168, 165, 159, 152, 159, 165, 169, 165], 0b1011111000)
    ];

    private sealed record Sheet(Glyph Clef, Glyph? AlternateClef, uint AlternateNotes, Glyph[] Notes);
    private readonly record struct GlyphReading(double Similarity, bool Hit, bool Red);

    private sealed class Glyph(Mat atlas, Rect template, int centerX, int centerY)
    {
        // B 通道存笔画边缘的带符号权重（零点 128），G 标记笔画，R 标记笔画附近的光晕区域。
        // 音符只保留普通态和命中态方向一致的边缘，两种状态都可验证同一位置的轮廓。
        private readonly Vec3b[] _mask = ReadMask(atlas, template);

        private static Vec3b[] ReadMask(Mat atlas, Rect template)
        {
            using var tile = new Mat(atlas, template);
            using var pixels = tile.Clone();
            pixels.GetArray(out Vec3b[] mask);
            return mask;
        }

        public unsafe GlyphReading Read(byte* frame, long stride, int channels)
        {
            var left = centerX - template.Width / 2;
            var top = centerY - template.Height / 2;
            int dot = 0, energy = 0, maskEnergy = 0;
            int ink = 0, bright = 0, cyan = 0, red = 0;
            var index = 0;
            for (var y = 0; y < template.Height; y++)
            {
                var p = frame + (top + y) * stride + left * channels;
                for (var x = 0; x < template.Width; x++, p += channels)
                {
                    var pixel = _mask[index++];
                    var weight = pixel.Item0 - 128;
                    if (weight != 0)
                    {
                        // 红色通道在青色命中光效下仍保留白色笔画的局部明暗变化。
                        var r = p + 2;
                        var edge = Math.Clamp(4 * *r - r[-2 * channels] - r[2 * channels]
                            - r[-2 * stride] - r[2 * stride], -70, 70);
                        dot += weight * edge;
                        energy += edge * edge;
                        maskEnergy += weight * weight;
                    }

                    if (pixel.Item1 != 0)
                    {
                        ink++;
                        if (p[0] >= 235 && p[1] >= 235 && p[2] > 180) bright++;
                        if (p[2] > 205 && p[1] < 145 && p[0] < 180 && p[2] - p[1] > 65) red++;
                    }
                    else if (pixel.Item2 != 0 && p[0] > 220 && p[1] > 195 && p[2] < 185 && p[1] - p[2] > 35)
                    {
                        cyan++;
                    }
                }
            }

            var similarity = energy == 0 || maskEnergy == 0 ? 0 : dot / Math.Sqrt((double)energy * maskEnergy);
            var hit = similarity >= 0.40 && cyan >= 100 && bright >= ink * 0.80;
            return new GlyphReading(similarity, hit, red >= 35);
        }
    }

    private static Sheet Load(int number, int clefX, int firstNoteX, int[] noteY, uint alternateNotes)
    {
        var path = Global.Absolute($@"GameTask\Music\Assets\1920x1080\Durin\sheet-{number}.png");
        using var atlas = Cv2.ImRead(path, ImreadModes.Color);
        var noteOffset = alternateNotes == 0 ? 57 : 114;
        if (atlas.Rows != 95 || atlas.Cols != noteOffset + 41 * noteY.Length)
        {
            throw new InvalidDataException($"杜林曲谱识别模板缺失或尺寸不符：{path}");
        }

        var clef = new Glyph(atlas, new Rect(0, 0, 57, 95), clefX, 172);
        var alternate = alternateNotes == 0 ? null : new Glyph(atlas, new Rect(57, 0, 57, 95), clefX, 172);
        var notes = new Glyph[noteY.Length];
        for (var i = 0; i < notes.Length; i++)
        {
            notes[i] = new Glyph(atlas, new Rect(noteOffset + i * 41, 0, 41, 35), firstNoteX + i * 84, noteY[i]);
        }
        return new Sheet(clef, alternate, alternateNotes, notes);
    }

    public unsafe ScoreReading Read(Mat frame, int expectedSheet, int nextNote)
    {
        if (!IsNative(frame) || (uint)expectedSheet >= _sheets.Length)
        {
            return default;
        }

        var sheet = _sheets[expectedSheet];
        if ((uint)nextNote >= sheet.Notes.Length) return default;
        var data = frame.DataPointer;
        var stride = frame.Step();
        var channels = frame.Channels();
        var initialClef = sheet.Clef.Read(data, stride, channels).Similarity >= 0.63;
        var full = initialClef;
        var remaining = true;
        uint hits = 0, red = 0;
        for (var i = 0; i < sheet.Notes.Length; i++)
        {
            var glyph = sheet.Notes[i].Read(data, stride, channels);
            if (glyph.Hit) hits |= 1u << i;
            if (glyph.Red) red |= 1u << i;
            var normal = glyph.Similarity >= 0.80 && !glyph.Hit && !glyph.Red;
            full &= normal;
            // 已经弹过的音符会变色。从当前音符直到末音逐个验形。
            if (i >= nextNote) remaining &= normal || (i == nextNote && glyph.Hit && !glyph.Red);
        }

        // 跨八度时，命中当帧就可能显示下一音符的谱号。
        uint clefSlots = 1u << nextNote;
        if ((hits & clefSlots) != 0 && nextNote + 1 < sheet.Notes.Length) clefSlots |= 1u << (nextNote + 1);
        var validClef = initialClef && (clefSlots & ~sheet.AlternateNotes) != 0;
        if (!validClef && (clefSlots & sheet.AlternateNotes) != 0 && sheet.AlternateClef is { } alternate)
        {
            validClef = alternate.Read(data, stride, channels).Similarity >= 0.63;
        }
        var pressedKeys = ReadPressedKeys(data, stride, channels);
        GC.KeepAlive(frame);
        return new ScoreReading(full, remaining && validClef, hits, red, pressedKeys);
    }

    private static bool IsNative(Mat frame) => frame.Width == 1920 && frame.Height == 1080 &&
        (frame.Type() == MatType.CV_8UC3 || frame.Type() == MatType.CV_8UC4);

    internal static unsafe bool HasKeyboard(Mat frame)
    {
        if (!IsNative(frame)) return false;
        var data = frame.DataPointer;
        var stride = frame.Step();
        var channels = frame.Channels();
        var caps = 0;
        foreach (var y in KeyY)
        foreach (var x in KeyX)
        {
            var white = 0;
            for (var dy = 14; dy < 31; dy++)
            {
                var p = data + (y + dy) * stride + (x - 10) * channels;
                for (var dx = 0; dx < 21; dx++, p += channels)
                {
                    var min = Math.Min(p[0], Math.Min(p[1], p[2]));
                    var max = Math.Max(p[0], Math.Max(p[1], p[2]));
                    if (min > 135 && max - min < 68) white++;
                }
            }
            if (white > 200) caps++;
        }
        GC.KeepAlive(frame);
        return caps >= 15;
    }

    private static unsafe uint ReadPressedKeys(byte* data, long stride, int channels)
    {
        // QWERTYU / ASDFGHJ / ZXCVBNM，从左至右、从上至下。
        uint pressed = 0;
        for (var row = 0; row < KeyY.Length; row++)
        for (var col = 0; col < KeyX.Length; col++)
        {
            var lit = 0;
            for (var y = KeyY[row] - 18; y < KeyY[row] + 18; y++)
            {
                var p = data + y * stride + (KeyX[col] - 20) * channels;
                for (var x = 0; x < 40; x++, p += channels)
                {
                    if (p[1] - p[2] > 35 && p[1] > 190 && p[0] > 155) lit++;
                }
            }
            if (lit > 40 * 36 * 0.45) pressed |= 1u << (row * 7 + col);
        }
        return pressed;
    }
}

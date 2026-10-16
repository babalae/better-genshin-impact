using BetterGenshinImpact.GameTask.Model.GameUI;
using BetterGenshinImpact.Helpers.Extensions;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BetterGenshinImpact.GameTask.InventoryMaterialStats;

/// <summary>
/// 本地图标库：<c>已识别</c> 为匹配成功过的背包裁图（不依赖 JS 模板是否还在）；
/// <c>未识别</c> 为点开详情后仍对不上白名单、或尚未读到标题的格子。
/// </summary>
public sealed class InventoryMaterialStatsUnrecognizedStore
{
    public const string FolderName = "未识别";
    public const string RecognizedFolderName = "已识别";
    public const string DumpPrefix = "待命名";
    public const string TempFolderName = "temp";

    private readonly HashSet<string> _hashes;
    private int _savedThisRun;
    private int _namedSavedThisRun;
    private int _recognizedSavedThisRun;

    public InventoryMaterialStatsUnrecognizedStore(string scriptFolderName)
    {
        var copyDir = InventoryMaterialStatsRecordStore.GetCopyDirectory(scriptFolderName);
        RootDirectory = Path.Combine(copyDir, FolderName);
        RecognizedRootDirectory = Path.Combine(copyDir, RecognizedFolderName);
        TempDirectory = Path.Combine(copyDir, TempFolderName);
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(RecognizedRootDirectory);
        _hashes = IndexExistingHashes(RootDirectory);
    }

    public string RootDirectory { get; }

    public string RecognizedRootDirectory { get; }

    public string TempDirectory { get; }

    public int SavedThisRun => _savedThisRun;

    public int NamedSavedThisRun => _namedSavedThisRun;

    public int RecognizedSavedThisRun => _recognizedSavedThisRun;

    public int TempDumpedThisRun => _dumpSeq;

    public void PrepareTempDump()
    {
        if (Directory.Exists(TempDirectory))
        {
            Directory.Delete(TempDirectory, true);
        }

        Directory.CreateDirectory(TempDirectory);
        _dumpSeq = 0;
    }

    /// <summary>
    /// 模板匹配失败时落下当前格子原图和分数，便于对照为何没对上已有的未识别/已识别图。
    /// </summary>
    public void DumpMatchMiss(
        GridScreenName page,
        Mat queryIcon,
        IReadOnlyList<(string Name, double Score, Mat? Template)> top,
        string reason = "match_miss",
        string? extra = null)
    {
        if (queryIcon.Empty())
        {
            return;
        }

        Directory.CreateDirectory(TempDirectory);
        _dumpSeq++;
        var stem = $"{_dumpSeq:000}_{SanitizeItemName(page.GetDescription())}_{SanitizeItemName(reason)}";
        Cv2.ImWrite(Path.Combine(TempDirectory, stem + "_query.png"), queryIcon);

        var sb = new StringBuilder();
        sb.AppendLine($"page={page.GetDescription()}");
        sb.AppendLine($"reason={reason}");
        sb.AppendLine($"query={queryIcon.Width}x{queryIcon.Height}");
        if (!string.IsNullOrWhiteSpace(extra))
        {
            sb.AppendLine(extra.TrimEnd());
        }

        for (var i = 0; i < top.Count; i++)
        {
            var (name, score, template) = top[i];
            sb.AppendLine($"{i + 1}. {name}  score={score:0.0000}");
            if (template != null && !template.Empty())
            {
                var safe = SanitizeItemName(name);
                Cv2.ImWrite(Path.Combine(TempDirectory, $"{stem}_{i + 1}_{safe}_{score:0.00}.png"), template);
            }
        }

        File.WriteAllText(Path.Combine(TempDirectory, stem + "_scores.txt"), sb.ToString());
    }

    public void DumpCurrency(CurrencyOcrResult result)
    {
        Directory.CreateDirectory(TempDirectory);
        var stem = "000_currency";
        WriteIfHasContent(Path.Combine(TempDirectory, stem + "_strip.png"), result.Strip);
        WriteIfHasContent(Path.Combine(TempDirectory, stem + "_primogem.png"), result.PrimogemRoi);
        WriteIfHasContent(Path.Combine(TempDirectory, stem + "_mora.png"), result.MoraRoi);
        WriteIfHasContent(Path.Combine(TempDirectory, stem + "_primogem_bin.png"), result.PrimogemBinary);
        WriteIfHasContent(Path.Combine(TempDirectory, stem + "_mora_bin.png"), result.MoraBinary);
        File.WriteAllText(Path.Combine(TempDirectory, stem + "_scores.txt"),
            $"source={result.Source}{Environment.NewLine}" +
            $"原石={result.Primogem}  raw={result.PrimogemRaw}{Environment.NewLine}" +
            $"摩拉={result.Mora}  raw={result.MoraRaw}{Environment.NewLine}");
    }

    private static void WriteIfHasContent(string path, Mat? mat)
    {
        if (mat != null && !mat.Empty())
        {
            Cv2.ImWrite(path, mat);
        }
    }

    private int _dumpSeq;

    public IEnumerable<(GridScreenName Page, string Name, string FilePath)> EnumerateNamedTemplates()
    {
        if (!Directory.Exists(RootDirectory))
        {
            yield break;
        }

        foreach (GridScreenName page in Enum.GetValues<GridScreenName>())
        {
            var dir = Path.Combine(RootDirectory, page.GetDescription());
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.png", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(name) || name.StartsWith(DumpPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                yield return (page, name, file);
            }
        }
    }

    public IEnumerable<(GridScreenName Page, string Name, string FilePath)> EnumerateRecognizedTemplates()
    {
        if (!Directory.Exists(RecognizedRootDirectory))
        {
            yield break;
        }

        foreach (GridScreenName page in Enum.GetValues<GridScreenName>())
        {
            var dir = Path.Combine(RecognizedRootDirectory, page.GetDescription());
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.png", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(name) || name.StartsWith(DumpPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                yield return (page, name, file);
            }
        }
    }

    public bool TrySaveRecognized(GridScreenName page, Mat icon125, string itemName)
    {
        if (icon125.Empty() || IsLikelyEmptySlot(icon125))
        {
            return false;
        }

        var name = SanitizeItemName(itemName);
        if (string.IsNullOrEmpty(name) || name.All(char.IsDigit))
        {
            return false;
        }

        var pageDir = Path.Combine(RecognizedRootDirectory, page.GetDescription());
        Directory.CreateDirectory(pageDir);
        var path = Path.Combine(pageDir, name + ".png");
        if (File.Exists(path))
        {
            return false;
        }

        if (!Cv2.ImWrite(path, icon125))
        {
            return false;
        }

        _recognizedSavedThisRun++;
        return true;
    }

    public bool TrySave(GridScreenName page, Mat icon125)
    {
        if (icon125.Empty() || IsLikelyEmptySlot(icon125))
        {
            return false;
        }

        var hash = ComputePixelHash(icon125);
        if (!_hashes.Add(hash))
        {
            return false;
        }

        var pageDir = Path.Combine(RootDirectory, page.GetDescription());
        Directory.CreateDirectory(pageDir);
        var path = Path.Combine(pageDir, $"{DumpPrefix}_{hash}.png");
        if (!Cv2.ImWrite(path, icon125))
        {
            _hashes.Remove(hash);
            return false;
        }

        _savedThisRun++;
        return true;
    }

    /// <summary>
    /// 用右侧详情标题落盘，下次作为额外模板。
    /// </summary>
    public bool TrySaveNamed(GridScreenName page, Mat icon125, string itemName)
    {
        if (icon125.Empty() || IsLikelyEmptySlot(icon125))
        {
            return false;
        }

        var name = SanitizeItemName(itemName);
        if (string.IsNullOrEmpty(name) || name.All(char.IsDigit) ||
            name.StartsWith(DumpPrefix, StringComparison.Ordinal))
        {
            return TrySave(page, icon125);
        }

        var pageDir = Path.Combine(RootDirectory, page.GetDescription());
        Directory.CreateDirectory(pageDir);
        var path = Path.Combine(pageDir, name + ".png");
        var hash = ComputePixelHash(icon125);
        _hashes.Add(hash);
        if (File.Exists(path))
        {
            return false;
        }

        if (!Cv2.ImWrite(path, icon125))
        {
            return false;
        }

        _savedThisRun++;
        _namedSavedThisRun++;
        return true;
    }

    private static HashSet<string> IndexExistingHashes(string root)
    {
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(root))
        {
            return hashes;
        }

        foreach (var file in Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories))
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            var fromName = TryHashFromDumpFileName(stem);
            if (fromName != null)
            {
                hashes.Add(fromName);
                continue;
            }

            using var mat = Cv2.ImRead(file, ImreadModes.Color);
            if (mat.Empty())
            {
                continue;
            }

            hashes.Add(ComputePixelHash(mat));
        }

        return hashes;
    }

    private static string? TryHashFromDumpFileName(string stem)
    {
        if (!stem.StartsWith(DumpPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var idx = stem.LastIndexOf('_');
        if (idx < 0 || idx == stem.Length - 1)
        {
            return null;
        }

        var suffix = stem[(idx + 1)..];
        return suffix.Length is >= 8 and <= 16 && suffix.All(Uri.IsHexDigit) ? suffix : null;
    }

    private static string ComputePixelHash(Mat mat)
    {
        using var gray = mat.Channels() == 1
            ? mat.Clone()
            : mat.CvtColor(ColorConversionCodes.BGR2GRAY);
        Cv2.ImEncode(".png", gray, out var bytes);
        var hex = Convert.ToHexString(SHA256.HashData(bytes));
        return hex[..12].ToLowerInvariant();
    }

    public static bool IsLikelyEmptySlot(Mat icon125)
    {
        using var gray = icon125.Channels() == 1
            ? icon125.Clone()
            : icon125.CvtColor(ColorConversionCodes.BGR2GRAY);
        Cv2.MeanStdDev(gray, out var mean, out var std);
        return mean.Val0 < 22 || std.Val0 < 6;
    }

    private static string SanitizeItemName(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(text.Length);
        foreach (var c in text.Trim())
        {
            if (char.IsWhiteSpace(c) || c == '\u3000')
            {
                continue;
            }

            if (c is '_' or '-' || (!invalid.Contains(c) && !char.IsControl(c)))
            {
                sb.Append(c);
            }

            if (sb.Length >= 40)
            {
                break;
            }
        }

        return sb.ToString().Trim();
    }
}

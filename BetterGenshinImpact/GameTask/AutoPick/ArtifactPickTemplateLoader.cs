using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace BetterGenshinImpact.GameTask.AutoPick;

/// <summary>
/// 加载圣遗物拾取名称模板。截图链路已是 1080P，模板按 1080P 原图加载，不再乘 AssetScale。
/// </summary>
public static class ArtifactPickTemplateLoader
{
    public const string UserTemplateFolder = @"User\pick_artifact_templates";

    private const string BuiltInFolder = @"GameTask\AutoPick\Assets\1920x1080\Artifacts";
    private const double DefaultThreshold = 0.7;

    private static readonly Regex ThresholdInPathRegex = new(@"[（(](.*?)[)）]", RegexOptions.Compiled);

    public static IReadOnlyList<ArtifactPickTemplate> Load()
    {
        var result = new List<ArtifactPickTemplate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        TryAddFolder(result, seen, Global.Absolute(BuiltInFolder), "内置");
        TryAddFolder(result, seen, Global.Absolute(UserTemplateFolder), "用户");
        return result;
    }

    private static void TryAddFolder(List<ArtifactPickTemplate> result, HashSet<string> seen, string folder, string source)
    {
        if (!Directory.Exists(folder))
        {
            if (source == "内置")
            {
                TaskControl.Logger.LogWarning("未找到圣遗物拾取模板目录：{Folder}", folder);
            }

            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(folder, "*.png", SearchOption.AllDirectories))
        {
            var itemName = ParseItemName(Path.GetFileName(filePath));
            if (string.IsNullOrWhiteSpace(itemName) || !seen.Add(itemName))
            {
                continue;
            }

            var mat = Cv2.ImRead(filePath, ImreadModes.Color);
            if (mat.Empty())
            {
                mat.Dispose();
                seen.Remove(itemName);
                TaskControl.Logger.LogWarning("圣遗物模板无法读取：{Path}", filePath);
                continue;
            }

            var recognition = RecognitionObject.TemplateMatch(mat);
            recognition.Name = itemName;
            recognition.Threshold = ParseThreshold(filePath);
            result.Add(new ArtifactPickTemplate
            {
                ItemName = itemName,
                Recognition = recognition
            });
        }
    }

    public static string ParseItemName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var match = ThresholdInPathRegex.Match(stem);
        if (match.Success && TryParseThreshold(match.Groups[1].Value, out _))
        {
            return stem[..match.Index].Trim();
        }

        return stem.Trim();
    }

    public static double ParseThreshold(string path)
    {
        var match = ThresholdInPathRegex.Match(path);
        if (match.Success && TryParseThreshold(match.Groups[1].Value, out var value))
        {
            return value;
        }

        return DefaultThreshold;
    }

    private static bool TryParseThreshold(string raw, out double value)
    {
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && value > 0 && value < 1)
        {
            return true;
        }

        value = DefaultThreshold;
        return false;
    }
}

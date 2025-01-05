using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Genshin.Paths;

/// <summary>
/// https://github.com/DGP-Studio/Snap.Hutao/blob/main/src/Snap.Hutao/Snap.Hutao/Service/Game/Locator/UnityLogGameLocator.cs
/// </summary>
public partial class UnityLogGameLocator
{
    [GeneratedRegex(@".:/.+(?:GenshinImpact|YuanShen)(?=_Data)", RegexOptions.IgnoreCase)]
    private static partial Regex WarmupFileLine();

    public static async ValueTask<string?> LocateSingleGamePathAsync()
    {
        try
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string logFilePathOversea = Path.Combine(appDataPath, @"..\LocalLow\miHoYo\Genshin Impact\output_log.txt");
            string logFilePathChinese = Path.Combine(appDataPath, @"..\LocalLow\miHoYo\原神\output_log.txt");

            // Fallback to the CN server.
            string logFilePath = File.Exists(logFilePathChinese) ? logFilePathChinese : logFilePathOversea;
            return await LocateGamePathAsync(logFilePath).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogDebug(e, "Failed to locate game path.");
            return null;
        }
    }

    private static async ValueTask<string?> LocateGamePathAsync(string logFilePath)
    {
        if (!File.Exists(logFilePath))
        {
            return null;
        }

        string content;
        try
        {
            await using var fileStream = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fileStream);
            content = await reader.ReadToEndAsync();
        }
        catch (IOException)
        {
            return null;
        }

        Match matchResult = WarmupFileLine().Match(content);
        if (!matchResult.Success)
        {
            return null;
        }

        string entryName = $"{matchResult.Value}.exe";
        string fullPath = Path.GetFullPath(Path.Combine(matchResult.Value, "..", entryName));
        return fullPath;
    }
}
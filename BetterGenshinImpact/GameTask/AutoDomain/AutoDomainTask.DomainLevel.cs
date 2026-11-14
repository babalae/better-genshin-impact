using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Script.Dependence;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoDomain;

public partial class AutoDomainTask
{
    private static readonly string[] KnownDomainTiers = ["I", "II", "III", "IV", "V", "VI"];

    /// <summary>
    /// 从秘境列表的一行文字中取出末尾的难度，并统一为 ASCII 罗马数字。
    /// OCR 常把 I 识别成 l 或 |，这里一并修正；无法识别时返回 null。
    /// </summary>
    internal static string? ParseDomainTier(string? rowText)
    {
        if (string.IsNullOrWhiteSpace(rowText))
        {
            return null;
        }

        var tail = Regex.Match(rowText.Trim(), @"[IVXivxlL|ⅠⅡⅢⅣⅤⅥ]+$").Value;
        var tier = tail
            .Replace("Ⅵ", "VI").Replace("Ⅴ", "V").Replace("Ⅳ", "IV")
            .Replace("Ⅲ", "III").Replace("Ⅱ", "II").Replace("Ⅰ", "I")
            .Replace('l', 'I').Replace('L', 'I').Replace('|', 'I')
            .ToUpperInvariant();
        return KnownDomainTiers.Contains(tier) ? tier : null;
    }

    /// <summary>
    /// 按从上到下的位置确定目标难度所在行，因为 OCR 常把罗马数字读错（如把 III 读成 II 或 1）。
    /// OCR 通常是漏读笔画，所以读出的难度不应高于位置；若高于位置，说明列表并非从 I 开始，返回 -1 以免选错。
    /// </summary>
    internal static int FindDomainTierRow(IReadOnlyList<string> rowTexts, string targetTier)
    {
        // 脚本可能传入小写或带空格的难度。
        var targetIndex = Array.IndexOf(KnownDomainTiers, targetTier.Trim().ToUpperInvariant());
        if (targetIndex < 0 || targetIndex >= rowTexts.Count || rowTexts.Count > KnownDomainTiers.Length)
        {
            return -1;
        }

        for (var i = 0; i < rowTexts.Count; i++)
        {
            var tier = ParseDomainTier(rowTexts[i]);
            if (tier != null && Array.IndexOf(KnownDomainTiers, tier) > i)
            {
                return -1;
            }
        }

        return targetIndex;
    }

    /// <summary>
    /// 在秘境列表中点击配置的难度。
    /// 找不到时停止任务：继续挑战可能进入更高难度，队伍打不过且会浪费树脂。
    /// </summary>
    private async Task SelectConfiguredDomainLevel(bool isAllDomainsOpen)
    {
        var targetTier = _taskParam.DomainLevel.Trim();
        if (isAllDomainsOpen)
        {
            // 全开时同一难度有多个不同奖励的材料本，按难度无法确定目标，由序号配置决定。
            using var screen = CaptureToRectArea();
            using var artifactArea = screen.Find(RecognitionAssets.Get("AutoFight", "ArtifactArea", screen));
            if (artifactArea.IsEmpty())
            {
                Logger.LogWarning("自动秘境：周日或限时全开的材料秘境不支持指定难度，已跳过");
                return;
            }
        }

        using (var screen = CaptureToRectArea())
        {
            GlobalMethod.MoveMouseTo(screen.Width / 4, screen.Height / 2);
        }

        // 滚动到顶部，让第一行对应难度 I。
        for (var i = 0; i < 80; i++)
        {
            Simulation.SendInput.Mouse.VerticalScroll(1);
            await Delay(20, _ct);
        }

        await Delay(600, _ct);

        using var capture = CaptureToRectArea();
        var rows = capture.FindMulti(RecognitionObject.Ocr(0, capture.Height * 0.14,
            capture.Width * 0.47, capture.Height * 0.84));
        try
        {
            var levelRows = rows.Where(r => r.Text.Contains("秘境")).OrderBy(r => r.Y).ToList();
            Logger.LogInformation("自动秘境：难度列表OCR：{Text}", string.Join(" | ", levelRows.Select(r => r.Text)));
            var rowIndex = FindDomainTierRow(levelRows.Select(r => r.Text).ToList(), targetTier);
            if (rowIndex < 0)
            {
                throw new InvalidOperationException($"自动秘境：未找到难度 {targetTier}，已停止以免进入其他难度");
            }

            levelRows[rowIndex].Click();
            await Delay(700, _ct);
            Logger.LogInformation("自动秘境：选择难度 {Tier}，第 {Row} 行", targetTier, rowIndex + 1);
        }
        finally
        {
            foreach (var row in rows)
            {
                row.Dispose();
            }
        }
    }
}

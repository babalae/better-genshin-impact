using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.GameUI;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Helpers.Extensions;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.InventoryMaterialStats;

/// <summary>
/// 扫描背包材料，追加库存记录；控制台只打新增/删除的物品。
/// </summary>
public sealed class InventoryMaterialStatsTask : ISoloTask, IDisposable
{
    private const double TemplateMatchThreshold = 0.85;
    private const double LocalCropMatchThreshold = 0.68;
    private const double UniqueScoreMargin = 0.03;

    private readonly ILogger _logger = App.GetLogger<InventoryMaterialStatsTask>();
    private readonly string _folderName;
    private readonly IReadOnlyList<string> _categories;
    private readonly List<LoadedTemplate> _templates = [];

    private IInputChannel Input => InputHub.Foreground;

    public string Name => "背包材料统计";

    public InventoryMaterialStatsTask()
        : this(TaskContext.Instance().Config.InventoryMaterialStatsConfig)
    {
    }

    public InventoryMaterialStatsTask(InventoryMaterialStatsConfig config)
        : this(config.ScriptFolderName, config.GetEnabledCategories())
    {
    }

    public InventoryMaterialStatsTask(string? folderName, IReadOnlyList<string> categories)
    {
        _folderName = folderName ?? string.Empty;
        _categories = categories;
    }

    public async Task Start(CancellationToken ct)
    {
        var storeFolder = string.IsNullOrWhiteSpace(_folderName)
            ? InventoryMaterialStatsScriptLocator.DefaultFolderName
            : _folderName;
        var categories = _categories.Count > 0
            ? _categories
            : throw new InvalidOperationException("请至少勾选一个扫描分类（养成道具、食物、材料）。");

        var copy = InventoryMaterialStatsScriptLocator.TryResolve(_folderName);
        if (copy != null)
        {
            LoadTemplates(copy.ProjectPath, categories);
        }

        var unrecognizedStore = new InventoryMaterialStatsUnrecognizedStore(storeFolder);
        // unrecognizedStore.PrepareTempDump();
        LoadExtraTemplates(unrecognizedStore, categories);
        if (_templates.Count == 0)
        {
            throw new InvalidOperationException(
                "没有可用的材料模板，也还没有本地已识别图。请先安装「背包材料统计」脚本（首次运行需要脚本资源，用于生成已识别目录）。");
        }

        if (copy == null)
        {
            _logger.LogInformation("未安装「背包材料统计」脚本，已使用本地已识别目录中的模板。");
        }

        // _logger.LogInformation("匹配失败原图目录：{Path}", unrecognizedStore.TempDirectory);

        var counts = _templates.Select(t => t.Name).Distinct().ToDictionary(n => n, _ => 0);
        var unrecognizedCounts = new List<string>();

        using var recognizer = ItemIconRecognizerFactory.CreateConfigured();
        var ocr = OcrFactory.Paddle;
        await new ReturnMainUiTask().Start(ct);

        var pages = _templates
            .GroupBy(t => t.Page)
            .OrderBy(g => (int)g.Key)
            .ToList();

        var first = true;
        try
        {
            try
            {
                foreach (var pageGroup in pages)
                {
                    ct.ThrowIfCancellationRequested();
                    var page = pageGroup.Key;
                    if (first)
                    {
                        await AutoArtifactSalvageTask.OpenInventory(page, Input, _logger, ct);
                        first = false;
                    }
                    else
                    {
                        await AutoArtifactSalvageTask.SwitchInventoryTab(page, Input, _logger, ct);
                    }

                    await ScanPage(page, pageGroup.ToList(), recognizer, ocr, counts, unrecognizedCounts, unrecognizedStore, ct);
                }
            }
            finally
            {
                await new ReturnMainUiTask().Start(CancellationToken.None);
            }

            foreach (var name in unrecognizedCounts.Distinct())
            {
                counts[name] = -2;
            }

            var baseline = InventoryMaterialStatsRecordStore.FindBaseline(storeFolder);
            var record = InventoryMaterialStatsRecordStore.Save(storeFolder, counts, baseline);
            LogSummary(record, unrecognizedCounts.Distinct().ToList(), unrecognizedStore);
        }
        finally
        {
            DisposeTemplates();
        }
    }

    private async Task ScanPage(
        GridScreenName page,
        List<LoadedTemplate> pageTemplates,
        IItemIconRecognizer recognizer,
        IOcrService ocr,
        Dictionary<string, int> counts,
        List<string> unrecognizedCounts,
        InventoryMaterialStatsUnrecognizedStore unrecognizedStore,
        CancellationToken ct)
    {
        var remaining = pageTemplates.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var byName = pageTemplates
            .GroupBy(t => t.Name)
            .ToDictionary(g => g.Key, g => g.ToList());

        _logger.LogInformation("扫描背包页 {Page}，待统计 {Count} 类材料", page.GetDescription(), remaining.Count);

        if (page == GridScreenName.CharacterDevelopmentItems)
        {
            ReadCharacterDevelopmentCurrencies(ocr, counts, unrecognizedCounts, unrecognizedStore, ct);
        }

        var gridScreen = new GridScreen(GridParams.Templates[page], _logger, ct);
        gridScreen.OnAfterTurnToNewPage += GridScreen.DrawItemsAfterTurnToNewPage;
        gridScreen.OnBeforeScroll += () => TaskContext.Instance().Runtime?.MaskWindowDrawingBoard.ClearAll();

        try
        {
            await foreach ((ImageRegion pageRegion, Rect itemRect) in gridScreen)
            {
                ct.ThrowIfCancellationRequested();
                using ImageRegion itemRegion = pageRegion.DeriveCrop(itemRect);
                using Mat icon = itemRegion.SrcMat.GetGridIcon();
                if (InventoryMaterialStatsUnrecognizedStore.IsLikelyEmptySlot(icon))
                {
                    continue;
                }

                using var countResult = GridItemCountRecognizer.RecognizeCropped(itemRegion.SrcMat, ocr);
                var hasQty = HasQuantityDigits(countResult);
                using var forLocal = SuppressNewBadge(icon);
                var hit = RecognizeName(icon, forLocal, recognizer, remaining, byName);
                var name = hit.Name;
                if (name != null && !remaining.Contains(name))
                {
                    // _logger.LogInformation(
                    //     "跳过已统计 {Page} {Name} qty={Qty} how={How} | {Detail}",
                    //     page.GetDescription(),
                    //     name,
                    //     countResult.Count,
                    //     hit.How,
                    //     hit.Detail);
                    continue;
                }
                if (!hasQty)
                {
                    // unrecognizedStore.DumpMatchMiss(
                    //     page,
                    //     icon,
                    //     TopTemplateScores(icon, forLocal, remaining, byName),
                    //     name == null ? "qty0_unmatched" : "qty0",
                    //     qtyExtra);

                    // 背包数量为 1 时常不显示数字（EMPTY）；能对上图则记 1。
                    if (name != null && string.Equals(countResult.Reason, "EMPTY", StringComparison.Ordinal))
                    {
                        // LogTake(page, name, countResult, hit, remaining, stolen: !remaining.Contains(name));
                        remaining.Remove(name);
                        unrecognizedStore.TrySaveRecognized(page, forLocal, name);
                        counts[name] = 1;
                    }

                    continue;
                }

                if (name == null)
                {
                    // unrecognizedStore.DumpMatchMiss(
                    //     page,
                    //     icon,
                    //     TopTemplateScores(icon, forLocal, remaining, byName),
                    //     "match_miss",
                    //     ...);

                    var panelName = InventoryMaterialStatsNameMatcher.Canonical(
                        await TryReadDetailPanelName(itemRegion, ocr, ct));
                    if (!string.IsNullOrEmpty(panelName))
                    {
                        var alignedRemaining = InventoryMaterialStatsNameMatcher.MatchUnique(
                            panelName, remaining, byName.Keys);
                        var alignedAll = InventoryMaterialStatsNameMatcher.MatchUnique(
                            panelName, byName.Keys, byName.Keys);
                        name = alignedRemaining ?? alignedAll;
                        _logger.LogInformation("点开详情 {Page} {Name}", page.GetDescription(), panelName);
                        if (name == null)
                        {
                            unrecognizedStore.TrySaveNamed(page, forLocal, panelName);
                            ApplyCount(panelName, countResult, counts, unrecognizedCounts);
                            continue;
                        }

                        if (!remaining.Contains(name))
                        {
                            continue;
                        }
                    }
                    else
                    {
                        _logger.LogInformation("点开详情 {Page} 未读到标题", page.GetDescription());
                        unrecognizedStore.TrySave(page, forLocal);
                        continue;
                    }
                }

                // LogTake(page, name, countResult, hit, remaining, stolen: !remaining.Contains(name));
                remaining.Remove(name);
                unrecognizedStore.TrySaveRecognized(page, forLocal, name);
                ApplyCount(name, countResult, counts, unrecognizedCounts);
            }
        }
        finally
        {
            TaskContext.Instance().Runtime?.MaskWindowDrawingBoard.ClearAll();
        }
    }

    private NameHit RecognizeName(
        Mat liveIcon,
        Mat localIcon,
        IItemIconRecognizer recognizer,
        HashSet<string> remaining,
        Dictionary<string, List<LoadedTemplate>> byName)
    {
        string? embedRaw = null;
        try
        {
            embedRaw = InventoryMaterialStatsNameMatcher.Canonical(recognizer.Recognize(localIcon));
            if (!string.IsNullOrEmpty(embedRaw))
            {
                if (remaining.Contains(embedRaw) &&
                    !InventoryMaterialStatsNameMatcher.HasLongerName(embedRaw, remaining))
                {
                    return new NameHit(embedRaw, "嵌入", $"embed={embedRaw} remaining=yes");
                }

                var matchedEmbedded = InventoryMaterialStatsNameMatcher.MatchUnique(
                    embedRaw, remaining, byName.Keys);
                if (matchedEmbedded != null &&
                    remaining.Contains(matchedEmbedded) &&
                    !InventoryMaterialStatsNameMatcher.HasLongerName(matchedEmbedded, remaining))
                {
                    return new NameHit(
                        matchedEmbedded,
                        "嵌入模糊",
                        $"embed={embedRaw} remaining=no fuzzy={matchedEmbedded}");
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "物品嵌入识别失败");
        }

        var embedNote = string.IsNullOrEmpty(embedRaw)
            ? "embed=-"
            : $"embed={embedRaw} remaining={(remaining.Contains(embedRaw) ? "yes" : "no")}";

        // JS 人工截图最准，先匹配；再未识别（优先于已识别），最后已识别。
        foreach (var (source, threshold) in new[]
                 {
                     (TemplateSource.Js, TemplateMatchThreshold),
                     (TemplateSource.Unrecognized, LocalCropMatchThreshold),
                     (TemplateSource.Recognized, LocalCropMatchThreshold)
                 })
        {
            var scores = ScoreTemplates(liveIcon, localIcon, remaining, byName, source);
            var picked = TryPickUniqueMatch(scores, threshold);
            var top = FormatTopScores(scores, 3);
            if (picked.Name != null)
            {
                return new NameHit(
                    picked.Name,
                    SourceLabel(source),
                    $"{embedNote} {SourceLabel(source)} {picked.Name}={picked.Best:0.000} second={picked.SecondName ?? "-"}:{picked.Second:0.000} th={threshold:0.00} top={top}");
            }
        }

        var allTop = FormatTopScores(ScoreTemplates(liveIcon, localIcon, remaining, byName), 5);

        // 翻页重叠：嵌入仍认识但已统计过。先走完剩余模板，避免今昔剧画之虎啮被当成已计的恶尉而漏扫。
        if (!string.IsNullOrEmpty(embedRaw) &&
            byName.ContainsKey(embedRaw) &&
            !remaining.Contains(embedRaw) &&
            !InventoryMaterialStatsNameMatcher.HasLongerName(embedRaw, remaining))
        {
            return new NameHit(embedRaw, "嵌入已计", $"{embedNote} remaining={remaining.Count} top={allTop}");
        }

        return new NameHit(null, "未命中", $"{embedNote} remaining={remaining.Count} top={allTop}");
    }

    private void LogTake(
        GridScreenName page,
        string name,
        GridItemCountRecognitionResult countResult,
        NameHit hit,
        HashSet<string> remaining,
        bool stolen)
    {
        if (stolen)
        {
            // _logger.LogInformation(
            //     "候选已占用后再次命中 {Page} {Name} qty={Qty} how={How} | {Detail}",
            //     page.GetDescription(),
            //     name,
            //     countResult.Count,
            //     hit.How,
            //     hit.Detail);
            return;
        }

        // _logger.LogInformation(
        //     "命中 {Page} {Name} qty={Qty} how={How} remaining余{Left} | {Detail}",
        //     page.GetDescription(),
        //     name,
        //     countResult.Count,
        //     hit.How,
        //     remaining.Count - 1,
        //     hit.Detail);
    }

    private static string FormatTopScores(
        List<(string Name, double Score, TemplateSource Source)> scores,
        int take)
    {
        return string.Join(
            ", ",
            scores.OrderByDescending(s => s.Score).Take(take)
                .Select(s => $"{SourceLabel(s.Source)}:{s.Name}={s.Score:0.000}"));
    }

    private static (string? Name, double Best, string? SecondName, double Second) TryPickUniqueMatch(
        List<(string Name, double Score, TemplateSource Source)> scores,
        double threshold)
    {
        var passed = scores
            .Where(s => s.Score >= threshold)
            .OrderByDescending(s => s.Score)
            .ToList();
        if (passed.Count == 0)
        {
            var bestFail = scores.OrderByDescending(s => s.Score).FirstOrDefault();
            return (null, bestFail.Score, null, 0);
        }

        var best = passed[0];
        var second = passed.Skip(1).FirstOrDefault(s => s.Name != best.Name);
        if (second.Name == null || best.Score - second.Score >= UniqueScoreMargin || best.Score >= 0.93)
        {
            return (best.Name, best.Score, second.Name, second.Score);
        }

        return (null, best.Score, second.Name, second.Score);
    }

    private static List<(string Name, double Score, TemplateSource Source)> ScoreTemplates(
        Mat liveIcon,
        Mat localIcon,
        HashSet<string> remaining,
        Dictionary<string, List<LoadedTemplate>> byName,
        TemplateSource? source = null)
    {
        using var grayLive = ToGray(liveIcon);
        using var grayLocal = ToGray(localIcon);
        var scores = new List<(string Name, double Score, TemplateSource Source)>();
        foreach (var name in remaining)
        {
            if (!byName.TryGetValue(name, out var templates))
            {
                continue;
            }

            foreach (var template in templates)
            {
                if (source != null && template.Source != source)
                {
                    continue;
                }

                // JS 人工图本身不含「新」角标，用未盖角的格子去比；已识别/未识别是背包裁图，两边都盖掉右上角。
                var useLocal = template.Source != TemplateSource.Js;
                var score = MeasureTemplateScore(
                    useLocal ? localIcon : liveIcon,
                    useLocal ? grayLocal : grayLive,
                    template);
                if (score > 0)
                {
                    scores.Add((name, score, template.Source));
                }
            }
        }

        return scores;
    }

    private static Mat ToGray(Mat colorOrGray)
    {
        return colorOrGray.Channels() == 1
            ? colorOrGray.Clone()
            : colorOrGray.CvtColor(ColorConversionCodes.BGR2GRAY);
    }

    private static double MeasureTemplateScore(Mat colorIcon, Mat grayIcon, LoadedTemplate template)
    {
        Mat src;
        Mat dst;
        if (template.Source != TemplateSource.Js && colorIcon.Type() == template.ColorIcon.Type())
        {
            src = colorIcon;
            dst = template.ColorIcon;
        }
        else
        {
            src = grayIcon;
            dst = template.GrayIcon;
        }

        if (src.Empty() || dst.Empty() || src.Width < dst.Width || src.Height < dst.Height)
        {
            return 0;
        }

        using var result = new Mat();
        Cv2.MatchTemplate(src, dst, result, TemplateMatchModes.CCoeffNormed);
        if (result.Empty())
        {
            return 0;
        }

        Cv2.MinMaxLoc(result, out double _, out double maxScore);
        return double.IsNaN(maxScore) ? 0 : maxScore;
    }

    private static string SourceLabel(TemplateSource source) => source switch
    {
        TemplateSource.Js => "JS",
        TemplateSource.Unrecognized => "未识别",
        TemplateSource.Recognized => "已识别",
        _ => source.ToString()
    };

    private static List<(string Name, double Score, Mat? Template)> TopTemplateScores(
        Mat liveIcon,
        Mat localIcon,
        HashSet<string> remaining,
        Dictionary<string, List<LoadedTemplate>> byName)
    {
        return ScoreTemplates(liveIcon, localIcon, remaining, byName)
            .OrderByDescending(s => s.Score)
            .Take(5)
            .Select(s =>
            {
                Mat? tmpl = null;
                if (byName.TryGetValue(s.Name, out var list))
                {
                    tmpl = list.FirstOrDefault(t => t.Source == s.Source)?.ColorIcon
                           ?? list.FirstOrDefault()?.ColorIcon;
                }

                return ($"{SourceLabel(s.Source)}:{s.Name}", s.Score, tmpl);
            })
            .ToList();
    }

    private void ReadCharacterDevelopmentCurrencies(
        IOcrService ocr,
        Dictionary<string, int> counts,
        List<string> unrecognizedCounts,
        InventoryMaterialStatsUnrecognizedStore unrecognizedStore,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var ra = CaptureToRectArea(forceNew: true);
        using var currency = InventoryMaterialStatsOcr.ReadPrimogemAndMora(ra.SrcMat, ocr);
        // unrecognizedStore.DumpCurrency(currency);
        ApplyCurrency("原石", currency.Primogem, counts, unrecognizedCounts);
        ApplyCurrency("摩拉", currency.Mora, counts, unrecognizedCounts);
    }

    private void ApplyCurrency(
        string name,
        int? value,
        Dictionary<string, int> counts,
        List<string> unrecognizedCounts)
    {
        if (value is >= 0)
        {
            counts[name] = value.Value;
            _logger.LogInformation("{Name}：{Count}", name, value.Value);
            return;
        }

        unrecognizedCounts.Add(name);
        _logger.LogWarning("未能识别养成道具页底部的{Name}", name);
    }

    /// <summary>
    /// 点击格子后 OCR 右侧详情标题（与 GetGridIconsTask 同一区域），不用格子底部数量。
    /// </summary>
    private async Task<string?> TryReadDetailPanelName(ImageRegion itemRegion, IOcrService ocr, CancellationToken ct)
    {
        itemRegion.Click();
        await Delay(300, ct);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var ra = CaptureToRectArea(forceNew: true);
            using ImageRegion nameRegion = ra.DeriveCrop(new Rect(
                (int)(ra.Width * 0.682),
                (int)(ra.Width * 0.0625),
                (int)(ra.Width * 0.256),
                (int)(ra.Width * 0.03125)));
            var text = InventoryMaterialStatsOcr.ReadWhiteTitle(nameRegion.SrcMat, ocr);
            if (!string.IsNullOrWhiteSpace(text) && !text.All(char.IsDigit))
            {
                _logger.LogDebug("右侧详情标题：{Text}", text);
                return text;
            }

            await Delay(200, ct);
        }

        return null;
    }

    private static bool HasQuantityDigits(GridItemCountRecognitionResult countResult)
    {
        if (countResult.Count >= 0)
        {
            return true;
        }

        if (string.Equals(countResult.Reason, "EMPTY", StringComparison.Ordinal))
        {
            return false;
        }

        return countResult.RawText.Any(char.IsDigit);
    }

    private void ApplyCount(
        string name,
        GridItemCountRecognitionResult countResult,
        Dictionary<string, int> counts,
        List<string> unrecognizedCounts)
    {
        if (countResult.Count >= 0)
        {
            counts[name] = countResult.Count;
        }
        else
        {
            unrecognizedCounts.Add(name);
            _logger.LogDebug("材料 {Name} 数量识别失败：{Reason} OCR={Text}",
                name, countResult.Reason, countResult.RawText);
        }
    }

    private void LoadTemplates(string projectPath, IReadOnlyList<string> categories)
    {
        DisposeTemplates();
        var imagesRoot = Path.Combine(projectPath, InventoryMaterialStatsScriptLocator.ImagesRelativePath);
        foreach (var category in categories.Distinct())
        {
            if (!InventoryMaterialStatsCategories.CategoryToPage.TryGetValue(category, out var page))
            {
                _logger.LogWarning("未知材料分类目录，已跳过：{Category}", category);
                continue;
            }

            var dir = Path.Combine(imagesRoot, category);
            if (!Directory.Exists(dir))
            {
                _logger.LogWarning("脚本中不存在分类目录：{Category}", category);
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.png", SearchOption.TopDirectoryOnly))
            {
                var name = InventoryMaterialStatsNameMatcher.StripWhiteSpace(
                    Path.GetFileNameWithoutExtension(file));
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                using var loaded = Cv2.ImRead(file, ImreadModes.Color);
                if (loaded.Empty())
                {
                    _logger.LogDebug("无法读取模板：{File}", file);
                    continue;
                }

                using var bgr = loaded.Channels() == 4
                    ? loaded.CvtColor(ColorConversionCodes.BGRA2BGR)
                    : loaded.Clone();
                var icon = ToIcon125(bgr);
                var gray = icon.CvtColor(ColorConversionCodes.BGR2GRAY);
                _templates.Add(new LoadedTemplate(name, page, icon, gray, TemplateSource.Js));
            }
        }

        _logger.LogInformation("已加载材料模板 {Count} 张", _templates.Count);
    }

    private void LoadExtraTemplates(InventoryMaterialStatsUnrecognizedStore store, IReadOnlyList<string> categories)
    {
        var recognized = LoadTemplateFiles(
            store.EnumerateRecognizedTemplates(), categories, TemplateSource.Recognized);
        var unnamed = LoadTemplateFiles(
            store.EnumerateNamedTemplates(), categories, TemplateSource.Unrecognized);
        if (recognized > 0)
        {
            _logger.LogInformation("已从已识别目录加载模板 {Count} 张", recognized);
        }

        if (unnamed > 0)
        {
            _logger.LogInformation("已从未识别目录加载额外模板 {Count} 张", unnamed);
        }
    }

    private int LoadTemplateFiles(
        IEnumerable<(GridScreenName Page, string Name, string FilePath)> files,
        IReadOnlyList<string> categories,
        TemplateSource source)
    {
        var allowedPages = categories
            .Where(c => InventoryMaterialStatsCategories.CategoryToPage.ContainsKey(c))
            .Select(c => InventoryMaterialStatsCategories.CategoryToPage[c])
            .ToHashSet();

        var loaded = 0;
        foreach (var (page, rawName, file) in files)
        {
            var name = InventoryMaterialStatsNameMatcher.StripWhiteSpace(rawName);
            if (string.IsNullOrWhiteSpace(name) || !allowedPages.Contains(page))
            {
                continue;
            }

            using var mat = Cv2.ImRead(file, ImreadModes.Color);
            if (mat.Empty())
            {
                _logger.LogDebug("无法读取本地模板：{File}", file);
                continue;
            }

            using var bgr = mat.Channels() == 4
                ? mat.CvtColor(ColorConversionCodes.BGRA2BGR)
                : mat.Clone();
            var icon = bgr.Width == 125 && bgr.Height == 125 ? bgr.Clone() : ToIcon125(bgr);
            var color = SuppressNewBadge(icon);
            icon.Dispose();
            var gray = color.CvtColor(ColorConversionCodes.BGR2GRAY);

            if (IsSuffixOfExistingTemplate(page, name))
            {
                color.Dispose();
                gray.Dispose();
                continue;
            }

            if (_templates.Any(t => t.Page == page && t.Name == name && t.Source == source))
            {
                color.Dispose();
                gray.Dispose();
                continue;
            }

            _templates.Add(new LoadedTemplate(name, page, color, gray, source));
            loaded++;
        }

        return loaded;
    }

    /// <summary>
    /// 「枫木中」这类未识别落盘，其实是「枫木」加了 OCR 尾巴，不能再当独立模板。
    /// </summary>
    private bool IsSuffixOfExistingTemplate(GridScreenName page, string name)
    {
        var normalized = InventoryMaterialStatsNameMatcher.Normalize(name);
        return _templates.Any(t =>
            t.Page == page &&
            normalized.StartsWith(InventoryMaterialStatsNameMatcher.Normalize(t.Name), StringComparison.Ordinal) &&
            normalized.Length > InventoryMaterialStatsNameMatcher.Normalize(t.Name).Length);
    }

    private static Mat ToIcon125(Mat source)
    {
        using var resized = source.Resize(new Size(125, 153));
        return resized.SubMat(0, 125, 0, 125).Clone();
    }

    /// <summary>
    /// 用左上角底板色盖住右上角「新」角标。只用于已识别/未识别（背包裁图）以及嵌入；JS 人工图不含这块，不要盖。
    /// </summary>
    private static Mat SuppressNewBadge(Mat icon125)
    {
        var clone = icon125.Clone();
        if (clone.Empty() || clone.Width < 16 || clone.Height < 16)
        {
            return clone;
        }

        var x = (int)(clone.Width * 0.70);
        var h = Math.Max((int)(clone.Height * 0.30), 1);
        var roi = new Rect(x, 0, clone.Width - x, h);
        var px = clone.At<Vec3b>(1, 1);
        Cv2.Rectangle(clone, roi, new Scalar(px.Item0, px.Item1, px.Item2), -1);
        return clone;
    }

    private void LogSummary(
        InventoryMaterialStatsRecord record,
        List<string> unrecognizedCountNames,
        InventoryMaterialStatsUnrecognizedStore unrecognizedStore)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"背包材料统计 {record.ServerDate}（服务器日）");

        var history = InventoryMaterialStatsRecordStore.LoadRecords(record.ScriptFolderName);
        var previous = history.Skip(1).FirstOrDefault();
        if (previous == null)
        {
            sb.AppendLine("已写入本次库存，尚无上次记录，无法对比增减。");
        }
        else
        {
            var (gained, lost) = InventoryMaterialStatsRecordStore.ComputeDelta(record.Counts, previous.Counts);
            if (gained.Count == 0 && lost.Count == 0)
            {
                sb.AppendLine($"相对上次（{previous.RecordedAt:MM-dd HH:mm}）无增减。");
            }
            else
            {
                sb.AppendLine($"相对上次（{previous.RecordedAt:MM-dd HH:mm}）：");
                foreach (var kv in gained.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    sb.AppendLine($"  {kv.Key} +{kv.Value}");
                }

                foreach (var kv in lost.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    sb.AppendLine($"  {kv.Key} -{kv.Value}");
                }
            }
        }

        if (unrecognizedCountNames.Count > 0)
        {
            sb.AppendLine($"未识别数量：{string.Join("、", unrecognizedCountNames.Distinct())}");
        }

        // if (unrecognizedStore.TempDumpedThisRun > 0)
        // {
        //     sb.AppendLine($"匹配失败原图 {unrecognizedStore.TempDumpedThisRun} 张：{unrecognizedStore.TempDirectory}");
        // }

        if (unrecognizedStore.RecognizedSavedThisRun > 0 || unrecognizedStore.SavedThisRun > 0)
        {
            sb.AppendLine(
                $"已识别新增 {unrecognizedStore.RecognizedSavedThisRun}，未识别 {unrecognizedStore.SavedThisRun}（其中已命名 {unrecognizedStore.NamedSavedThisRun}）");
        }

        _logger.LogInformation("{Summary}", sb.ToString().TrimEnd());
    }

    private void DisposeTemplates()
    {
        foreach (var template in _templates)
        {
            template.Dispose();
        }

        _templates.Clear();
    }

    public void Dispose()
    {
        DisposeTemplates();
    }

    private enum TemplateSource
    {
        Js,
        Unrecognized,
        Recognized
    }

    private readonly record struct NameHit(string? Name, string How, string Detail);

    private sealed class LoadedTemplate(
        string name,
        GridScreenName page,
        Mat icon,
        Mat grayIcon,
        TemplateSource source) : IDisposable
    {
        public string Name { get; } = name;
        public GridScreenName Page { get; } = page;
        public Mat ColorIcon { get; } = icon;
        public Mat GrayIcon { get; } = grayIcon;
        public TemplateSource Source { get; } = source;

        public void Dispose()
        {
            ColorIcon.Dispose();
            GrayIcon.Dispose();
        }
    }
}

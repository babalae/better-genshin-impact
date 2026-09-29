using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Wpf.Ui.Violeta.Controls;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoDomain;

public partial class AutoDomainTask
{
    private bool _guidePlanning;
    private bool _guideAdvance;
    private bool _guideReenter;
    private int _guideRounds;
    private int _guideRoundResin;
    private Model.ResinStatus? _guideResinStatus;
    private IReadOnlyDictionary<string, int>? _guideRoundRewards;
    private string? _guideLevelKey;
    private TrainingGuideFamilyPlan? _guideActivePlan;
    private readonly Dictionary<string, TrainingGuideFamilyPlan> _guidePlans = new();
    private readonly HashSet<string> _guideCompletedLevels = new();
    private readonly HashSet<string> _guideCompletedDomains = new();
    private List<string>? _guideDomainCandidates;
    private readonly Dictionary<TrainingGuideMaterial, int>? _guideCustomTargets;
    private readonly HashSet<string> _guideUnavailableFamilies = new();
    private sealed class GuideDomainCompleteException : Exception { }

    private int GuideReservePercent => TrainingGuideRunCalculator.ResolveCraftingBonusReservePercent(
        _taskParam.TrainingGuideRunPreference, _taskParam.TrainingGuideCraftingBonusReservePercent);

    private async Task RunTrainingGuidePlan()
    {
        while (_guideRounds < _taskParam.DomainRoundNum)
        {
            _ct.ThrowIfCancellationRequested();
            if (_guideDomainName == null)
            {
                _guideDomainName = await FindNextGuideDomain();
                if (_guideDomainName == null)
                {
                    if (_guideUnavailableFamilies.Count > 0)
                    {
                        Logger.LogWarning("自定义培养目标未全部完成，以下家族入口未开放或未识别，停止任务：{Families}",
                            string.Join("、", _guideUnavailableFamilies));
                        return;
                    }
                    await RunGuideFallback();
                    return;
                }
            }
            _guideAdvance = false;
            try { await DoDomain(); }
            catch (GuideDomainCompleteException)
            {
                _guideCompletedDomains.Add(_guideDomainName);
                Logger.LogInformation("培养计划：{Domain} 当前可识别的最高难度目标已处理完毕；未开放或未识别的自定义目标另行报告", _guideDomainName);
                _guideDomainName = null;
                _guideReenter = false;
                _guideActivePlan = null;
                await new ReturnMainUiTask().Start(_ct);
                continue;
            }
            // 树脂不足、次数用尽等原有退出原因不得触发下一秘境或备选秘境。
            if (!_guideAdvance) return;
            if (!await BetterGenshinImpact.GameTask.Common.BgiVision.Bv.WaitForMainUi(_ct, 30))
                throw new InvalidOperationException("退出秘境后未确认回到主界面，停止重新进入");
            await Delay(800, _ct);
            _guideReenter = true;
        }
    }

    private async Task<string?> FindNextGuideDomain()
    {
        if (_guideDomainCandidates == null)
        {
            _guideDomainCandidates = _guideCustomTargets == null ? await ScanGuideDomains() :
                _guideCustomTargets.Keys.Select(m => TrainingGuideEntryCatalog.Entries.Single(e =>
                    e.Family == m.Family && e.IsWeapon == m.IsWeapon).Domain).Distinct().ToList();
            if (_guideCustomTargets != null)
                Logger.LogInformation("自定义培养计划（目标库存）：{Targets}；跳过游戏提升指南预读",
                    string.Join("、", _guideCustomTargets.Select(p => $"{p.Key.Name}={p.Value}")));
        }
        var next = _guideDomainCandidates.FirstOrDefault(name => !_guideCompletedDomains.Contains(name));
        if (next != null) Logger.LogInformation("培养计划：从本次缓存选择 {Domain}", next);
        return next;
    }

    private async Task<List<string>> ScanGuideDomains()
    {
        await OpenTrainingGuide();
        using (var initial = CaptureToRectArea())
            BetterGenshinImpact.Core.Script.Dependence.GlobalMethod.MoveMouseTo((int)(initial.Width * .52), (int)(initial.Height * .45));
        // 这里只滚动秘境名称列表，不拖拽或点击冒险之证中的奖励材料。
        for (var i = 0; i < 20; i++) { Simulation.SendInput.Mouse.VerticalScroll(1); await Delay(70, _ct); }
        await Delay(500, _ct);
        var previous = string.Empty;
        var repeats = 0;
        var foundAny = false;
        var domains = new List<string>();
        var seen = new HashSet<string>();
        for (var page = 0; page < 20; page++)
        {
            using var capture = CaptureToRectArea();
            var rows = capture.FindMulti(RecognitionObject.Ocr(capture.Width * .39, capture.Height * .28,
                capture.Width * .18, capture.Height * .46));
            string signature;
            try
            {
                // 同样的名称仍可能正在向上移动，必须同时比较位置，不能仅凭文字判定到底。
                signature = string.Join("|", rows.OrderBy(r => r.Y).Select(r => $"{NormalizeGuideDomainName(r.Text)}@{r.Y / 4}"));
                foreach (var row in rows.OrderBy(r => r.Y))
                {
                    var text = NormalizeGuideDomainName(row.Text);
                    var matches = MapLazyAssets.Get().DomainPositionMap.Keys.Where(n => text.Contains(NormalizeGuideDomainName(n))).ToArray();
                    if (matches.Length != 1) continue;
                    foundAny = true;
                    if (seen.Add(matches[0])) domains.Add(matches[0]);
                }
            }
            finally { foreach (var row in rows) row.Dispose(); }
            repeats = signature == previous ? repeats + 1 : 0;
            previous = signature;
            if (repeats >= 2)
            {
                if (foundAny && string.IsNullOrWhiteSpace(signature))
                    throw new InvalidOperationException("提升指南列表连续识别为空，无法确认已到末尾；停止以避免缓存不完整的秘境列表");
                if (!foundAny)
                {
                    using var empty = capture.Find(RecognitionObject.Ocr(capture.Width * .38, capture.Height * .25,
                        capture.Width * .45, capture.Height * .5));
                    if (!(empty.Text.Contains("暂无") || empty.Text.Contains("没有") || empty.Text.Contains("已完成")))
                        throw new InvalidOperationException("提升指南未识别到秘境，也未确认无目标提示；停止，不能将OCR失败当作目标完成");
                }
                await new ReturnMainUiTask().Start(_ct);
                Logger.LogInformation("培养计划：首次扫描缓存 {Count} 个候选秘境：{Domains}", domains.Count, string.Join("、", domains));
                return domains;
            }
            // 首次滚动三次避开标题影响，之后每轮八次；每批完成后再 OCR。
            var scrollSteps = page == 0 ? 3 : 8;
            for (var step = 0; step < scrollSteps; step++)
            {
                Simulation.SendInput.Mouse.VerticalScroll(-1);
                await Delay(60, _ct);
            }
            await Delay(600, _ct);
        }
        throw new InvalidOperationException("提升指南名称扫描达到上限，停止以避免遗漏目标");
    }

    private async Task RunGuideFallback()
    {
        var name = _taskParam.TrainingGuideFallbackDomainName;
        if (string.IsNullOrWhiteSpace(name))
        {
            Logger.LogInformation("培养计划：没有更多目标，未配置备选秘境，结束");
            return;
        }
        if (name == TrainingGuideOption || !MapLazyAssets.Get().DomainPositionMap.ContainsKey(name))
            throw new InvalidOperationException("培养计划备选秘境无效，必须选择手动秘境");
        var originalName = _taskParam.DomainName;
        var originalSelection = _taskParam.SundaySelectedValue;
        var originalRounds = _taskParam.DomainRoundNum;
        _guidePlanning = false;
        _guideDomainName = null;
        _guideActivePlan = null;
        try
        {
            _taskParam.DomainName = name;
            _taskParam.SundaySelectedValue = _taskParam.TrainingGuideFallbackSundaySelectedValue;
            _taskParam.DomainRoundNum = Math.Max(0, originalRounds - _guideRounds);
            if (_taskParam.DomainRoundNum > 0)
            {
                Logger.LogInformation("培养计划：切换备选秘境 {Domain}", name);
                await DoDomain();
            }
        }
        finally
        {
            _guidePlanning = true;
            _taskParam.DomainName = originalName;
            _taskParam.SundaySelectedValue = originalSelection;
            _taskParam.DomainRoundNum = originalRounds;
        }
    }

    private async Task SelectPlannedGuideLevel(bool allOpen, bool scanOnly = false)
    {
        using (var screen = CaptureToRectArea())
            BetterGenshinImpact.Core.Script.Dependence.GlobalMethod.MoveMouseTo((int)(screen.Width * .25), (int)(screen.Height * .5));
        for (var i = 0; i < 37; i++) { Simulation.SendInput.Mouse.VerticalScroll(-1); await Delay(60, _ct); }
        await Delay(600, _ct);
        using var capture = CaptureToRectArea();
        var rows = capture.FindMulti(RecognitionObject.Ocr(0, capture.Height * .12, capture.Width * .47, capture.Height * .84));
        try
        {
            var levels = rows.Where(r => r.Text.Contains("秘境") &&
                System.Text.RegularExpressions.Regex.IsMatch(r.Text.Trim(), @"[IVXⅠⅡⅢⅣⅤⅥ]+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                .OrderBy(r => r.Y).ToArray();
            var count = allOpen ? 3 : 1;
            if (levels.Length < count) throw new InvalidOperationException("未完整识别底部最高难度关卡，停止培养规划");
            var candidates = levels.TakeLast(count).ToArray();
            var availableEntries = candidates.Select(r => TrainingGuideEntryCatalog.Find(_guideDomainName!, r.Text))
                .OfType<TrainingGuideEntry>().ToHashSet();
            var tiers = candidates.Select(r => System.Text.RegularExpressions.Regex.Match(r.Text.Trim(), @"[IVXⅠⅡⅢⅣⅤⅥ]+$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase).Value).Distinct().ToArray();
            if (tiers.Length != 1) throw new InvalidOperationException("底部候选难度不一致，列表可能未滚到底部");
            var planned = new List<(BetterGenshinImpact.GameTask.Model.Area.Region Region, string Key)>();
            foreach (var candidate in candidates)
            {
                var key = _guideDomainName + ":" + TrainingGuideMaterialCatalog.Normalize(candidate.Text);
                if (!scanOnly && _guideCompletedLevels.Contains(key)) continue;
                if (!scanOnly && _guideCustomTargets != null)
                {
                    var entry = TrainingGuideEntryCatalog.Find(_guideDomainName ?? string.Empty, candidate.Text);
                    if (entry != null && !_guideCustomTargets.Keys.Any(m => m.Family == entry.Family && m.IsWeapon == entry.IsWeapon)) continue;
                }
                if (scanOnly && !ShouldScanGuideEntry(candidate.Text)) continue;
                if (scanOnly) MarkGuideScanAttempt(TrainingGuideEntryCatalog.Find(_guideDomainName ?? string.Empty, candidate.Text));
                candidate.Click();
                await Delay(700, _ct);
                if (scanOnly)
                {
                    try { await ReadEntryMaterials(candidate.Text, scanOnly: true); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e)
                    {
                        _guideScanEntryFailed++;
                        RecordGuideScan($"{key}：入口扫描失败，{e.Message}");
                    }
                    continue;
                }
                if (_guideCustomTargets == null)
                {
                    using var detail = CaptureToRectArea();
                    using var demand = detail.Find(RecognitionObject.Ocr(detail.Width * .48, detail.Height * .49, detail.Width * .5, detail.Height * .33));
                    if (!demand.Text.Contains("需求角色"))
                    {
                        Logger.LogInformation("培养计划：{Level} 未出现需求角色，跳过", candidate.Text);
                        continue;
                    }
                }
                var materials = await ReadEntryMaterials(candidate.Text, availableEntries: availableEntries);
                if (materials.Count == 0) continue;
                if (!_guidePlans.TryGetValue(key, out var plan)) _guidePlans[key] = plan = new(materials);
                else plan.Refresh(materials);
                var remaining = plan.RemainingResin(GuideReservePercent);
                if (remaining == null) throw new InvalidOperationException($"{candidate.Text}：低级库存不完整，不能开始刷取");
                ReportGuidePlan(candidate.Text, plan, remaining.Value);
                if (remaining == 0) _guideCompletedLevels.Add(key);
                else planned.Add((candidate, key));
            }
            if (scanOnly) return;
            if (_guideCustomTargets != null)
            {
                var requestedEntries = TrainingGuideEntryCatalog.Entries.Where(e => e.Domain == _guideDomainName &&
                    _guideCustomTargets.Keys.Any(m => m.Family == e.Family && m.IsWeapon == e.IsWeapon)).ToArray();
                foreach (var available in availableEntries) _guideUnavailableFamilies.Remove(available.Family);
                foreach (var unavailable in requestedEntries.Where(e => !availableEntries.Contains(e)))
                {
                    _guideUnavailableFamilies.Add(unavailable.Family);
                    Logger.LogWarning("自定义培养目标：{Domain}/{Entry} 未开放或入口未识别，本次跳过", unavailable.Domain, unavailable.Entry);
                }
            }
            if (planned.Count == 0) throw new GuideDomainCompleteException();
            var selected = planned[0];
            selected.Region.Click();
            await Delay(700, _ct);
            _guideLevelKey = selected.Key;
            _guideActivePlan = _guidePlans[selected.Key];
        }
        finally { foreach (var row in rows) row.Dispose(); }
    }

    private async Task<List<TrainingGuideMaterialReading>> ReadEntryMaterials(string level, bool scanOnly = false,
        ISet<TrainingGuideEntry>? availableEntries = null)
    {
        using var capture = CaptureToRectArea();
        var texts = capture.FindMulti(RecognitionObject.Ocr(capture.Width * .48, capture.Height * .35, capture.Width * .5, capture.Height * .4));
        Rect band;
        try
        {
            var label = texts.FirstOrDefault(r => r.Text.Contains("可能获得奖励"));
            if (label == null) throw new InvalidOperationException("秘境入口未识别到可能获得奖励区域");
            // 从标题底部开始，保留图标边框，避免固定偏移切掉顶部或底部轮廓。
            var top = label.Y + label.Height + (int)(capture.Height * .006);
            var height = Math.Min((int)(capture.Height * .145), capture.Height - top);
            band = new Rect((int)(capture.Width * .475), top, (int)(capture.Width * .48), height);
            Logger.LogInformation("秘境材料图标区域定位：画面 {Width}x{Height}，标题 ({X},{Y},{W},{H})，裁剪 {Band}",
                capture.Width, capture.Height, label.X, label.Y, label.Width, label.Height, band);
        }
        finally { foreach (var text in texts) text.Dispose(); }
        using var strip = new Mat(capture.SrcMat, band);
        var icons = TrainingGuideEntryMaterialIconLocator.FindIcons(strip, capture.Height / 1080d);
        if (icons.Count is not (5 or 6))
        {
            TrainingGuideDiagnostics.Save(strip, Logger, "entry-material-icons");
            throw new InvalidOperationException($"秘境入口未确认完整的5/6个图标（摩拉、阅历及3/4级材料图标），已停止点击，请查看材料图标区域定位日志");
        }
        var materials = new List<TrainingGuideMaterialReading>();
        var entry = TrainingGuideEntryCatalog.Find(_guideDomainName ?? string.Empty, level);
        var identified = new List<(Rect Icon, TrainingGuideMaterial Material)>();
        var recognitionId = $"icons-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}";
        // 整个入口共用模型会话，先确认完整家族，再打开任何材料弹窗。
        using (var recognizer = ItemIconRecognizerFactory.CreateConfigured())
        {
            for (var index = 0; index < icons.Count; index++)
            {
                _ct.ThrowIfCancellationRequested();
                var rect = icons[index];
                using var crop = new Mat(strip, rect);
                using var bgr = new Mat();
                if (crop.Channels() == 4) Cv2.CvtColor(crop, bgr, ColorConversionCodes.BGRA2BGR);
                else crop.CopyTo(bgr);
                using var normalized = new Mat();
                Cv2.Resize(bgr, normalized, new Size(125, 125), 0, 0, InterpolationFlags.Cubic);
                var name = recognizer.Recognize(normalized);
                var material = name == null ? null : TrainingGuideMaterialCatalog.Find(name);
                if (_taskParam.TrainingGuideOcrDebugEnabled)
                    TrainingGuideDiagnostics.Save(normalized, Logger, $"material-{index + 1}", recognitionId);
                Logger.LogInformation("培养图标识别 {Level}，位置 {Index}：{Name}", level, index + 1, name ?? "未识别");
                if (material != null) identified.Add((rect, material));
            }
        }
        // 最高难度奖励按等级从高到低横向排列，防止同一家族的等级互认后绑定错误库存。
        var tiersByPosition = identified.OrderBy(x => x.Icon.X).Select(x => x.Material.Tier).ToArray();
        var tiersInOrder = tiersByPosition.SequenceEqual(tiersByPosition.OrderByDescending(tier => tier));
        var family = identified.FirstOrDefault().Material;
        if (family == null || identified.Count != (family.IsWeapon ? 4 : 3) ||
            identified.Any(x => x.Material.Family != family.Family || x.Material.IsWeapon != family.IsWeapon) ||
            identified.Select(x => x.Material.Tier).Distinct().Count() != identified.Count ||
            !tiersInOrder ||
            (entry != null && (family.Family != entry.Family || family.IsWeapon != entry.IsWeapon)))
        {
            TrainingGuideDiagnostics.Save(capture.SrcMat, Logger, "capture", recognitionId);
            TrainingGuideDiagnostics.Save(strip, Logger, "strip", recognitionId);
            TrainingGuideDiagnostics.AppendOcrIssue(Logger, recognitionId,
                $"入口={level}；图标数量={icons.Count}；已识别={string.Join("、", identified.Select(x => x.Material.Name))}；横向等级={string.Join(",", tiersByPosition)}；结果=家族或等级不完整/等级顺序异常/与入口冲突");
            throw new InvalidOperationException($"{level}：图标识别未确认完整材料家族或等级顺序，停止本入口扫描");
        }
        // 入口文字识别失败时，用已经确认的完整图标家族补全入口身份。
        entry ??= TrainingGuideEntryCatalog.Entries.FirstOrDefault(e => e.Domain == _guideDomainName &&
            e.Family == family.Family && e.IsWeapon == family.IsWeapon);
        if (entry == null)
            throw new InvalidOperationException($"{level}: 图标家族不属于当前秘境，停止本入口识别");
        availableEntries?.Add(entry);
        // 完整图标家族确认入口后再筛选，非目标入口不读取弹窗、不计入扫描成功数。
        if (scanOnly && _guideScanEntries?.Contains(entry) != true) return materials;
        if (!scanOnly && _guideCustomTargets != null &&
            !_guideCustomTargets.Keys.Any(m => m.Family == entry.Family && m.IsWeapon == entry.IsWeapon)) return materials;
        if (scanOnly) MarkGuideScanAttempt(entry);
        foreach (var (icon, expected) in identified)
        {
            capture.ClickTo(band.X + icon.X + icon.Width / 2, band.Y + icon.Y + icon.Height / 2);
            await Delay(600, _ct);
            TrainingGuideMaterialReading? reading;
            try
            {
                reading = await new TrainingGuidePopupRecognizer(Logger, _ct,
                    _taskParam.TrainingGuideOcrDebugEnabled).ReadStable(expected, entry);
            }
            finally
            {
                if (!_ct.IsCancellationRequested)
                {
                    using (var popup = CaptureToRectArea()) popup.ClickTo(popup.Width * .94, popup.Height * .40);
                    await Delay(400, _ct);
                }
            }
            if (reading == null)
            {
                if (scanOnly)
                {
                    _guideScanFailed++;
                    if (entry != null) _guideScanFailedMaterials.Add(expected);
                    RecordGuideScan($"{_guideDomainName}/{level}：材料 {expected.Name} 数量识别失败");
                    continue;
                }
                throw new InvalidOperationException("秘境材料库存识别失败，请查看培养浮窗OCR日志；停止以避免错误刷取");
            }
            if (!scanOnly && _guideCustomTargets != null)
            {
                var required = _guideCustomTargets.GetValueOrDefault(reading.Material);
                reading = reading with { Required = required, IsTarget = required > 0 };
            }
            materials.Add(reading);
            if (scanOnly)
            {
                _guideScanSucceeded++;
                if (entry != null) _guideScanReadMaterials.Add(reading.Material);
                RecordGuideScan($"{_guideDomainName}/{level}：{reading.Material.Name} {reading.Stock}/{(reading.IsTarget ? reading.Required.ToString() : "-")}");
            }
        }
        if (scanOnly)
        {
            if (entry != null && materials.Count == identified.Count) _guideScanCompletedEntries.Add(entry);
            return materials;
        }
        if (!materials.Any(m => m.IsTarget))
            throw new InvalidOperationException("已确认需求角色，但未读到材料的培养需求数字，暂不能规划");
        if (materials.Select(m => m.Material.Family).Distinct().Count() != 1)
            throw new InvalidOperationException("奖励材料未能归入同一材料家族，停止规划");
        return materials;
    }

    private void ReportGuidePlan(string level, TrainingGuideFamilyPlan plan, int remaining)
    {
        var materials = string.Join("、", plan.Materials.Select(m =>
            $"{m.Material.Name} {m.Stock}/{(m.IsTarget ? m.Required.ToString() : "-")}"));
        Logger.LogInformation("培养材料（库存/目标）{Level}：{Materials}", level, materials);
        var allocation = DescribeGuideResinAllocation(remaining);
        Logger.LogInformation("培养树脂规划 {Level}：当前进度 {Spent}/{Total}体；{Allocation}；合成预留 {Reserve}%",
            level, plan.KnownResinSpent, (long)plan.KnownResinSpent + remaining, allocation, GuideReservePercent);
        UIDispatcherHelper.BeginInvoke(() => Toast.Information($"培养材料（库存/目标）\n{level}\n{materials}"));
        UIDispatcherHelper.BeginInvoke(() => Toast.Information($"培养树脂规划\n当前进度 {plan.KnownResinSpent}/{(long)plan.KnownResinSpent + remaining}体；{allocation}；合成预留 {GuideReservePercent}%"));
    }

    private static int SelectGuideResinAmount(int remaining, int condensed, int original) =>
        remaining <= 0 ? 0 : condensed > 0 && (remaining >= 60 || original < 20) ? 60 :
        original >= 40 && remaining >= 40 ? 40 : original >= 20 ? 20 : 0;

    private string DescribeGuideResinAllocation(int remaining)
    {
        if (remaining <= 0) return "材料需求已满足";
        if (_taskParam.SpecifyResinUse)
        {
            if (_resinPriorityListWhenSpecifyUse.Any(r => r.Name == "原粹树脂" && r.RemainCount > 0))
                return "指定额度包含未指定档位的原粹树脂，20/40体用量待领奖确认";
            var allowances = _resinPriorityListWhenSpecifyUse.Select(r => new TrainingGuideResinAllowance(
                r.Name, r.Name == "原粹树脂20" ? 20 : r.Name == "原粹树脂40" ? 40 : 60,
                Math.Max(0, r.RemainCount))).ToArray();
            var plan = TrainingGuideResinPlanner.Allocate(new[] { remaining }, allowances);
            return "按指定额度预计：" + string.Join("、", plan.Uses) +
                (plan.UncoveredResin > 0 ? $"；额度尚缺{plan.UncoveredResin}体" : "");
        }
        if (_guideResinStatus == null) return "组合次数待领奖时读取树脂库存";
        var condensed = _guideResinStatus.CondensedResinCount;
        var original = _guideResinStatus.OriginalResinCount;
        var shortage = remaining;
        var counts = new Dictionary<int, int> { [60] = 0, [40] = 0, [20] = 0 };
        while (shortage > 0)
        {
            var amount = SelectGuideResinAmount(shortage, condensed, original);
            if (amount == 0) break;
            counts[amount]++;
            if (amount == 60) condensed--; else original -= amount;
            shortage -= amount;
        }
        var uses = string.Join("、", new[] { 60, 40, 20 }.Where(n => counts[n] > 0)
            .Select(n => $"{n}体{counts[n]}次"));
        return (uses.Length == 0 ? "暂无可用树脂" : "预计" + uses) +
            (shortage > 0 ? $"；树脂尚缺{shortage}体" : shortage < 0 ? $"（预计超出{-shortage}体）" : "");
    }

    private async Task<bool> UseTrainingGuideResin(Model.ResinStatus status)
    {
        var remaining = _guideActivePlan?.RemainingResin(GuideReservePercent);
        if (remaining == null || remaining <= 0)
            throw new InvalidOperationException("培养计划剩余需求无效，停止领取以避免错误消耗树脂");

        _guideResinStatus = status;
        var amount = SelectGuideResinAmount(remaining.Value, status.CondensedResinCount, status.OriginalResinCount);
        if (amount == 0) return false;
        var useCondensed = amount == 60;
        Logger.LogInformation("培养树脂组合：{Allocation}", DescribeGuideResinAllocation(remaining.Value));
        Logger.LogInformation("培养计划：剩余预计 {Remaining}体，本轮选择 {Amount}体{Resin}",
            remaining, amount, useCondensed ? "浓缩树脂" : "原粹树脂");
        if (useCondensed)
        {
            using var capture = CaptureToRectArea();
            var (success, _) = PressUseResin(capture, "浓缩树脂");
            if (success)
            {
                _guideRoundResin = 60;
                status.CondensedResinCount--;
            }
            return success;
        }

        bool switched;
        try
        {
            switched = SwitchOriginalResinType(amount, _ct);
        }
        catch (RetryException)
        {
            Logger.LogWarning("培养计划：切换原粹树脂类型重试超时，本轮停止领奖");
            return false;
        }

        if (!switched) return false;
        await Delay(400, _ct);
        // 切档后重新截图，并只校验原粹树脂所在行，不能使用旧截图或默认20体记账。
        using var screen = CaptureToRectArea();
        var texts = screen.FindMulti(RecognitionObject.Ocr(screen.Width * .25, screen.Height * .2, screen.Width * .5, screen.Height * .6));
        try
        {
            var resin = texts.FirstOrDefault(t => System.Text.RegularExpressions.Regex.IsMatch(t.Text, ResolveResinNamePattern("原粹树脂")));
            if (resin == null) return false;
            var amounts = System.Text.RegularExpressions.Regex.Matches(resin.Text, @"(?<!\d)(20|40)(?!\d)")
                .Select(m => int.Parse(m.Value)).Distinct().ToArray();
            if (amounts.Length != 1 || amounts[0] != amount)
            {
                Logger.LogWarning("培养计划：未确认原粹树脂已切换到 {Amount}体，停止领取；识别文本：{Text}", amount, resin.Text);
                return false;
            }
            var (success, _) = PressUseResin(texts, "原粹树脂");
            if (success)
            {
                _guideRoundResin = amount;
                status.OriginalResinCount -= amount;
            }
            return success;
        }
        finally { foreach (var text in texts) text.Dispose(); }
    }

    private void SaveGuideRewardOcrImage(Mat image, string stage) =>
        TrainingGuideDiagnostics.Save(image, Logger, $"reward-{stage}");

    private bool UpdateGuideAfterReward(bool resinExhausted)
    {
        if (!_guidePlanning || _guideActivePlan == null) return false;
        _guideRounds++;
        var updated = _guideActivePlan.ApplyRewards(_guideRoundRewards, _guideRoundResin);
        if (_guideRoundResin == 0)
            Logger.LogWarning("培养计划：本轮树脂用量未确认，仅按材料更新库存，不计入每体掉落样本");
        var remaining = updated ? _guideActivePlan.RemainingResin(GuideReservePercent) : null;
        if (remaining == 0 && _guideLevelKey != null) _guideCompletedLevels.Add(_guideLevelKey);
        if (remaining != null) ReportGuidePlan(_guideLevelKey ?? "当前关卡", _guideActivePlan, remaining.Value);
        else Logger.LogWarning("培养计划：奖励数据不完整，将退出并在入口重新读取库存");
        var limitReached = _guideRounds >= _taskParam.DomainRoundNum;
        var change = remaining == null || remaining == 0;
        _guideAdvance = change && !resinExhausted && !limitReached;
        return change || limitReached;
    }
}

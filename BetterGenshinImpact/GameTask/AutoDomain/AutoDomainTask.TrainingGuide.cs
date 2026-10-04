using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Input;
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
    private TrainingGuideEntryLayout? _guideEntryLayout;
    private readonly Dictionary<bool, int> _guideDifficultyCaps = new();
    private enum GuideNextAction { Continue, Stop, AdvancePlan }
    private GuideNextAction _guideNextAction;
    private string? _guideScannedDomain;
    private readonly List<(TrainingGuideEntry Entry, int Difficulty, string Key)> _guidePendingEntries = new();
    private readonly HashSet<string> _guideDomainsWithObservedDemand = new();
    private int _guideRounds;
    private int _guideRoundResin;
    private Model.ResinStatus? _guideResinStatus;
    private IReadOnlyDictionary<string, int>? _guideRoundRewards;
    private string? _guideLevelKey;
    private TrainingGuideFamilyPlan? _guideActivePlan;
    private readonly Dictionary<string, TrainingGuideFamilyPlan> _guidePlans = new();
    private readonly HashSet<string> _guideProcessedLevels = new();
    private readonly HashSet<string> _guideProcessedDomains = new();
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
            _guideNextAction = GuideNextAction.Continue;
            try { await DoDomain(); }
            catch (GuideDomainCompleteException)
            {
                _guideProcessedDomains.Add(_guideDomainName);
                Logger.LogInformation("培养计划：{Domain} 当前没有待执行入口；未展示的高级材料不计入本次目标", _guideDomainName);
                _guideDomainName = null;
                _guideActivePlan = null;
                ClearGuideEntryQueue();
                await new ReturnMainUiTask().Start(_ct);
                continue;
            }
            // 树脂不足、次数用尽等原有退出原因不得触发下一秘境或备选秘境。
            if (_guideNextAction is GuideNextAction.Continue or GuideNextAction.Stop) return;
            if (!await BetterGenshinImpact.GameTask.Common.BgiVision.Bv.WaitForMainUi(_ct, 30))
                throw new InvalidOperationException("退出秘境后未确认回到主界面，停止重新进入");
            await Delay(800, _ct);
            if (_guideNextAction == GuideNextAction.AdvancePlan)
            {
                _guidePendingEntries.RemoveAll(e => _guideProcessedLevels.Contains(e.Key));
                if (_guideScannedDomain == _guideDomainName && _guidePendingEntries.Count == 0)
                {
                    Logger.LogInformation("培养计划：{Domain} 已缓存的入口均已执行结束，不重进复查；预算结束不代表实际需求已核实", _guideDomainName);
                    _guideProcessedDomains.Add(_guideDomainName!);
                    _guideDomainName = null;
                    _guideActivePlan = null;
                    _guideLevelKey = null;
                    ClearGuideEntryQueue();
                    continue;
                }
                Logger.LogInformation("培养计划：切换同秘境剩余目标，剩余 {Count} 个入口", _guidePendingEntries.Count);
            }
            // 下一轮从 DoDomain -> TpDomain -> EnterDomain 的正常入口重新开始。
            // 保留预算、完成记录和候选缓存；只让新画面重新建立入口坐标与选中状态。
            Logger.LogInformation("培养计划：已确认出本，下一轮重新传送到 {Domain} 并重新选择入口", _guideDomainName);
        }
    }

    private void ClearGuideEntryQueue()
    {
        _guideScannedDomain = null;
        _guidePendingEntries.Clear();
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
        var next = _guideDomainCandidates.FirstOrDefault(name => !_guideProcessedDomains.Contains(name));
        if (next != null) Logger.LogInformation("培养计划：从本次缓存选择 {Domain}", next);
        return next;
    }

    private async Task<List<string>> ScanGuideDomains()
    {
        await OpenTrainingGuide();
        using (var initial = CaptureToRectArea())
            BetterGenshinImpact.Core.Script.Dependence.GlobalMethod.MoveMouseTo((int)(initial.Width * .52), (int)(initial.Height * .45));
        // 这里只滚动秘境名称列表，不拖拽或点击冒险之证中的奖励材料。
        for (var i = 0; i < 20; i++) { InputHub.Foreground.Mouse.VerticalScroll(1); await Delay(70, _ct); }
        await Delay(500, _ct);
        var previous = new List<(string Name, int Y)>();
        using var previousListImage = new Mat();
        var confirmingBottom = false;
        var domains = new List<string>();
        var seen = new HashSet<string>();
        // 周本和圣遗物秘境可作为页面识别成功的证据，但不能进入材料培养规划。
        var matcher = new TrainingGuideDomainMatcher(MapLazyAssets.Get().ScenesDic.Values
            .SelectMany(scene => scene.Points)
            .Where(point => point.Type is "BlessDomain" or "ForgeryDomain" or "MasteryDomain" or "TrounceDomain")
            .Select(point => point.Name).OfType<string>().Where(name => !string.IsNullOrWhiteSpace(name)),
            NormalizeGuideDomainName);
        for (var page = 0; page < 20; page++)
        {
            using var capture = CaptureToRectArea();
            var rows = capture.FindMulti(RecognitionObject.Ocr(capture.Width * .39, capture.Height * .28,
                capture.Width * .18, capture.Height * .46));
            var current = new List<(string Name, int Y)>();
            var recognizedDomain = false;
            var countBefore = domains.Count;
            using var listImage = new Mat(capture.SrcMat, new Rect(
                (int)(capture.Width * .39), (int)(capture.Height * .28),
                (int)(capture.Width * .18), (int)(capture.Height * .46)));
            try
            {
                TrainingGuideDiagnostics.Detail("候选秘境测试：第 {Pass} 次截图，OCR 共 {Count} 行", page + 1, rows.Count);
                foreach (var row in rows.OrderBy(r => r.Y))
                {
                    var match = matcher.Match(row.Text);
                    TrainingGuideDiagnostics.Detail("候选秘境测试：原文【{Raw}】，标准化【{Normalized}】，位置 ({X},{Y})，匹配【{Name}】，结果：{Result}",
                        row.Text, NormalizeGuideDomainName(row.Text), row.X, row.Y,
                        match?.Name ?? "无唯一匹配",
                        match == null ? "不加入候选" : !match.Value.Supported ? "不支持材料规划" :
                        seen.Contains(match.Value.Name) ? "重复候选" : "新增候选");
                    if (match == null) continue;
                    recognizedDomain = true;
                    if (match.Value.Supported) current.Add((match.Value.Name, row.Y));
                    if (!seen.Add(match.Value.Name)) continue;
                    if (match.Value.Supported) domains.Add(match.Value.Name);
                    else Logger.LogInformation("培养计划：忽略不支持材料规划的目标 {Domain}", match.Value.Name);
                }
            }
            finally { foreach (var row in rows) row.Dispose(); }
            // 只比较材料秘境；图标乱码、地区文字和周本名称不参与位置判断。
            var tolerance = Math.Max(1, (int)Math.Round(capture.Height * 5d / 1080));
            var stable = current.Count > 0 && current.Count == previous.Count &&
                current.Zip(previous, (a, b) => a.Name == b.Name && Math.Abs(a.Y - b.Y) <= tolerance).All(equal => equal);
            if (current.Count == 0 && previous.Count == 0 && !previousListImage.Empty())
            {
                // 无材料本时用列表图像确认是否停止移动，不能把两次空 OCR 当作到底。
                stable = listImage.Size() == previousListImage.Size() &&
                    Cv2.Norm(listImage, previousListImage, NormTypes.L1) /
                    (listImage.Total() * listImage.Channels()) <= 1d;
            }
            stable &= domains.Count == countBefore;
            TrainingGuideDiagnostics.Detail("候选秘境测试：第 {Pass} 次识别结束，位置稳定 {Stable}，停滚复核 {Confirming}，累计候选：{Domains}",
                    page + 1, stable, confirmingBottom, domains.Count == 0 ? "（空）" : string.Join("、", domains));
            if (stable && confirmingBottom)
            {
                if (current.Count == 0 && !recognizedDomain)
                {
                    using var empty = capture.Find(RecognitionObject.Ocr(capture.Width * .38, capture.Height * .25,
                        capture.Width * .45, capture.Height * .5));
                    TrainingGuideDiagnostics.Detail("候选秘境测试：无目标提示复核 OCR【{Text}】", empty.Text);
                    if (!(empty.Text.Contains("暂无") || empty.Text.Contains("没有") || empty.Text.Contains("已完成")))
                        throw new InvalidOperationException("提升指南未识别到秘境，也未确认无目标提示；停止，不能将OCR失败当作目标完成");
                }
                await new ReturnMainUiTask().Start(_ct);
                Logger.LogInformation("培养计划：首次扫描缓存 {Count} 个候选秘境：{Domains}", domains.Count, string.Join("、", domains));
                return domains;
            }
            previous = current;
            listImage.CopyTo(previousListImage);
            confirmingBottom = stable;
            if (confirmingBottom)
            {
                TrainingGuideDiagnostics.Detail("候选秘境测试：疑似到底，停止滚动并等待 800 毫秒后复核");
                await Delay(800, _ct);
                continue;
            }
            // 首次滚动三次避开标题影响，之后每轮八次；每批完成后再 OCR。
            var scrollSteps = page == 0 ? 3 : 8;
            for (var step = 0; step < scrollSteps; step++)
            {
                InputHub.Foreground.Mouse.VerticalScroll(-1);
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

    private async Task SelectPlannedGuideLevel(bool allOpen)
    {
        using (var screen = CaptureToRectArea())
            BetterGenshinImpact.Core.Script.Dependence.GlobalMethod.MoveMouseTo((int)(screen.Width * .25), (int)(screen.Height * .5));
        await Delay(100, _ct);
        TrainingGuideEntry[] candidates = Array.Empty<TrainingGuideEntry>();
        _guideEntryLayout = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if ((allOpen && attempt == 0) || (!allOpen && attempt == 1))
            {
                for (var i = 0; i < 37; i++) { InputHub.Foreground.Mouse.VerticalScroll(-1); await Delay(60, _ct); }
                await Delay(600, _ct);
            }
            using var capture = CaptureToRectArea();
            var rows = capture.FindMulti(RecognitionObject.Ocr(0, capture.Height * .12, capture.Width * .47, capture.Height * .84));
            var groupingId = $"entry-group-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}";
            try
            {
                var scale = capture.Height / 1080d;
                var rawRows = rows.Select(r => new { Region = r, Entry = TrainingGuideEntryCatalog.Find(_guideDomainName!, r.Text),
                    CenterY = r.Y + r.Height / 2d }).OrderBy(r => r.CenterY).ToArray();
                TrainingGuideDiagnostics.LogEntryVerification(groupingId,
                    $"分组初始化；轮次={attempt + 1}/3；限时全开={allOpen}；画面={capture.Width}x{capture.Height}；原始OCR=" +
                    string.Join(" | ", rawRows.Select(r => $"[{r.Region.Text}]，区域=({r.Region.X},{r.Region.Y},{r.Region.Width},{r.Region.Height})，中心Y={r.CenterY:F1}，匹配={r.Entry?.Entry ?? "未确认"}")));
                // 先保留物理标题行，不能先删除名称匹配失败的行再从上方补足三行。
                var titleRows = rawRows.Where(r => r.Entry != null || r.Region.Text.Contains("秘境")).ToArray();
                // 同一标题的重复OCR只算一行；冲突或未识别结果交由下方校验拒绝。
                var physicalRows = titleRows.Where((r, i) => i == 0 ||
                    r.CenterY - titleRows[i - 1].CenterY > 20 * scale).ToArray();
                var count = allOpen ? 3 : 4;
                var bottomRows = physicalRows.TakeLast(count).ToArray();
                TrainingGuideDiagnostics.LogEntryVerification(groupingId,
                    $"标题OCR数={titleRows.Length}；去重后物理行数={physicalRows.Length}；待核验底部行=" +
                    string.Join(" | ", bottomRows.Select(r => $"[{r.Region.Text}]@{r.CenterY:F1}=>{r.Entry?.Entry ?? "未确认"}")));
                if (bottomRows.Length != count || bottomRows.Any(r => r.Entry == null) ||
                    bottomRows.Zip(bottomRows.Skip(1), (a, b) => b.CenterY - a.CenterY)
                        .Any(gap => gap < 90 * scale || gap > 130 * scale))
                    throw new InvalidOperationException("底部连续入口缺行、行距异常或名称未确认，不能用上方入口补位");
                // 即使整条标题漏读，也不能将其上方一组误当成列表末尾。
                if (rawRows.Any(r => r.Region.Text.Contains("推荐") && r.Region.Text.Contains("等级") &&
                    r.CenterY > bottomRows[^1].CenterY + 90 * scale))
                    throw new InvalidOperationException("最后标题下方仍存在其他入口说明，疑似末行标题漏读");
                var namedRows = physicalRows.Where(r => r.Entry != null).ToArray();
                // 普通开放仅四行，必须完整识别，避免将漏读末行后的 III 当成 IV。
                if (!allOpen && (namedRows.Length != 4 || namedRows.Select(r => r.Entry).Distinct().Count() != 1 ||
                    namedRows.Any(r => r.Region.Y <= capture.Height * .12 ||
                        r.Region.Y + r.Region.Height >= capture.Height * .96)))
                    throw new InvalidOperationException("普通开放未确认完整的四个同名入口");
                _guideEntryLayout = new TrainingGuideEntryLayout(namedRows.Select(r =>
                    new TrainingGuideEntryLayout.Row(r.Entry!, r.Region.Y + r.Region.Height / 2d)).ToArray(),
                    allOpen ? 3 : 1, capture.Height / 1080d);
                candidates = _guideEntryLayout.Group;
                TrainingGuideDiagnostics.Detail("培养入口分组：每组 {Count} 行，底部为 IV，行距 {Pitch:F1}；名称顺序 {Names}；罗马数字不参与定位",
                    candidates.Length, _guideEntryLayout.Pitch, string.Join("、", candidates.Select(e => e.Entry)));
                if (!allOpen && attempt == 0)
                    TrainingGuideDiagnostics.Detail("培养入口：普通开放四行完整可见，跳过滚动，直接按位置选择难度");
                break;
            }
            catch (InvalidOperationException e)
            {
                TrainingGuideDiagnostics.Save(capture.SrcMat, "failed-screen", groupingId);
                TrainingGuideDiagnostics.LogEntryVerification(groupingId,
                    $"分组失败；原因={e.Message}；后续={(attempt == 2 ? "停止" : !allOpen && attempt == 0 ? "滚到底后复查" : "原地等待后复查")}");
                if (attempt == 2) throw;
            }
            finally { foreach (var row in rows) row.Dispose(); }
            await Delay(600, _ct);
        }
        // 已完成整座秘境的首次扫描时，直接选择剩余计划，不重复检查无需求入口。
        if (_guideScannedDomain == _guideDomainName)
        {
            _guidePendingEntries.RemoveAll(e => _guideProcessedLevels.Contains(e.Key));
            if (_guidePendingEntries.Count == 0) throw new GuideDomainCompleteException();
            if (_guidePendingEntries.Any(e => !candidates.Contains(e.Entry)))
                throw new InvalidOperationException("已规划入口不在当前开放列表中，停止使用旧计划");
            await ActivateGuideEntry(_guidePendingEntries[0]);
            return;
        }
        var availableEntries = candidates.ToHashSet();
        var planned = new List<(TrainingGuideEntry Entry, int Difficulty, string Key)>();
        foreach (var entry in candidates)
        {
            if (_guideCustomTargets != null && !_guideCustomTargets.Keys.Any(m => m.Family == entry.Family && m.IsWeapon == entry.IsWeapon)) continue;
            var difficulty = _guideDifficultyCaps.GetValueOrDefault(entry.IsWeapon, 4);
            var key = $"{entry.Domain}:{entry.Family}:{difficulty}";
            if (_guideProcessedLevels.Contains(key)) continue;
            difficulty = await SelectUnlockedGuideEntry(entry, difficulty);
            key = $"{entry.Domain}:{entry.Family}:{difficulty}";
            if (_guideProcessedLevels.Contains(key)) continue;
            if (_guideCustomTargets == null)
            {
                // SelectUnlockedGuideEntry 已完成点击等待及选中验证，此后两帧之间不操作界面。
                if (!await TrainingGuideDemandReader.ReadAsync(entry.Entry, Logger, _ct))
                {
                    Logger.LogInformation("培养计划：{Level} 难度 {Difficulty} 连续两帧未识别到需求角色，跳过本入口", entry.Entry, difficulty);
                    continue;
                }
                _guideDomainsWithObservedDemand.Add(_guideDomainName!);
            }
            var materials = await ReadEntryMaterials(entry, difficulty);
            if (materials.Count == 0) continue;
            if (!_guidePlans.TryGetValue(key, out var plan)) _guidePlans[key] = plan = new(materials, difficulty);
            else if (_taskParam.TrainingGuideRewardRecognitionEnabled) plan.Refresh(materials);
            var remaining = RemainingGuideResin(plan);
            if (remaining == null) throw new InvalidOperationException($"{entry.Entry}：已识别材料之间存在库存缺失，不能开始刷取");
            ReportGuidePlan(entry.Entry, plan, remaining.Value);
            if (remaining == 0) _guideProcessedLevels.Add(key);
            else planned.Add((entry, difficulty, key));
        }
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
        if (planned.Count == 0 && _guideCustomTargets == null &&
            !_guideDomainsWithObservedDemand.Contains(_guideDomainName!))
            throw new InvalidOperationException($"{_guideDomainName}：所有候选入口均未确认需求角色，停止培养任务，请检查游戏内培养计划；不刷新候选缓存、不进入备选秘境");
        if (planned.Count == 0) throw new GuideDomainCompleteException();
        _guideScannedDomain = _guideDomainName;
        _guidePendingEntries.Clear();
        _guidePendingEntries.AddRange(planned);
        await ActivateGuideEntry(_guidePendingEntries[0]);
    }

    private async Task ActivateGuideEntry((TrainingGuideEntry Entry, int Difficulty, string Key) selected)
    {
        var selectedDifficulty = await SelectUnlockedGuideEntry(selected.Entry, selected.Difficulty);
        if (selectedDifficulty != selected.Difficulty)
            throw new InvalidOperationException("开始挑战前入口难度发生变化，停止使用旧的树脂预算");
        _guideLevelKey = selected.Key;
        _guideActivePlan = _guidePlans[selected.Key];
    }

    private sealed record GuideEntryClick(string Text, double CenterY, int Width, int Height, int Index);

    // 列表到底后按同一家族逐级查找；不能把相邻的其他家族入口当作低一级。
    private async Task<int> SelectUnlockedGuideEntry(TrainingGuideEntry entry, int difficulty)
    {
        for (; difficulty >= 1; difficulty--)
        {
            var clicked = await ClickGuideEntry(entry, difficulty);
            var verificationStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            bool? unlocked = null;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var captureStartedAt = DateTime.Now;
                var elapsedBeforeCapture = System.Diagnostics.Stopwatch.GetElapsedTime(verificationStarted).TotalMilliseconds;
                using var screen = CaptureToRectArea();
                var titles = screen.FindMulti(RecognitionObject.Ocr(0, screen.Height * .12, screen.Width * .47, screen.Height * .84));
                bool selectionConfirmed;
                bool layoutStable;
                var stableAnchors = 0;
                var titleDetails = string.Empty;
                try
                {
                    var entryRows = titles.Where(r => r.Text.Contains("秘境")).Select(r => new
                    {
                        Row = r,
                        NameMatches = TrainingGuideEntryCatalog.Find(entry.Domain, r.Text) == entry,
                        Difficulty = TrainingGuideDropExpectations.ReadDifficulty(r.Text),
                        HighlightRatio = TrainingGuideEntrySelection.HighlightRatio(screen.SrcMat, r.Y, r.Height)
                    }).ToArray();
                    var selectedRows = entryRows.Where(r => r.HighlightRatio >= TrainingGuideEntrySelection.MinimumHighlightRatio).ToArray();
                    var layout = _guideEntryLayout!;
                    var namedRows = entryRows.Select(r => new { Value = r,
                        Entry = TrainingGuideEntryCatalog.Find(entry.Domain, r.Row.Text) })
                        .Where(r => r.Entry != null).Select(r => new TrainingGuideEntryLayout.Row(
                            r.Entry!, r.Value.Row.Y + r.Value.Row.Height / 2d)).ToArray();
                    layoutStable = screen.Width == clicked.Width && screen.Height == clicked.Height &&
                        layout.TryUpdate(namedRows);
                    stableAnchors = layoutStable ? layout.MatchedRows : 0;
                    selectionConfirmed = selectedRows.Length == 1 && selectedRows[0].NameMatches && layoutStable &&
                        layout.IsTarget(new TrainingGuideEntryLayout.Row(entry,
                            selectedRows[0].Row.Y + selectedRows[0].Row.Height / 2d), clicked.Index);
                    titleDetails = string.Join(" | ", entryRows.Select(r =>
                            $"原文=[{r.Row.Text}]，位置=({r.Row.X},{r.Row.Y},{r.Row.Width},{r.Row.Height})，名称匹配={r.NameMatches}，解析难度={r.Difficulty}，高亮比例={r.HighlightRatio:F4}"));
                }
                finally { foreach (var title in titles) title.Dispose(); }
                using var button = screen.Find(RecognitionObject.Ocr(screen.Width * .48, screen.Height * .80, screen.Width * .5, screen.Height * .18));
                var text = TrainingGuideMaterialCatalog.Normalize(button.Text);
                if (selectionConfirmed)
                {
                    if (text.Contains("冒险等阶") && text.Contains("解锁")) unlocked = false;
                    else if (text.Contains(singlePlayerChallengeString)) unlocked = true;
                }
                if (TrainingGuideDiagnostics.Enabled)
                {
                    var captureId = $"entry-check-{captureStartedAt:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}";
                    var message = $"目标={entry.Domain}/{entry.Entry}；目标难度={difficulty}；验证轮次={attempt + 1}/3；截图开始={captureStartedAt:O}；点击方法返回后耗时={elapsedBeforeCapture:F0}ms（方法内已等待700ms）；画面={screen.Width}x{screen.Height}；左侧OCR区域=(0%,12%,47%,84%)；入口行=[{titleDetails}]；点击前原文=[{clicked.Text}]；点击前中心Y={clicked.CenterY:F1}；匹配行数={stableAnchors}；整体位移={_guideEntryLayout!.LastShift:F1}；底部起行号={clicked.Index}；列表位置稳定={layoutStable}；确认难度依据=底部IV分组及行序；高亮阈值={TrainingGuideEntrySelection.MinimumHighlightRatio:F2}；目标行选中确认={selectionConfirmed}；按钮OCR区域=(48%,80%,50%,18%)；按钮原文=[{button.Text}]；按钮标准化=[{text}]；包含冒险等阶={text.Contains("冒险等阶")}；包含解锁={text.Contains("解锁")}；挑战匹配词=[{singlePlayerChallengeString}]；包含挑战={text.Contains(singlePlayerChallengeString)}；结果={(unlocked == true ? "已解锁" : unlocked == false ? "未解锁" : "未确认")}";
                    TrainingGuideDiagnostics.LogEntryVerification(captureId, message);
                    TrainingGuideDiagnostics.Save(screen.SrcMat, "screen", captureId);
                }
                if (unlocked != null) break;
                if (!layoutStable && attempt < 2)
                    Logger.LogWarning("培养入口排列暂未确认：{Entry} 难度 {Difficulty}，等待稳定后复查，不降级或重复点击",
                        entry.Entry, difficulty);
                await Delay(400, _ct);
            }
            if (unlocked == true) return difficulty;
            if (unlocked == null) throw new InvalidOperationException($"{entry.Entry}：未确认选中难度或挑战按钮，不能将识别失败当作未解锁");
            _guideDifficultyCaps[entry.IsWeapon] = difficulty - 1;
            Logger.LogInformation("培养计划：{Entry} 难度 {Difficulty} 未解锁，同类材料后续从低一级检查", entry.Entry, difficulty);
        }
        throw new InvalidOperationException($"{entry.Entry}：所有难度均未解锁，停止培养规划");
    }

    private async Task<GuideEntryClick> ClickGuideEntry(TrainingGuideEntry entry, int difficulty)
    {
        var layout = _guideEntryLayout ?? throw new InvalidOperationException("尚未建立入口分组");
        var targetIndex = layout.Index(entry, difficulty);
        var failedFrames = 0;
        var missingTargetFrames = 0;
        var afterUpwardScroll = false;
        for (var page = 0; page < 80; page++)
        {
            using var screen = CaptureToRectArea();
            var rows = screen.FindMulti(RecognitionObject.Ocr(0, screen.Height * .12, screen.Width * .47, screen.Height * .84));
            var direction = 0;
            try
            {
                var namedRows = rows.Select(r => new { Region = r, Entry = TrainingGuideEntryCatalog.Find(entry.Domain, r.Text) })
                    .Where(r => r.Entry != null).ToArray();
                if (!layout.TryUpdate(namedRows.Select(r => new TrainingGuideEntryLayout.Row(
                        r.Entry!, r.Region.Y + r.Region.Height / 2d)).ToArray(), afterUpwardScroll))
                {
                    if (++failedFrames >= 3)
                        throw new InvalidOperationException("入口排列或位移不明确，停止以避免误选难度");
                }
                else
                {
                    failedFrames = 0;
                    afterUpwardScroll = false;
                    var target = namedRows.FirstOrDefault(r => layout.IsTarget(
                        new TrainingGuideEntryLayout.Row(r.Entry!, r.Region.Y + r.Region.Height / 2d), targetIndex));
                    if (target != null && target.Region.Y >= screen.Height * .14 &&
                        target.Region.Y + target.Region.Height < screen.Height * .95)
                    {
                        var clicked = new GuideEntryClick(target.Region.Text,
                            target.Region.Y + target.Region.Height / 2d, screen.Width, screen.Height, targetIndex);
                        TrainingGuideDiagnostics.Detail("培养入口顺序定位：{Entry} 难度 {Difficulty}，底部起行号 {Index}，中心Y {Y:F1}，整体位移 {Shift:F1}",
                            entry.Entry, difficulty, targetIndex, clicked.CenterY, layout.LastShift);
                        target.Region.Click();
                        await Delay(700, _ct);
                        return clicked;
                    }
                    var y = layout.Y(targetIndex);
                    // 可见范围内漏读时原地重试，不能通过滚动猜测难度。
                    if (y < screen.Height * .17) direction = 1;
                    else if (y > screen.Height * .92) direction = -1;
                    else if (++missingTargetFrames >= 3)
                        throw new InvalidOperationException($"{entry.Entry} 难度 {difficulty}：目标位置可见，但原地复查三次仍未确认可点击入口，停止查找");
                }
            }
            finally { foreach (var row in rows) row.Dispose(); }
            if (direction != 0)
            {
                missingTargetFrames = 0;
                BetterGenshinImpact.Core.Script.Dependence.GlobalMethod.MoveMouseTo((int)(screen.Width * .25), (int)(screen.Height * .5));
                // 普通开放的入口同名，每次只滚一格，保持小幅位移跟踪；全开模式可按名称周期对齐。
                var scrollCount = direction > 0 && layout.Group.Length > 1 ? 5 : 1;
                for (var step = 0; step < scrollCount; step++)
                {
                    InputHub.Foreground.Mouse.VerticalScroll(direction);
                    await Delay(60, _ct);
                }
                afterUpwardScroll = direction > 0 && scrollCount > 1;
                TrainingGuideDiagnostics.Detail("培养入口查找：滚动方向 {Direction}，本次 {Count} 格，等待后重新确认入口排列",
                    direction > 0 ? "向上" : "向下", scrollCount);
            }
            await Delay(500, _ct);
        }
        throw new InvalidOperationException($"未找到 {entry.Entry} 难度 {difficulty}，停止以避免选择其他材料家族");
    }

    private async Task<List<TrainingGuideMaterialReading>> ReadEntryMaterials(TrainingGuideEntry entry, int difficulty)
    {
        var level = entry.Entry;
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
            TrainingGuideDiagnostics.Detail("秘境材料图标区域定位：画面 {Width}x{Height}，标题 ({X},{Y},{W},{H})，裁剪 {Band}",
                capture.Width, capture.Height, label.X, label.Y, label.Width, label.Height, band);
        }
        finally { foreach (var text in texts) text.Dispose(); }
        using var strip = new Mat(capture.SrcMat, band);
        var recognitionId = $"icons-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}";
        var locationDetails = new System.Text.StringBuilder();
        Action<string>? logLocation = !TrainingGuideDiagnostics.Enabled ? null : message => locationDetails.AppendLine(message);
        Action<Mat, string>? saveLocation = !TrainingGuideDiagnostics.Enabled ? null :
            (image, stage) => TrainingGuideDiagnostics.Save(image, stage, recognitionId);
        if (TrainingGuideDiagnostics.Enabled)
        {
            locationDetails.AppendLine($"入口={level}；全屏={capture.Width}x{capture.Height}；奖励区域={band}；以下矩形/边界坐标均相对奖励区域");
            TrainingGuideDiagnostics.Save(strip, "strip", recognitionId);
        }
        IReadOnlyList<Rect> icons;
        try
        {
            icons = TrainingGuideEntryMaterialIconLocator.FindIcons(strip, capture.Height / 1080d, logLocation, saveLocation);
        }
        finally
        {
            if (TrainingGuideDiagnostics.Enabled)
                TrainingGuideDiagnostics.LogIconLocation(recognitionId, locationDetails.ToString());
        }
        if (icons.Count == 0)
        {
            TrainingGuideDiagnostics.Save(strip, "entry-material-icons", recognitionId);
            throw new InvalidOperationException("秘境入口未识别到奖励图标，已停止点击。" + TrainingGuideDiagnostics.TroubleshootingHint);
        }
        var materials = new List<TrainingGuideMaterialReading>();
        var identified = new List<(Rect Icon, TrainingGuideMaterial Material)>();
        // 整个入口共用模型会话，先确认材料所属家族，再打开任何材料弹窗。
        using (var recognizer = ItemIconRecognizerFactory.CreateConfigured())
        {
            // 前两项是固定通用奖励；第三项先核验好感经验，未匹配则正常识别材料。
            TrainingGuideDiagnostics.Detail("培养图标扫描 {Level}：跳过前两个通用奖励，从第三项开始核验", level);
            for (var index = 2; index < icons.Count; index++)
            {
                _ct.ThrowIfCancellationRequested();
                var rect = icons[index];
                using var crop = new Mat(strip, rect);
                using var bgr = new Mat();
                if (crop.Channels() == 4) Cv2.CvtColor(crop, bgr, ColorConversionCodes.BGRA2BGR);
                else crop.CopyTo(bgr);
                using var normalized = new Mat();
                Cv2.Resize(bgr, normalized, new Size(125, 125), 0, 0, InterpolationFlags.Cubic);
                if (index == 2 && TrainingGuideFriendshipIcon.Matches(normalized, Logger, recognitionId, level))
                {
                    if (TrainingGuideDiagnostics.Enabled)
                        TrainingGuideDiagnostics.Save(normalized, "friendship-3", recognitionId);
                    continue;
                }
                var name = recognizer.Recognize(normalized);
                var material = name == null ? null : TrainingGuideMaterialCatalog.Find(name);
                if (TrainingGuideDiagnostics.Enabled)
                    TrainingGuideDiagnostics.Save(normalized, $"material-{index + 1}", recognitionId);
                TrainingGuideDiagnostics.LogIconMatch(name,
                    recognitionId, level, index + 1);
                TrainingGuideDiagnostics.Detail("培养图标识别 {Level}，位置 {Index}：{Name}", level, index + 1, name ?? "未识别");
                if (material != null)
                {
                    identified.Add((rect, material));
                    continue;
                }
                // 未识别和非培养奖励仅记录识别结果；遗漏培养材料由下方等级完整性校验拦截。
                TrainingGuideDiagnostics.Detail("培养图标扫描 {Level}，位置 {Index}：未确认为培养材料，不计入需求，继续扫描", level, index + 1);
            }
        }
        // 奖励按等级从高到低排列，但低难度无需展示家族的全部等级。
        var tiersByPosition = identified.OrderBy(x => x.Icon.X).Select(x => x.Material.Tier).ToArray();
        var tiersInOrder = tiersByPosition.SequenceEqual(tiersByPosition.OrderByDescending(tier => tier));
        var family = identified.FirstOrDefault().Material;
        // 跳过的图标可能包含识别失败的材料，必须确认该难度会掉落的全部等级。
        var expectedTiers = family == null ? Array.Empty<int>() :
            TrainingGuideDropExpectations.Per20(family.IsWeapon, difficulty)
                .Select((drop, tier) => (drop, tier)).Where(x => x.drop > 0).Select(x => x.tier).ToArray();
        var tiersComplete = tiersByPosition.OrderBy(tier => tier).SequenceEqual(expectedTiers);
        if (family == null ||
            identified.Any(x => x.Material.Family != family.Family || x.Material.IsWeapon != family.IsWeapon) ||
            identified.Select(x => x.Material.Tier).Distinct().Count() != identified.Count ||
            !tiersInOrder ||
            !tiersComplete ||
            (family.Family != entry.Family || family.IsWeapon != entry.IsWeapon))
        {
            TrainingGuideDiagnostics.Save(capture.SrcMat, "capture", recognitionId);
            TrainingGuideDiagnostics.Save(strip, "strip", recognitionId);
            TrainingGuideDiagnostics.AppendOcrIssue(recognitionId,
                $"入口={level}；难度={difficulty}；图标数量={icons.Count}；已识别={string.Join("、", identified.Select(x => x.Material.Name))}；横向等级={string.Join(",", tiersByPosition)}；预期等级={string.Join(",", expectedTiers)}；结果=家族不一致/等级缺失或顺序异常/与入口冲突");
            throw new InvalidOperationException($"{level}：图标识别未确认材料家族或等级顺序，停止本入口扫描");
        }
        // 图标家族确认入口后再筛选，非目标入口不读取弹窗。
        if (_guideCustomTargets != null &&
            !_guideCustomTargets.Keys.Any(m => m.Family == entry.Family && m.IsWeapon == entry.IsWeapon)) return materials;
        foreach (var (icon, expected) in identified)
        {
            capture.ClickTo(band.X + icon.X + icon.Width / 2, band.Y + icon.Y + icon.Height / 2);
            await Delay(600, _ct);
            TrainingGuideMaterialReading? reading;
            try
            {
                reading = await new TrainingGuidePopupRecognizer(Logger, _ct).ReadStable(expected, entry);
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
                throw new InvalidOperationException("秘境材料库存识别失败，停止以避免错误刷取。" + TrainingGuideDiagnostics.TroubleshootingHint);
            }
            if (_guideCustomTargets != null)
            {
                var required = _guideCustomTargets.GetValueOrDefault(reading.Material);
                reading = reading with { Required = required, IsTarget = required > 0 };
            }
            materials.Add(reading);
        }
        if (!materials.Any(m => m.IsTarget))
            Logger.LogInformation("培养计划：当前难度展示的材料没有培养需求，不推算未展示的高级材料需求");
        if (materials.Select(m => m.Material.Family).Distinct().Count() != 1)
            throw new InvalidOperationException("奖励材料未能归入同一材料家族，停止规划");
        return materials;
    }

    private void ReportGuidePlan(string level, TrainingGuideFamilyPlan plan, int remaining)
    {
        var materials = string.Join("、", plan.Materials.Select(m =>
            $"{m.Material.Name} {m.Stock}/{(m.IsTarget ? m.Required.ToString() : "-")}"));
        var inventoryLabel = !_taskParam.TrainingGuideRewardRecognitionEnabled || plan.HasMissingRewards
            ? "库存快照/目标（预算模式，不代表当前库存）" : "库存/目标";
        Logger.LogInformation("培养材料（{InventoryLabel}）{Level}，难度 {Difficulty}：{Materials}；仅处理本入口已识别的材料", inventoryLabel, level, plan.Difficulty, materials);
        var allocation = DescribeGuideResinAllocation(remaining);
        Logger.LogInformation("培养树脂规划 {Level}：当前进度 {Spent}/{Total}体；{Allocation}；合成预留 {Reserve}%",
            level, plan.KnownResinSpent, (long)plan.KnownResinSpent + remaining, allocation, GuideReservePercent);
        UIDispatcherHelper.BeginInvoke(() => Toast.Information($"培养材料（{inventoryLabel}）\n{level}\n{materials}"));
        UIDispatcherHelper.BeginInvoke(() => Toast.Information($"培养树脂规划\n当前进度 {plan.KnownResinSpent}/{(long)plan.KnownResinSpent + remaining}体；{allocation}；合成预留 {GuideReservePercent}%"));
    }

    private static int SelectGuideResinAmount(int remaining, int condensed, int original) =>
        remaining <= 0 ? 0 : condensed > 0 && (remaining >= 60 || original < 20) ? 60 :
        original >= 40 && remaining >= 40 ? 40 : original >= 20 ? 20 : 0;

    private string DescribeGuideResinAllocation(int remaining)
    {
        if (remaining <= 0) return "当前入口计划执行完毕";
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
        var remaining = _guideActivePlan == null ? null : RemainingGuideResin(_guideActivePlan);
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

    private int? RemainingGuideResin(TrainingGuideFamilyPlan plan)
    {
        // 首次入口预读即固定原预算，不能等漏记奖励后用已变化的库存倒推。
        var budgetRemaining = plan.RemainingInitialResin(GuideReservePercent);
        return !_taskParam.TrainingGuideRewardRecognitionEnabled || plan.HasMissingRewards
            ? budgetRemaining : plan.RemainingResin(GuideReservePercent);
    }

    private bool UpdateGuideAfterReward(bool resinExhausted)
    {
        if (!_guidePlanning || _guideActivePlan == null) return false;
        _guideRounds++;
        var useRewards = _taskParam.TrainingGuideRewardRecognitionEnabled;
        if (_guideRoundResin <= 0)
            throw new InvalidOperationException("培养计划：本轮树脂消耗未确认，无法可靠扣减预算，停止任务");
        if (!useRewards || _guideActivePlan.HasMissingRewards)
        {
            // 预算模式只记消耗；奖励仍由主流程汇总，不再维护不完整的计划库存和掉落样本。
            _guideActivePlan.RecordResinSpent(_guideRoundResin);
        }
        else if (!_guideActivePlan.ApplyRewards(_guideRoundRewards, _guideRoundResin))
        {
            if (!_taskParam.TrainingGuideRewardFailureBudgetEnabled)
                throw new InvalidOperationException("培养计划：奖励识别失败，未开启固定预算兜底，结束任务");
            _guideActivePlan.MarkRewardsMissing();
            Logger.LogError("培养计划：本轮奖励未可靠识别，本入口转为原预算保守刷取，不再依据后续奖励恢复动态估算");
        }
        var remaining = RemainingGuideResin(_guideActivePlan);
        if (remaining == null)
            throw new InvalidOperationException("培养计划：剩余树脂预算无法确认，停止任务");
        if (!useRewards || _guideActivePlan.HasMissingRewards)
            Logger.LogInformation("培养计划：按初始预算执行，本轮消耗 {Spent}体，剩余预计 {Remaining}体", _guideRoundResin, remaining);
        if (remaining == 0 && _guideLevelKey != null) _guideProcessedLevels.Add(_guideLevelKey);
        ReportGuidePlan(_guideLevelKey ?? "当前关卡", _guideActivePlan, remaining.Value);
        if (remaining == 0)
        {
            if (!useRewards || _guideActivePlan.HasMissingRewards)
                Logger.LogWarning("培养计划：本入口预规划预算执行完毕，实际材料需求未核实；后续入口使用各自已读取的库存计划");
            else
                Logger.LogInformation("培养计划：本入口按可靠库存及奖励核算，当前难度可处理的需求已满足");
        }
        var limitReached = _guideRounds >= _taskParam.DomainRoundNum;
        var change = remaining == 0;
        _guideNextAction = resinExhausted || limitReached ? GuideNextAction.Stop :
            remaining == 0 ? GuideNextAction.AdvancePlan : GuideNextAction.Continue;
        return change || limitReached;
    }
}

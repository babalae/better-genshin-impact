using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoDomain;

// 低世界等级适配：识别入口布局、校验选中与解锁状态，并选择可用难度。
public partial class AutoDomainTask
{
    private TrainingGuideEntryLayout? _guideEntryLayout;
    private readonly Dictionary<bool, int> _guideDifficultyCaps = new();

    // 识别入口布局，为按材料家族选择可用难度提供行号。
    private async Task<TrainingGuideEntry[]> PrepareGuideEntriesForLowAR(bool allOpen)
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
        return candidates;
    }

    private sealed record GuideEntryClick(string Text, double CenterY, int Width, int Height, int Index);

    // 列表到底后按同一家族逐级查找；不能把相邻的其他家族入口当作低一级。
    private async Task<int> SelectGuideEntryForLowAR(TrainingGuideEntry entry, int difficulty)
    {
        for (; difficulty >= 1; difficulty--)
        {
            var clicked = await ClickGuideEntryForLowAR(entry, difficulty);
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
                    Logger.LogWarning("培养入口排列暂未确认：{Entry} 难度 {Difficulty}，等待稳定后复查",
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

    private async Task<GuideEntryClick> ClickGuideEntryForLowAR(TrainingGuideEntry entry, int difficulty)
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

    /// <summary>从底部 IV 组建立行号；普通开放按小幅位移跟踪，全开时按入口名称周期对齐。</summary>
    private sealed class TrainingGuideEntryLayout
    {
        internal sealed record Row(TrainingGuideEntry Entry, double Y);
        public TrainingGuideEntry[] Group { get; }
        public double Pitch { get; }
        public double BottomY { get; private set; }
        public double LastShift { get; private set; }
        public int MatchedRows { get; private set; }
        private readonly double _tolerance;

        public TrainingGuideEntryLayout(Row[] rows, int groupSize, double scale)
        {
            rows = rows.OrderBy(r => r.Y).ToArray();
            if (rows.Length < Math.Max(3, groupSize))
                throw new InvalidOperationException("底部入口不足，无法建立难度分组");
            _tolerance = 12 * scale;
            var gaps = rows.Zip(rows.Skip(1), (a, b) => b.Y - a.Y)
                .Where(g => g >= 90 * scale && g <= 130 * scale).OrderBy(g => g).ToArray();
            if (gaps.Length < 2) throw new InvalidOperationException("入口行距未确认，停止建立难度分组");
            Pitch = gaps[gaps.Length / 2];
            Group = rows.TakeLast(groupSize).Select(r => r.Entry).ToArray();
            if (Group.Distinct().Count() != groupSize)
                throw new InvalidOperationException("底部入口组名称重复，停止建立难度分组");
            BottomY = rows[^1].Y;
            if (!TryUpdate(rows)) throw new InvalidOperationException("底部入口排列不符合难度分组，停止规划");
        }

        public int Index(TrainingGuideEntry entry, int difficulty)
        {
            var member = Array.IndexOf(Group, entry);
            if (member < 0 || difficulty is < 1 or > 4)
                throw new InvalidOperationException("目标不属于当前入口分组");
            return (4 - difficulty) * Group.Length + Group.Length - 1 - member;
        }

        public double Y(int index) => BottomY - index * Pitch;

        public bool IsTarget(Row row, int index) =>
            row.Entry == Expected(index) && Math.Abs(row.Y - Y(index)) <= _tolerance;

        private TrainingGuideEntry Expected(int index) => Group[Group.Length - 1 - index % Group.Length];

        public bool TryUpdate(IReadOnlyList<Row> rows, bool afterUpwardScroll = false)
        {
            if (afterUpwardScroll && Group.Length > 1)
                return TryUpdateAfterUpwardScroll(rows);
            // 位移限制在半行以内，防止普通开放时同名的邻级被当成原行。
            // 调用方每次只滚一格，并在下一次滚动前重新截图确认。
            var shifts = new List<double>();
            foreach (var row in rows)
            {
                var index = (int)Math.Round((BottomY - row.Y) / Pitch);
                if (index < 0 || index >= Group.Length * 4 || row.Entry != Expected(index)) return false;
                var shift = row.Y - Y(index);
                if (Math.Abs(shift) >= Pitch * .45) return false;
                shifts.Add(shift);
            }
            if (shifts.Count < 2) return false;
            shifts.Sort();
            var movement = shifts[shifts.Count / 2];
            if (shifts.Any(s => Math.Abs(s - movement) > _tolerance)) return false;
            var indices = rows.Select(r => (int)Math.Round((BottomY + movement - r.Y) / Pitch)).ToArray();
            if (indices.Distinct().Count() != indices.Length) return false;
            BottomY += movement;
            LastShift = movement;
            MatchedRows = rows.Count;
            return true;
        }

        private bool TryUpdateAfterUpwardScroll(IReadOnlyList<Row> rows)
        {
            if (rows.Count < 2) return false;
            // 向上五格会跨行；按三种名称的周期对齐，允许正向移动但不跨完整同名周期。
            // 多个对齐结果或超过范围时拒绝，不能将五格直接当作五行。
            var candidates = new List<double>();
            for (var index = 0; index < Group.Length * 4; index++)
            {
                if (rows[0].Entry != Expected(index)) continue;
                var shift = rows[0].Y - Y(index);
                if (shift < -_tolerance || shift > Pitch * (Group.Length - .5)) continue;
                var indices = rows.Select(r => (int)Math.Round((BottomY + shift - r.Y) / Pitch)).ToArray();
                if (indices.Distinct().Count() != rows.Count) continue;
                if (rows.Where((r, i) => indices[i] < 0 || indices[i] >= Group.Length * 4 ||
                        r.Entry != Expected(indices[i]) ||
                        Math.Abs(r.Y - (Y(indices[i]) + shift)) > _tolerance).Any()) continue;
                var shifts = rows.Select((r, i) => r.Y - Y(indices[i])).OrderBy(s => s).ToArray();
                candidates.Add(shifts[shifts.Length / 2]);
            }
            if (candidates.Count != 1) return false;
            LastShift = candidates[0];
            BottomY += LastShift;
            MatchedRows = rows.Count;
            return true;
        }
    }

    /// <summary>检查左侧入口标题所在行的浅色选中背景；不使用锁图标判断选中。</summary>
    private static class TrainingGuideEntrySelection
    {
        public const double MinimumHighlightRatio = .8;

        public static double HighlightRatio(Mat screen, int titleY, int titleHeight)
        {
            // 取列表右侧、与标题同高的空白区域，避开文字、锁和左侧悬浮日志。
            // 必须由当前截图的 OCR 提供位置，不能复用滚动前的坐标。
            var left = (int)(screen.Width * .32);
            var right = (int)(screen.Width * .43);
            if (titleHeight <= 0 || titleY < screen.Height * .12 ||
                titleY + titleHeight > screen.Height * .96 || right <= left)
                return 0;

            using var region = new Mat(screen, new Rect(left, titleY, right - left, titleHeight));
            using var bgr = new Mat();
            if (region.Channels() == 4) Cv2.CvtColor(region, bgr, ColorConversionCodes.BGRA2BGR);
            else if (region.Channels() == 3) region.CopyTo(bgr);
            else return 0;

            var highlighted = 0;
            for (var y = 0; y < bgr.Height; y++)
            for (var x = 0; x < bgr.Width; x++)
            {
                var pixel = bgr.At<Vec3b>(y, x);
                var minimum = Math.Min(pixel.Item0, Math.Min(pixel.Item1, pixel.Item2));
                var maximum = Math.Max(pixel.Item0, Math.Max(pixel.Item1, pixel.Item2));
                // 米白色高亮；普通深蓝底和黄色悬停边框不能满足大面积浅色条件。
                if (minimum >= 170 && maximum - minimum <= 75 && pixel.Item2 + 10 >= pixel.Item0)
                    highlighted++;
            }
            return highlighted / (double)(bgr.Width * bgr.Height);
        }
    }
}

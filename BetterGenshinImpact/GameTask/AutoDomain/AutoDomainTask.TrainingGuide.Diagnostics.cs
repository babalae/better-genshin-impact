using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;
using BetterGenshinImpact.GameTask.Common.Job;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoDomain;

public partial class AutoDomainTask
{
    private string _guideScanId = string.Empty;
    private int _guideScanSucceeded;
    private int _guideScanFailed;
    private int _guideScanEntryFailed;
    private HashSet<TrainingGuideEntry>? _guideScanEntries;
    private readonly HashSet<TrainingGuideEntry> _guideScanVisitedEntries = new();
    private readonly HashSet<TrainingGuideEntry> _guideScanCompletedEntries = new();
    private readonly HashSet<TrainingGuideMaterial> _guideScanReadMaterials = new();
    private readonly HashSet<TrainingGuideMaterial> _guideScanFailedMaterials = new();
    private readonly HashSet<string> _guideScanWholeDomains = new();

    private void MarkGuideScanAttempt(TrainingGuideEntry? entry)
    {
        if (entry != null) _guideScanVisitedEntries.Add(entry);
    }

    private HashSet<TrainingGuideEntry> ResolveGuideScanEntries()
    {
        static string[] Split(string text) => (text ?? string.Empty).Split(new[] { ';', '；' },
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        static string Normalize(string text) => TrainingGuideMaterialCatalog.Normalize(text)
            .Replace("「", "").Replace("」", "").Replace("\"", "").Replace("净言", "诤言");

        var domains = Split(_taskParam.TrainingGuideOcrScanDomains);
        var materials = Split(_taskParam.TrainingGuideOcrScanMaterials);
        _guideScanWholeDomains.Clear();
        if (domains.Length == 0 && materials.Length == 0)
        {
            _guideScanWholeDomains.UnionWith(TrainingGuideEntryCatalog.Entries.Select(e => e.Domain));
            return TrainingGuideEntryCatalog.Entries.ToHashSet();
        }

        var selected = new HashSet<TrainingGuideEntry>();
        var invalid = new List<string>();
        foreach (var domain in domains)
        {
            var matches = TrainingGuideEntryCatalog.Entries.Where(e => Normalize(e.Domain) == Normalize(domain)).ToArray();
            if (matches.Length == 0) invalid.Add($"秘境：{domain}");
            selected.UnionWith(matches);
            _guideScanWholeDomains.UnionWith(matches.Select(e => e.Domain));
        }
        foreach (var name in materials)
        {
            var families = TrainingGuideMaterialCatalog.Materials.Where(m => !m.IsWeapon &&
                (Normalize(m.Name) == Normalize(name) || Normalize(m.Family) == Normalize(name)))
                .Select(m => m.Family).Distinct().ToArray();
            var matches = TrainingGuideEntryCatalog.Entries.Where(e => !e.IsWeapon && families.Contains(e.Family)).ToArray();
            if (matches.Length != 1) invalid.Add($"天赋素材：{name}");
            else selected.Add(matches[0]);
        }
        if (invalid.Count > 0)
            throw new InvalidOperationException($"OCR 测试筛选名称无效，请检查后重新启动：{string.Join("；", invalid)}");
        return selected;
    }

    private bool ShouldScanGuideEntry(string level)
    {
        var entry = TrainingGuideEntryCatalog.Find(_guideDomainName ?? string.Empty, level);
        if (entry != null && _guideScanEntries?.Contains(entry) == true)
        {
            return true;
        }
        // 完整秘境扫描允许入口文字未匹配，由完整图标家族补全入口身份。
        return _guideDomainName != null && _guideScanWholeDomains.Contains(_guideDomainName);
    }

    private void RecordGuideScan(string message)
    {
        Logger.LogInformation("培养 OCR 遍历：{Message}", message);
        TrainingGuideDiagnostics.AppendScan(Logger, _guideScanId, message);
    }

    private async Task RunTrainingGuideOcrScan()
    {
        // 先验证所有输入，避免拼写错误意外触发完整遍历或部分传送。
        var selectedEntries = ResolveGuideScanEntries();
        _guideScanId = $"scan-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}";
        _guideScanSucceeded = _guideScanFailed = _guideScanEntryFailed = 0;
        var originalDomain = _taskParam.DomainName;
        var originalGuideDomain = _guideDomainName;
        var originalPlanning = _guidePlanning;
        _guidePlanning = false;
        _guideScanEntries = selectedEntries;
        _guideScanVisitedEntries.Clear();
        _guideScanCompletedEntries.Clear();
        _guideScanReadMaterials.Clear();
        _guideScanFailedMaterials.Clear();
        var domains = TrainingGuideEntryCatalog.Entries.Where(selectedEntries.Contains).Select(e => e.Domain).Distinct().ToArray();
        var failedDomains = 0;
        var attemptedDomains = 0;
        RecordGuideScan($"开始扫描 {domains.Length} 个已知材料秘境；不战斗、不领奖；未解锁或无法到达的秘境记录后跳过");
        try
        {
            RecordGuideScan($"测试入口：{string.Join("；", selectedEntries.Select(e => $"{e.Domain}/{e.Entry}"))}");
            foreach (var domain in domains)
            {
                _ct.ThrowIfCancellationRequested();
                attemptedDomains++;
                _taskParam.DomainName = domain;
                _guideDomainName = domain;
                RecordGuideScan($"开始 {attemptedDomains}/{domains.Length}：{domain}");
                try
                {
                    await new ReturnMainUiTask().Start(_ct);
                    await TpDomain();
                    // 复用正常任务的接近、交互及全开检测；扫描分支在点击挑战前返回。
                    await EnterDomain(scanOnly: true);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e)
                {
                    failedDomains++;
                    RecordGuideScan($"{domain}：未完成扫描，{e.Message}");
                }
            }
            await new ReturnMainUiTask().Start(_ct);
            RecordGuideScan("遍历结束；非全开日期的未开放入口需要在全开时补测");
        }
        finally
        {
            foreach (var entry in selectedEntries.Except(_guideScanCompletedEntries))
                RecordGuideScan($"未完成目标：{entry.Domain}/{entry.Entry}；{(_guideScanVisitedEntries.Contains(entry) ? "已尝试，材料未完整读取" : "未确认尝试，可能未开放、未解锁或入口识别失败")}");
            var expectedMaterials = TrainingGuideMaterialCatalog.Materials.Where(m =>
                selectedEntries.Any(e => e.Family == m.Family && e.IsWeapon == m.IsWeapon)).ToHashSet();
            var readCount = expectedMaterials.Count(_guideScanReadMaterials.Contains);
            var failedCount = expectedMaterials.Count(m => !_guideScanReadMaterials.Contains(m) && _guideScanFailedMaterials.Contains(m));
            RecordGuideScan($"目标覆盖：入口完整完成 {_guideScanCompletedEntries.Count(selectedEntries.Contains)}/{selectedEntries.Count}；预期材料 {expectedMaterials.Count}，已读取 {readCount}，数量识别失败 {failedCount}，未取得读取结果 {expectedMaterials.Count - readCount - failedCount}（含入口失败、未开放及取消）");
            RecordGuideScan($"扫描汇总：已尝试秘境 {attemptedDomains}/{domains.Length}，秘境异常 {failedDomains}，入口异常 {_guideScanEntryFailed}；材料读取成功次数 {_guideScanSucceeded}，数量识别失败次数 {_guideScanFailed}；取消={_ct.IsCancellationRequested}");
            _taskParam.DomainName = originalDomain;
            _guideDomainName = originalGuideDomain;
            _guidePlanning = originalPlanning;
            _guideScanEntries = null;
            _guideScanWholeDomains.Clear();
            _guideScanVisitedEntries.Clear();
            _guideScanCompletedEntries.Clear();
            _guideScanReadMaterials.Clear();
            _guideScanFailedMaterials.Clear();
        }
    }
}

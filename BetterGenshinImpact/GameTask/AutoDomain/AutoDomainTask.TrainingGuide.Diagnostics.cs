using System;
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

    private void RecordGuideScan(string message)
    {
        Logger.LogInformation("培养 OCR 遍历：{Message}", message);
        TrainingGuideDiagnostics.AppendScan(Logger, _guideScanId, message);
    }

    private async Task RunTrainingGuideOcrScan()
    {
        _guideScanId = $"scan-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}";
        _guideScanSucceeded = _guideScanFailed = _guideScanEntryFailed = 0;
        var originalDomain = _taskParam.DomainName;
        var originalGuideDomain = _guideDomainName;
        var originalPlanning = _guidePlanning;
        _guidePlanning = false;
        var domains = TrainingGuideEntryCatalog.Entries.Select(e => e.Domain).Distinct().ToArray();
        var failedDomains = 0;
        var attemptedDomains = 0;
        RecordGuideScan($"开始扫描 {domains.Length} 个已知材料秘境；不战斗、不领奖；未解锁或无法到达的秘境记录后跳过");
        try
        {
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
            RecordGuideScan($"扫描汇总：已尝试秘境 {attemptedDomains}/{domains.Length}，秘境异常 {failedDomains}，入口异常 {_guideScanEntryFailed}；材料读取成功 {_guideScanSucceeded}，失败 {_guideScanFailed}；取消={_ct.IsCancellationRequested}");
            _taskParam.DomainName = originalDomain;
            _guideDomainName = originalGuideDomain;
            _guidePlanning = originalPlanning;
        }
    }
}

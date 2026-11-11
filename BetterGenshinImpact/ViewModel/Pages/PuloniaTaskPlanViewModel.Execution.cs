using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// “任务计划”页面的运行提交；状态查看、取消与续跑移至共用执行记录视图模型。
/// </summary>
public partial class PuloniaTaskPlanViewModel
{
    /// <summary>
    /// 计划内与全局页面复用的执行记录模型，不依赖可编辑的计划内容。
    /// </summary>
    public PuloniaTaskHistoryViewModel History { get; }

    /// <summary>
    /// 打开运行预览：按当前配置逐项预测本次执行或因路线 CD 跳过；在预览中选择“开始执行”后提交运行。
    /// </summary>
    /// <param name="document">要预览的计划；为空时使用当前计划。</param>
    [RelayCommand(CanExecute = nameof(CanRunDocument))]
    private async Task PreviewRunAsync(PuloniaTaskPlanDocumentViewModel? document)
    {
        document ??= SelectedDocument;
        if (document is null || _settingsDialogs is null || !Session.CanRunDocument(document))
            return;
        var preview = new PuloniaTaskRunPreviewViewModel(Session, document);
        if (_settingsDialogs.ShowRunPreview(preview))
            await RunPlanAsync(document);
    }

    /// <summary>
    /// 当前计划是否可以提交运行。
    /// </summary>
    public bool CanRunPlan => Session.CanRunDocument(SelectedDocument);

    /// <summary>卡片命令检查自身目标，兼容未提供参数时执行当前计划的调用。</summary>
    private bool CanRunDocument(PuloniaTaskPlanDocumentViewModel? document)
        => Session.CanRunDocument(document ?? SelectedDocument);

    /// <summary>
    /// 计划头部“开始执行”旁的“运行于”：主实例且有在线子实例时显示，与新一条龙页共用；子实例不显示，运行提交到本实例。
    /// </summary>
    public PuloniaRunTargetsViewModel RunTargets { get; }

    /// <summary>
    /// 保存计划后按“运行于”在每个选中的实例上各提交一次运行，并在执行记录中定位新运行（优先本实例的运行）；
    /// 提交时固定快照，随后编辑不会影响本次运行。页面执行入口默认不限时；手动停止与节点自身的执行策略仍然生效。
    /// </summary>
    /// <param name="document">要运行的计划；为空时使用当前计划。</param>
    [RelayCommand(CanExecute = nameof(CanRunDocument))]
    private async Task RunPlanAsync(PuloniaTaskPlanDocumentViewModel? document)
    {
        document ??= SelectedDocument;
        if (document is null || !Session.CanRunDocument(document))
            return;
        if (document.Purpose == PuloniaTaskPlanPurpose.General) SelectedDocument = document;
        var results = await RunTargets.SubmitAsync(document);
        if ((results.FirstOrDefault(result => result is { IsLocal: true, IsSuccess: true })
             ?? results.FirstOrDefault(result => result.IsSuccess)) is { RunId: { } runId })
            await History.RevealRunAsync(runId);
    }
}

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>任务树展开状态的读取与延迟保存；展开或折叠不修改计划、不进入撤销，也不触发计划保存。</summary>
public partial class PuloniaPlanEditingSession
{
    /// <summary>等待写入展开状态的防抖延迟；连续展开或折叠只写最后一次。</summary>
    private CancellationTokenSource? _viewStateSaveDelay;

    /// <summary>读取展开状态；文件损坏或不可读时回退为全部展开，不阻止计划加载。</summary>
    private async Task<PuloniaTaskPlanViewState> LoadViewStateAsync()
    {
        try
        {
            return await _store.LoadPlanViewStateAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or PuloniaTaskValidationException or Newtonsoft.Json.JsonException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            return new PuloniaTaskPlanViewState();
        }
    }

    /// <summary>文档展开状态变化后安排写盘。</summary>
    private void OnDocumentViewStateChanged(object? sender, EventArgs e) => ScheduleViewStateSave();

    /// <summary>重新安排展开状态保存，只保留最后一次请求。</summary>
    private void ScheduleViewStateSave()
    {
        _viewStateSaveDelay?.Cancel();
        var cancellation = new CancellationTokenSource();
        _viewStateSaveDelay = cancellation;
        _ = SaveViewStateAfterDelayAsync(cancellation);
    }

    /// <summary>等待防抖时间后保存会话内全部计划的折叠节点；失败只影响下次打开时的展开显示。</summary>
    private async Task SaveViewStateAfterDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(AutoSaveDelayMilliseconds, cancellation.Token);
            var state = new PuloniaTaskPlanViewState();
            foreach (var document in AllDocuments.Where(document => !document.IsDeleted))
            {
                var collapsed = document.GetCollapsedNodeIds();
                if (collapsed.Count > 0)
                    state.CollapsedTaskIds[document.Id] = collapsed.ToList();
            }
            await _store.SavePlanViewStateAsync(state);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // 新的展开操作已重新安排保存。
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PuloniaTaskValidationException)
        {
            ReportStatus("任务的展开状态未能保存，下次打开时可能全部展开：" + ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_viewStateSaveDelay, cancellation))
                _viewStateSaveDelay = null;
            cancellation.Dispose();
        }
    }
}

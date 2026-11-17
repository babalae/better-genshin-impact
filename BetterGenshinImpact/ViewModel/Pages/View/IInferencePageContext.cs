using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;

namespace BetterGenshinImpact.ViewModel.Pages.View;

/// <summary>
///     后端设置子 ViewModel（ncnn）对宿主页面的交互面。
///     子 ViewModel 只依赖本接口而非 <c>HardwareAccelerationViewModel</c> 具体类型：
///     页面实现可替换、单元测试可用 fake，页面与子 ViewModel 的耦合收敛在这里。
/// </summary>
public interface IInferencePageContext
{
    /// <summary>页面当前的全局后端意图草稿（子 ViewModel 据此生成覆盖提示文案）。</summary>
    InferenceBackend SelectedBackendDraft { get; }

    /// <summary>页面当前选中的 ONNX 执行引擎卡片；null 表示尚未完成首次刷新。</summary>
    InferenceEngineCardViewModel? SelectedEngine { get; }

    /// <summary>「OCR 使用 CPU」开关草稿（影响用途覆盖提示的目标描述）。</summary>
    bool ForceCpuOcr { get; }

    /// <summary>以页面统一的操作语义执行一次后台操作（IsBusy / 进度区 / 取消按钮）。</summary>
    Task RunOperationAsync(Func<CancellationToken, Task> action);

    /// <summary>运行库下载确认弹窗（展示包清单与预计大小）。</summary>
    Task<bool> ConfirmDownloadAsync(IReadOnlyList<RuntimeDownloadPackage> packages);

    /// <summary>页面进度区状态更新。</summary>
    void ReportProgress(string message, double percentage, bool indeterminate);
}

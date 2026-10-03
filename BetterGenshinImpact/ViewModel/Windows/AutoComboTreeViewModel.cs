using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Windows;

/// <summary>
/// 自动连招行为树浮窗的数据源：建树任务（含中途 preview 刷新装饰器）与运行任务写入最新行为树 ASCII，
/// 浮窗通过 DataContext 绑定展示，INPC 属性变更通知自动推送到 UI（后台线程赋值亦由 WPF 绑定自动调度）
/// </summary>
public partial class AutoComboTreeViewModel : ObservableObject
{
    /// <summary>全局唯一实例：数据由建树/运行任务在后台线程写入，浮窗只读</summary>
    public static AutoComboTreeViewModel Instance { get; } = new();

    /// <summary>最新一次渲染的行为树 ASCII（Tick 路径或建树预览），无内容时为 null</summary>
    [ObservableProperty]
    private string? _latestTreeAscii;

    /// <summary>最新一次渲染的兜底攻击行为树 ASCII（建树预览或运行期 Tick 路径），无内容时为 null</summary>
    [ObservableProperty]
    private string? _latestFallbackTreeAscii;

    /// <summary>清空全部树预览</summary>
    public void Clear()
    {
        LatestTreeAscii = null;
        LatestFallbackTreeAscii = null;
    }
}

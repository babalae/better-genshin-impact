namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 新增叶子任务时可选择的任务类型。
/// </summary>
public sealed class PuloniaTaskTypeOption
{
    /// <summary>
    /// 持久化的稳定任务类型。
    /// </summary>
    public string TaskType { get; }

    /// <summary>
    /// 用户可见名称。
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// 建立一个任务类型选项。
    /// </summary>
    public PuloniaTaskTypeOption(string taskType, string displayName)
    {
        TaskType = taskType;
        DisplayName = displayName;
    }
}

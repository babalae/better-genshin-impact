namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// “运行于”逐个实例提交一次运行的结果。
/// </summary>
/// <param name="InstanceName">实例显示名</param>
/// <param name="IsLocal">是否在本实例本地提交</param>
/// <param name="RunId">新运行 ID；提交失败时为空</param>
/// <param name="Error">失败原因；提交成功时为空</param>
public sealed record PuloniaRunSubmitResult(string InstanceName, bool IsLocal, string? RunId, string? Error)
{
    /// <summary>是否已提交（本实例已入队，或子实例已接受）。</summary>
    public bool IsSuccess => RunId is not null;
}

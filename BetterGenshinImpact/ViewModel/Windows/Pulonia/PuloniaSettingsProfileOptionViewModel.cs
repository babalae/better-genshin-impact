namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>
/// 设置方案下拉框中的一项；ID 为空表示不使用方案。
/// </summary>
/// <param name="Id">方案 ID。</param>
/// <param name="DisplayName">显示文字。</param>
public sealed record PuloniaSettingsProfileOptionViewModel(string? Id, string DisplayName);

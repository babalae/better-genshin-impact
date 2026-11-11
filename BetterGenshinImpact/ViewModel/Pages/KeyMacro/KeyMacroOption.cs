namespace BetterGenshinImpact.ViewModel.Pages.KeyMacro;

/// <summary>
/// 下拉框选项：值和显示名称
/// </summary>
/// <typeparam name="T">选项值类型</typeparam>
/// <param name="Value">选项值</param>
/// <param name="Name">显示名称</param>
public sealed record KeyMacroOption<T>(T Value, string Name);

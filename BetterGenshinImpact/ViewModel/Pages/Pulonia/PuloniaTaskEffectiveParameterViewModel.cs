namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 展示一个有效参数的最终 JSON 值及其最近来源。
/// </summary>
public sealed class PuloniaTaskEffectiveParameterViewModel
{
    /// <summary>
    /// 参数名称。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 不丢失 JSON 类型的紧凑值文本。
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// 最后覆盖该参数的位置说明。
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// 建立一行有效参数预览。
    /// </summary>
    public PuloniaTaskEffectiveParameterViewModel(string name, string value, string source)
    {
        Name = name;
        Value = value;
        Source = source;
    }
}

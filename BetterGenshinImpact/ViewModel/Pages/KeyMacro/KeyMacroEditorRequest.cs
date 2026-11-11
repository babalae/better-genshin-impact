namespace BetterGenshinImpact.ViewModel.Pages.KeyMacro;

/// <summary>
/// 对脚本编辑器的请求类型
/// </summary>
public enum KeyMacroEditorRequestKind
{
    /// <summary>
    /// 在光标处插入文字
    /// </summary>
    Insert,

    /// <summary>
    /// 跳到指定位置并选中
    /// </summary>
    Jump
}

/// <summary>
/// ViewModel 对脚本编辑器的一次请求（插入文字、跳到出错位置）。编辑器收到新的请求对象后执行一次
/// </summary>
/// <param name="Id">请求序号，保证内容相同的连续请求也会被执行</param>
/// <param name="Kind">请求类型</param>
/// <param name="Text">要插入的文字</param>
/// <param name="Line">跳转的行号，从 1 开始</param>
/// <param name="Column">跳转的列，从 0 开始</param>
/// <param name="Length">选中的长度</param>
public sealed record KeyMacroEditorRequest(int Id, KeyMacroEditorRequestKind Kind, string Text = "", int Line = 0, int Column = 0, int Length = 0);

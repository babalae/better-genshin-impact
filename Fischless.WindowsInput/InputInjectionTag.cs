namespace Fischless.WindowsInput;

/// <summary>标识本程序发送的输入，空闲检测仍会统计其他程序注入的输入。</summary>
public static class InputInjectionTag
{
    /// <summary>同时适用于 32/64 位 dwExtraInfo 的稳定标记。</summary>
    public static readonly IntPtr Value = new(0x42474950);
    /// <summary>可选输入安全回调，由主程序无人值守作用域建立；抬键清理不调用。</summary>
    public static Action? BeforeInput;
}

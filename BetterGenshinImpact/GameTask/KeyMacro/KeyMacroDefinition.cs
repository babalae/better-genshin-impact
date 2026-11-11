using BetterGenshinImpact.Model;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.GameTask.KeyMacro;

/// <summary>
/// 按键宏的触发方式
/// </summary>
public enum KeyMacroTriggerMode
{
    /// <summary>
    /// 单次：按一下执行一遍，执行中再按忽略
    /// </summary>
    Once,

    /// <summary>
    /// 按住循环：按住时重复执行，松开立即终止
    /// </summary>
    Hold,

    /// <summary>
    /// 切换循环：按一下开始循环，再按一下立即终止
    /// </summary>
    Toggle
}

/// <summary>
/// 一个按键宏。对应 User/BgiMacro 下的一个 txt 文件，文件名即名称。
/// 由 <see cref="BetterGenshinImpact.Service.KeyMacro.KeyMacroStore"/> 在 UI 线程上创建和修改
/// </summary>
public partial class KeyMacroDefinition : ObservableObject
{
    /// <summary>
    /// 宏名称，即不含扩展名的文件名。只能通过 KeyMacroStore.TryRename 修改，以保证文件同步改名
    /// </summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    /// 是否启用；禁用时不注册快捷键
    /// </summary>
    [ObservableProperty]
    private bool _enabled = true;

    /// <summary>
    /// 快捷键字符串，格式同 HotKeyConfig 中的热键（例如 F6、Ctrl + D1、XButton1），空字符串表示未设置
    /// </summary>
    [ObservableProperty]
    private string _hotkey = string.Empty;

    /// <summary>
    /// 快捷键类型。按住循环只能使用键鼠监听
    /// </summary>
    [ObservableProperty]
    private HotKeyTypeEnum _hotkeyType = HotKeyTypeEnum.KeyboardMonitor;

    /// <summary>
    /// 触发方式
    /// </summary>
    [ObservableProperty]
    private KeyMacroTriggerMode _triggerMode = KeyMacroTriggerMode.Once;

    /// <summary>
    /// 宏脚本（不含文件开头的设置行）
    /// </summary>
    [ObservableProperty]
    private string _script = string.Empty;

    /// <summary>
    /// 宏文件的完整路径
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// 实际生效的快捷键类型：按住循环需要松开事件，固定使用键鼠监听
    /// </summary>
    public HotKeyTypeEnum EffectiveHotkeyType => TriggerMode == KeyMacroTriggerMode.Hold ? HotKeyTypeEnum.KeyboardMonitor : HotkeyType;
}

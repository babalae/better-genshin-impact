using BetterGenshinImpact.GameTask.KeyMacro;
using System.Collections.Generic;

namespace BetterGenshinImpact.ViewModel.Pages.KeyMacro;

/// <summary>
/// 新建宏的模板
/// </summary>
/// <param name="Key">模板标识，作为命令参数</param>
/// <param name="Title">菜单显示的名称</param>
/// <param name="Name">新宏的默认名称</param>
/// <param name="Mode">触发方式</param>
/// <param name="Script">脚本</param>
public sealed record KeyMacroTemplate(string Key, string Title, string Name, KeyMacroTriggerMode Mode, string Script);

/// <summary>
/// 内置模板。写在代码里，不新增资源文件
/// </summary>
public static class KeyMacroTemplates
{
    /// <summary>
    /// 全部模板，按菜单顺序排列
    /// </summary>
    public static IReadOnlyList<KeyMacroTemplate> All { get; } =
    [
        new("blank", "空白宏", "新建宏", KeyMacroTriggerMode.Once, ""),
        new("click", "连点左键（切换循环）", "连点左键", KeyMacroTriggerMode.Toggle,
            "// 按一下开始连点，再按一下停止\r\nclick(left)\r\nwait(0.05)"),
        new("hold", "按住一个键 1 秒（单次）", "按住 E", KeyMacroTriggerMode.Once,
            "keydown(e)\r\nwait(1)\r\nkeyup(e)"),
        new("charge-jump", "重击接跳跃（按住循环）", "重击跳", KeyMacroTriggerMode.Hold,
            "// 按住时循环：重击后跳跃取消后摇\r\nmousedown(left)\r\nwait(0.35)\r\nmouseup(left)\r\nkeypress(space)  // 跳跃取消\r\nwait(0.4)")
    ];
}

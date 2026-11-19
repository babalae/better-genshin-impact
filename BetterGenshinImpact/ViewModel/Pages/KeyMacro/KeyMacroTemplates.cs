using BetterGenshinImpact.GameTask.KeyMacro;
using System.Collections.Generic;
using BetterGenshinImpact.Service.I18n;

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
    /// 全部模板，按菜单顺序排列；每次读取按当前语言生成显示名称
    /// </summary>
    public static IReadOnlyList<KeyMacroTemplate> All =>
    [
        new("blank", I18nService.Instance.Translate("空白宏"), I18nService.Instance.Translate("新建宏"), KeyMacroTriggerMode.Once, ""),
        new("click", I18nService.Instance.Translate("连点左键（切换循环）"), I18nService.Instance.Translate("连点左键"), KeyMacroTriggerMode.Toggle,
            "// 按一下开始连点，再按一下停止\r\nclick(left)\r\nwait(0.05)"),
        new("hold", I18nService.Instance.Translate("按住一个键 1 秒（单次）"), I18nService.Instance.Translate("按住 E"), KeyMacroTriggerMode.Once,
            "keydown(e)\r\nwait(1)\r\nkeyup(e)"),
        new("charge-jump", I18nService.Instance.Translate("重击接跳跃（按住循环）"), I18nService.Instance.Translate("重击跳"), KeyMacroTriggerMode.Hold,
            "// 按住时循环：重击后跳跃取消后摇\r\nmousedown(left)\r\nwait(0.35)\r\nmouseup(left)\r\nkeypress(space)  // 跳跃取消\r\nwait(0.4)")
    ];
}

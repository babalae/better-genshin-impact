using System;

using BetterGenshinImpact.Service.I18n;

namespace BetterGenshinImpact.Model;

public enum HotKeyTypeEnum
{
    GlobalRegister, // 全局热键
    KeyboardMonitor, // 键盘监听
}

public static class HotKeyTypeEnumExtension
{
    public static string ToChineseName(this HotKeyTypeEnum type)
    {
        return type switch
        {
            HotKeyTypeEnum.GlobalRegister => "全局热键",
            HotKeyTypeEnum.KeyboardMonitor => "键鼠监听",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }

    public static string ToLocalizedName(this HotKeyTypeEnum type)
    {
        return type switch
        {
            HotKeyTypeEnum.GlobalRegister => I18nService.Instance.Translate("全局热键"),
            HotKeyTypeEnum.KeyboardMonitor => I18nService.Instance.Translate("键鼠监听"),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }
}

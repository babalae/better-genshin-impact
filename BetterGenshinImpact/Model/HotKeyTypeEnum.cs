using BetterGenshinImpact.Helpers;
﻿using System;

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
            HotKeyTypeEnum.GlobalRegister => Lang.S["Gen_12016_fedaa5"],
            HotKeyTypeEnum.KeyboardMonitor => Lang.S["Gen_12015_255785"],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }
}
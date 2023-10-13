using BetterGenshinImpact.Model;
using System;

namespace BetterGenshinImpact.Core.Config;

[Serializable]
public class HotKeyConfig
{
    public string? DisplayName { get; set; }

    public string? FunctionClass { get; set; }

    public HotKey? Hotkey { get; set; }
}
using BetterGenshinImpact.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
/// 速查条显示项：已绑定且生效的快捷键紧凑文本 + 功能短名
/// </summary>
public sealed record OverlayHotkeyDisplayItem(string HotkeyText, string DisplayName);

/// <summary>
/// 遮罩快捷键显示项定义。
/// key 为 <see cref="HotKeyConfig"/> 上的快捷键属性名字符串（nameof(HotKeyConfig.XxxHotkey)），
/// 中文名与快捷键设置页（HotKeyPageViewModel）保持一致。
/// 顺序按设置页的目录分组排列（顶层 / 系统控制 / 实时任务 / 操控辅助 / 独立任务 / 开发者 / 内部测试），
/// 便于两处对照维护；新增快捷键时需同步更新此处与设置页。
/// </summary>
public static class OverlayHotkeyItemDefaults
{
    /// <summary>
    /// 状态栏已展示的 5 个实时任务开关对应的快捷键属性名。
    /// 「速查条排除状态栏已显示项」开启时用于过滤，必须与 MaskWindowViewModel.InitializeStatusList 中
    /// 传入 StatusItem 的属性名保持同源，避免两处维护漂移。
    /// </summary>
    public static IReadOnlySet<string> StatusBarItemPropertyNames { get; } = new HashSet<string>
    {
        nameof(HotKeyConfig.AutoPickEnabledHotkey),
        nameof(HotKeyConfig.AutoSkipEnabledHotkey),
        nameof(HotKeyConfig.AutoSkipHangoutEnabledHotkey),
        nameof(HotKeyConfig.AutoFishingEnabledHotkey),
        nameof(HotKeyConfig.QuickTeleportEnabledHotkey),
    };

    /// <summary>
    /// 显式顺序决定速查条显示顺序，按快捷键设置页的目录分组排列，不要随手改动。
    /// </summary>
    public static IReadOnlyList<string> AllItems { get; } =
    [
        // 顶层
        nameof(HotKeyConfig.BgiEnabledHotkey),
        // 系统控制
        nameof(HotKeyConfig.CancelTaskHotkey),
        nameof(HotKeyConfig.SuspendHotkey),
        nameof(HotKeyConfig.TakeScreenshotHotkey),
        nameof(HotKeyConfig.LogBoxDisplayHotkey),
        nameof(HotKeyConfig.OverlayMetricsDisplayHotkey),
        // 实时任务
        nameof(HotKeyConfig.AutoPickEnabledHotkey),
        nameof(HotKeyConfig.AutoSkipEnabledHotkey),
        nameof(HotKeyConfig.AutoSkipHangoutEnabledHotkey),
        nameof(HotKeyConfig.AutoFishingEnabledHotkey),
        nameof(HotKeyConfig.QuickTeleportEnabledHotkey),
        nameof(HotKeyConfig.SkillCdEnabledHotkey),
        nameof(HotKeyConfig.QuickTeleportTickHotkey),
        nameof(HotKeyConfig.MapMaskEnabledHotkey),
        // 操控辅助
        nameof(HotKeyConfig.TurnAroundHotkey),
        nameof(HotKeyConfig.EnhanceArtifactHotkey),
        nameof(HotKeyConfig.QuickBuyHotkey),
        nameof(HotKeyConfig.OneKeyClaimRewardHotkey),
        nameof(HotKeyConfig.QuickSereniteaPotHotkey),
        nameof(HotKeyConfig.ClickGenshinConfirmButtonHotkey),
        nameof(HotKeyConfig.ClickGenshinCancelButtonHotkey),
        nameof(HotKeyConfig.OneKeyFightHotkey),
        // 独立任务
        nameof(HotKeyConfig.OnedragonHotkey),
        nameof(HotKeyConfig.AutoGeniusInvokationHotkey),
        nameof(HotKeyConfig.AutoWoodHotkey),
        nameof(HotKeyConfig.AutoFightHotkey),
        nameof(HotKeyConfig.AutoDomainHotkey),
        nameof(HotKeyConfig.AutoMusicGameHotkey),
        nameof(HotKeyConfig.AutoFishingGameHotkey),
        nameof(HotKeyConfig.AutoCookGameHotkey),
        // 开发者
        nameof(HotKeyConfig.KeyMouseMacroRecordHotkey),
        nameof(HotKeyConfig.RecognitionTemplateEditorHotkey),
        nameof(HotKeyConfig.RecBigMapPosHotkey),
        nameof(HotKeyConfig.PathRecorderHotkey),
        nameof(HotKeyConfig.AddWaypointHotkey),
        // 内部测试（RuntimeHelper.IsDebug 分支，Release 下通常未绑定）
        nameof(HotKeyConfig.Test1Hotkey),
        nameof(HotKeyConfig.Test2Hotkey),
        nameof(HotKeyConfig.ExecutePathHotkey),
    ];

    public static Dictionary<string, bool> CreateDefaultItems()
    {
        return AllItems.ToDictionary(item => item, IsEnabledByDefault);
    }

    public static bool IsEnabledByDefault(string configPropertyName)
    {
        // 默认全部允许显示：用户绑定新快捷键后自动出现在遮罩上，无需再去设置页勾选；嫌多时可反向取消。
        return true;
    }

    /// <summary>
    /// 获取中文显示名。未知 key 返回空字符串，渲染层应跳过该项，不得回退显示英文属性名。
    /// </summary>
    public static string GetDisplayName(string configPropertyName)
    {
        return configPropertyName switch
        {
            nameof(HotKeyConfig.BgiEnabledHotkey) => "启动停止",
            nameof(HotKeyConfig.CancelTaskHotkey) => "停止任务",
            nameof(HotKeyConfig.SuspendHotkey) => "暂停任务",
            nameof(HotKeyConfig.TakeScreenshotHotkey) => "游戏截图",
            nameof(HotKeyConfig.LogBoxDisplayHotkey) => "日志开关",
            nameof(HotKeyConfig.OverlayMetricsDisplayHotkey) => "指标栏开关",
            nameof(HotKeyConfig.AutoPickEnabledHotkey) => "自动拾取",
            nameof(HotKeyConfig.AutoSkipEnabledHotkey) => "自动剧情",
            nameof(HotKeyConfig.AutoSkipHangoutEnabledHotkey) => "自动邀约",
            nameof(HotKeyConfig.AutoFishingEnabledHotkey) => "自动钓鱼",
            nameof(HotKeyConfig.QuickTeleportEnabledHotkey) => "快速传送",
            nameof(HotKeyConfig.SkillCdEnabledHotkey) => "冷却提示",
            nameof(HotKeyConfig.QuickTeleportTickHotkey) => "触发传送",
            nameof(HotKeyConfig.MapMaskEnabledHotkey) => "地图遮罩",
            nameof(HotKeyConfig.TurnAroundHotkey) => "旋转视角",
            nameof(HotKeyConfig.EnhanceArtifactHotkey) => "快速强化",
            nameof(HotKeyConfig.QuickBuyHotkey) => "快速购买",
            nameof(HotKeyConfig.OneKeyClaimRewardHotkey) => "领取奖励",
            nameof(HotKeyConfig.QuickSereniteaPotHotkey) => "进出尘歌壶",
            nameof(HotKeyConfig.ClickGenshinConfirmButtonHotkey) => "点击确认",
            nameof(HotKeyConfig.ClickGenshinCancelButtonHotkey) => "点击取消",
            nameof(HotKeyConfig.OneKeyFightHotkey) => "一键战斗宏",
            nameof(HotKeyConfig.OnedragonHotkey) => "一条龙",
            nameof(HotKeyConfig.AutoGeniusInvokationHotkey) => "七圣召唤",
            nameof(HotKeyConfig.AutoWoodHotkey) => "自动伐木",
            nameof(HotKeyConfig.AutoFightHotkey) => "自动战斗",
            nameof(HotKeyConfig.AutoDomainHotkey) => "自动秘境",
            nameof(HotKeyConfig.AutoMusicGameHotkey) => "自动音游",
            nameof(HotKeyConfig.AutoFishingGameHotkey) => "自动钓鱼(任务)",
            nameof(HotKeyConfig.AutoCookGameHotkey) => "自动烹饪",
            nameof(HotKeyConfig.KeyMouseMacroRecordHotkey) => "键鼠录制",
            nameof(HotKeyConfig.RecognitionTemplateEditorHotkey) => "模板素材制作",
            nameof(HotKeyConfig.RecBigMapPosHotkey) => "获取地图中心点",
            nameof(HotKeyConfig.PathRecorderHotkey) => "路径记录器",
            nameof(HotKeyConfig.AddWaypointHotkey) => "添加路径点",
            nameof(HotKeyConfig.Test1Hotkey) => "测试",
            nameof(HotKeyConfig.Test2Hotkey) => "测试2",
            nameof(HotKeyConfig.ExecutePathHotkey) => "播放路径",
            _ => string.Empty
        };
    }

    /// <summary>
    /// 获取完整功能名（与快捷键设置页一致），用于提示信息等长文本场景。
    /// </summary>
    public static string GetFullDisplayName(string configPropertyName)
    {
        return configPropertyName switch
        {
            nameof(HotKeyConfig.BgiEnabledHotkey) => "启动停止 BetterGI",
            nameof(HotKeyConfig.CancelTaskHotkey) => "停止当前脚本/独立任务",
            nameof(HotKeyConfig.SuspendHotkey) => "暂停当前脚本/独立任务",
            nameof(HotKeyConfig.TakeScreenshotHotkey) => "游戏截图",
            nameof(HotKeyConfig.LogBoxDisplayHotkey) => "日志与状态窗口展示开关",
            nameof(HotKeyConfig.OverlayMetricsDisplayHotkey) => "遮罩指标栏展示开关",
            nameof(HotKeyConfig.AutoPickEnabledHotkey) => "自动拾取开关",
            nameof(HotKeyConfig.AutoSkipEnabledHotkey) => "自动剧情开关",
            nameof(HotKeyConfig.AutoSkipHangoutEnabledHotkey) => "自动邀约开关",
            nameof(HotKeyConfig.AutoFishingEnabledHotkey) => "自动钓鱼开关",
            nameof(HotKeyConfig.QuickTeleportEnabledHotkey) => "快速传送开关",
            nameof(HotKeyConfig.SkillCdEnabledHotkey) => "冷却提示开关",
            nameof(HotKeyConfig.QuickTeleportTickHotkey) => "手动触发快速传送（按住起效）",
            nameof(HotKeyConfig.MapMaskEnabledHotkey) => "地图遮罩开关",
            nameof(HotKeyConfig.TurnAroundHotkey) => "长按旋转视角",
            nameof(HotKeyConfig.EnhanceArtifactHotkey) => "快速强化圣遗物",
            nameof(HotKeyConfig.QuickBuyHotkey) => "快速购买商店物品",
            nameof(HotKeyConfig.OneKeyClaimRewardHotkey) => "一键领取奖励",
            nameof(HotKeyConfig.QuickSereniteaPotHotkey) => "快速进出尘歌壶",
            nameof(HotKeyConfig.ClickGenshinConfirmButtonHotkey) => "点击原神内确认按钮",
            nameof(HotKeyConfig.ClickGenshinCancelButtonHotkey) => "点击原神内取消按钮",
            nameof(HotKeyConfig.OneKeyFightHotkey) => "一键战斗宏",
            nameof(HotKeyConfig.OnedragonHotkey) => "启动/停止一条龙",
            nameof(HotKeyConfig.AutoGeniusInvokationHotkey) => "启动/停止自动七圣召唤",
            nameof(HotKeyConfig.AutoWoodHotkey) => "启动/停止自动伐木",
            nameof(HotKeyConfig.AutoFightHotkey) => "启动/停止自动战斗",
            nameof(HotKeyConfig.AutoDomainHotkey) => "启动/停止自动秘境",
            nameof(HotKeyConfig.AutoMusicGameHotkey) => "启动/停止自动音游",
            nameof(HotKeyConfig.AutoFishingGameHotkey) => "启动/停止自动钓鱼",
            nameof(HotKeyConfig.AutoCookGameHotkey) => "启动/停止自动烹饪",
            nameof(HotKeyConfig.KeyMouseMacroRecordHotkey) => "启动/停止键鼠录制",
            nameof(HotKeyConfig.RecognitionTemplateEditorHotkey) => "模板素材制作",
            nameof(HotKeyConfig.RecBigMapPosHotkey) => "获取当前大地图中心点位置",
            nameof(HotKeyConfig.PathRecorderHotkey) => "启动/停止路径记录器",
            nameof(HotKeyConfig.AddWaypointHotkey) => "添加路径点",
            nameof(HotKeyConfig.Test1Hotkey) => "测试",
            nameof(HotKeyConfig.Test2Hotkey) => "测试2",
            nameof(HotKeyConfig.ExecutePathHotkey) => "播放内存中的路径",
            _ => string.Empty
        };
    }

    /// <summary>
    /// 判断快捷键的当前配置值是否实际生效。
    /// 与 <see cref="HotKeySettingModel.RegisterHotKey"/> 的行为严格对齐：
    /// 键鼠监听（KeyboardMonitor）类型下的组合键（带 Ctrl/Alt/Shift/Win）会被静默置空、无法触发，
    /// 必须过滤以免显示按了没反应的按键；全局热键（GlobalRegister）支持组合键；鼠标侧键不受修饰键影响。
    /// 已知局限：热键被系统占用导致注册失败时无法感知（配置字符串仍保留），接受该误差。
    /// </summary>
    public static bool IsHotkeyEffective(string? hotkeyValue, string? hotkeyTypeValue)
    {
        if (string.IsNullOrWhiteSpace(hotkeyValue))
        {
            return false;
        }

        HotKey hotkey;
        try
        {
            hotkey = HotKey.FromString(hotkeyValue);
        }
        catch (Exception)
        {
            // 配置被外部篡改成无法解析的值，视为未生效
            return false;
        }

        if (hotkey.IsEmpty)
        {
            return false;
        }

        // 鼠标侧键走 MouseHook 注册，修饰键会被忽略，始终生效
        if (hotkey.MouseButton != MouseButton.Left)
        {
            return true;
        }

        // 全局热键通过系统 RegisterHotKey 注册，支持组合键
        if (string.Equals(hotkeyTypeValue, nameof(HotKeyTypeEnum.GlobalRegister), StringComparison.Ordinal))
        {
            return true;
        }

        // 键鼠监听仅支持无修饰键的单键
        return hotkey.Modifiers == ModifierKeys.None;
    }

    /// <summary>
    /// 将配置中的快捷键字符串转为适合遮罩显示的紧凑文本："Ctrl + F" → "Ctrl+F"。
    /// </summary>
    public static string ToCompactHotkeyText(string hotkeyValue)
    {
        return hotkeyValue.Replace(" + ", "+", StringComparison.Ordinal);
    }
}

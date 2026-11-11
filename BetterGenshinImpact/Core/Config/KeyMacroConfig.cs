using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
/// 按键宏的全局设置。宏本身保存在 User/BgiMacro 下，每个宏一个 txt 文件，见 Docs/design/key-macro.md
/// </summary>
[Serializable]
public partial class KeyMacroConfig : ObservableObject
{
    /// <summary>
    /// 按键宏总开关。关闭时注销全部按键宏的快捷键并停止正在运行的宏
    /// </summary>
    [ObservableProperty]
    private bool _enabled = true;

    /// <summary>
    /// 编辑页上次使用的视图，true 为脚本视图，false 为步骤视图
    /// </summary>
    [ObservableProperty]
    private bool _preferScriptView;
}

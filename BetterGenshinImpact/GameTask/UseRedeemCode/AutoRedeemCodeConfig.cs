using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.GameTask.UseRedeemCode;

[Serializable]
public partial class AutoRedeemCodeConfig: ObservableObject
{
    /// <summary>
    /// 是否启用剪切板监听
    /// </summary>
    [ObservableProperty]
    private bool _clipboardListenerEnabled = true;
}
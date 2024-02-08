using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Config
{
    /// <summary>
    /// 遮罩窗口配置
    /// </summary>
    [Serializable]
    public partial class CommonConfig : ObservableObject
    {
        /// <summary>
        /// 是否启用遮罩窗口
        /// </summary>
        [ObservableProperty] private bool _screenshotEnabled = false;
        /// <summary>
        /// UID遮盖是否启用
        /// </summary>
        [ObservableProperty] private bool _screenshotUidCoverEnabled = false;
    }
}
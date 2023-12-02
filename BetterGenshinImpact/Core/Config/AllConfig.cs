using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.AutoPick;
using BetterGenshinImpact.GameTask.AutoSkip;
using CommunityToolkit.Mvvm.ComponentModel;
using Fischless.GameCapture;
using System;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation;
using BetterGenshinImpact.GameTask.AutoWood;

namespace BetterGenshinImpact.Core.Config
{
    /// <summary>
    /// 更好的原神配置
    /// </summary>
    [Serializable]
    public partial class AllConfig : ObservableObject
    {
        /// <summary>
        /// 窗口捕获的方式
        /// </summary>
        [ObservableProperty] private string _captureMode = CaptureModes.BitBlt.ToString();

        ///// <summary>
        ///// 窗口捕获帧数/触发器触发频率
        ///// </summary>
        //[ObservableProperty] private int _frameRate = 30;

        /// <summary>
        /// 触发器触发频率(ms)
        /// </summary>
        [ObservableProperty] private int _triggerInterval = 50;

        /// <summary>
        /// 不展示新版本提示的最新版本
        /// </summary>
        [ObservableProperty] private string _notShowNewVersionNoticeEndVersion = "";

        /// <summary>
        /// 遮罩窗口配置
        /// </summary>
        public MaskWindowConfig MaskWindowConfig { get; set; } = new();

        /// <summary>
        /// 自动拾取配置
        /// </summary>
        public AutoPickConfig AutoPickConfig { get; set; } = new();

        /// <summary>
        /// 自动剧情配置
        /// </summary>
        public AutoSkipConfig AutoSkipConfig { get; set; } = new();

        /// <summary>
        /// 自动钓鱼配置
        /// </summary>
        public AutoFishingConfig AutoFishingConfig { get; set; } = new();

        /// <summary>
        /// 自动打牌配置
        /// </summary>
        public AutoGeniusInvokationConfig AutoGeniusInvokationConfig { get; set; } = new();

        /// <summary>
        /// 自动伐木配置
        /// </summary>
        public AutoWoodConfig AutoWoodConfig { get; set; } = new();

        /// <summary>
        /// 脚本类配置
        /// </summary>
        public MacroConfig MacroConfig { get; set; } = new();

        /// <summary>
        /// 快捷键配置
        /// </summary>
        public HotKeyConfig HotKeyConfig { get; set; } = new();

        /// <summary>
        /// 原神安装路径
        /// </summary>
        [ObservableProperty]
        private string _installPath = "";

        [JsonIgnore] public Action? OnAnyChangedAction { get; set; }

        public void InitEvent()
        {
            this.PropertyChanged += OnAnyPropertyChanged;
            MaskWindowConfig.PropertyChanged += OnAnyPropertyChanged;
            AutoPickConfig.PropertyChanged += OnAnyPropertyChanged;
            AutoSkipConfig.PropertyChanged += OnAnyPropertyChanged;
            AutoFishingConfig.PropertyChanged += OnAnyPropertyChanged;
            MacroConfig.PropertyChanged += OnAnyPropertyChanged;
            HotKeyConfig.PropertyChanged += OnAnyPropertyChanged;
        }

        public void OnAnyPropertyChanged(object? sender, EventArgs args)
        {
            GameTaskManager.RefreshTriggerConfigs();
            OnAnyChangedAction?.Invoke();
        }
    }
}
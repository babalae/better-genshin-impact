using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.GameTask.AutoPick
{
    public enum AutoPickMode
    {
        Blacklist,
        Whitelist
    }

    /// <summary>
    /// 非16:9分辨率下可能无法正常工作
    /// </summary>
    [Serializable]
    public partial class AutoPickConfig : ObservableObject
    {
        /// <summary>
        /// 触发器是否启用
        /// </summary>
        [ObservableProperty] private bool _enabled = true;

        /// <summary>
        /// 1080p下拾取文字左边的起始偏移
        /// </summary>
        [ObservableProperty] private int _itemIconLeftOffset = 60;

        /// <summary>
        /// 1080p下拾取文字的起始偏移
        /// </summary>
        [ObservableProperty] private int _itemTextLeftOffset = 115;

        /// <summary>
        /// 1080p下拾取文字的终止偏移
        /// </summary>
        [ObservableProperty] private int _itemTextRightOffset = 400;

        /// <summary>
        /// 文字识别引擎
        /// - Paddle
        /// - Yap
        /// </summary>
        [ObservableProperty]
        private string _ocrEngine = PickOcrEngineEnum.Paddle.ToString();

        /// <summary>
        /// 急速模式
        /// 无视文字识别结果，直接拾取
        /// </summary>

        [ObservableProperty] private bool _fastModeEnabled = false;

        /// <summary>
        /// 自定义按键拾取
        /// </summary>
        [ObservableProperty] private string _pickKey = "F";

        /// <summary>
        /// 自动拾取名单模式
        /// </summary>
        [ObservableProperty]
        [property: JsonConverter(typeof(JsonStringEnumConverter<AutoPickMode>))]
        private AutoPickMode _mode = AutoPickMode.Whitelist;

        // 黑名单模式的拾取规则启用状态
        [ObservableProperty]
        private bool _blacklistModePickEnabled = false;

        // 白名单模式的不拾取规则启用状态
        [ObservableProperty]
        private bool _whitelistModeDoNotPickEnabled = true;

        /// <summary>
        /// 兼容旧版白名单开关，读取后迁移到黑名单模式的拾取规则。
        /// </summary>
        [JsonPropertyName("whiteListEnabled")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? LegacyWhiteListEnabled { get; set; }

        public void MigrateLegacyConfig()
        {
            if (LegacyWhiteListEnabled is null)
            {
                return;
            }

            Mode = AutoPickMode.Blacklist;
            BlacklistModePickEnabled = LegacyWhiteListEnabled.Value;
            LegacyWhiteListEnabled = null;
        }
    }
}

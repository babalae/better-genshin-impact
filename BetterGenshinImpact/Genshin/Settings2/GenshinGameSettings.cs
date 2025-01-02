using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization;
using BetterGenshinImpact.Genshin.Settings;
using Microsoft.Win32;
using Newtonsoft.Json;
using JsonSerializerOptions = System.Text.Json.JsonSerializerOptions;

namespace BetterGenshinImpact.Genshin.Settings2;

public class GenshinGameSettings
{
    [JsonProperty("deviceUUID")]
    public string DeviceUUID { get; set; } // 设备唯一标识符

    [JsonProperty("userLocalDataVersionId")]
    public string UserLocalDataVersionId { get; set; } // 用户本地数据版本ID

    [JsonProperty("deviceLanguageType")]
    public int DeviceLanguageType { get; set; } // 设备语言类型

    [JsonProperty("deviceVoiceLanguageType")]
    public int DeviceVoiceLanguageType { get; set; } // 设备语音语言类型

    [JsonProperty("selectedServerName")]
    public string SelectedServerName { get; set; } // 选择的服务器名称

    [JsonProperty("localLevelIndex")]
    public int LocalLevelIndex { get; set; } // 本地等级索引

    [JsonProperty("deviceID")]
    public string DeviceID { get; set; } // 设备ID

    [JsonProperty("targetUID")]
    public string TargetUID { get; set; } // 目标用户ID

    [JsonProperty("curAccountName")]
    public string CurAccountName { get; set; } // 当前账户名称

    [JsonProperty("uiSaveData")]
    public string UiSaveData { get; set; } // UI保存数据

    [JsonProperty("inputData")]
    public string InputData { get; set; } // 输入设置数据

    [JsonProperty("graphicsData")]
    public string GraphicsData { get; set; } // 图形设置数据

    [JsonProperty("globalPerfData")]
    public string GlobalPerfData { get; set; } // 全局性能数据

    [JsonProperty("miniMapConfig")]
    public int MiniMapConfig { get; set; } // 小地图配置

    [JsonProperty("enableCameraSlope")]
    public bool EnableCameraSlope { get; set; } // 启用相机坡度

    [JsonProperty("enableCameraCombatLock")]
    public bool EnableCameraCombatLock { get; set; } // 启用相机战斗锁定

    [JsonProperty("completionPkg")]
    public bool CompletionPkg { get; set; } // 完成的包

    [JsonProperty("completionPlayGoPkg")]
    public bool CompletionPlayGoPkg { get; set; } // 完成的PlayGo包

    [JsonProperty("onlyPlayWithPSPlayer")]
    public bool OnlyPlayWithPSPlayer { get; set; } // 仅与PS玩家一起游戏

    [JsonProperty("onlyPlayWithXboxPlayer")]
    public bool OnlyPlayWithXboxPlayer { get; set; } // 仅与Xbox玩家一起游戏

    [JsonProperty("needPlayGoFullPkgPatch")]
    public bool NeedPlayGoFullPkgPatch { get; set; } // 需要PlayGo完整包补丁

    [JsonProperty("resinNotification")]
    public bool ResinNotification { get; set; } // 树脂通知

    [JsonProperty("exploreNotification")]
    public bool ExploreNotification { get; set; } // 探索通知

    [JsonProperty("volumeGlobal")]
    public int VolumeGlobal { get; set; } // 全局音量

    [JsonProperty("volumeSFX")]
    public int VolumeSFX { get; set; } // 音效音量

    [JsonProperty("volumeMusic")]
    public int VolumeMusic { get; set; } // 音乐音量

    [JsonProperty("volumeVoice")]
    public int VolumeVoice { get; set; } // 语音音量

    [JsonProperty("audioAPI")]
    public int AudioAPI { get; set; } // 音频API

    [JsonProperty("audioDynamicRange")]
    public int AudioDynamicRange { get; set; } // 音频动态范围

    [JsonProperty("audioOutput")]
    public int AudioOutput { get; set; } // 音频输出

    [JsonProperty("_audioSuccessInit")]
    public bool AudioSuccessInit { get; set; } // 音频成功初始化

    [JsonProperty("enableAudioChangeAndroidMinimumBufferCapacity")]
    public bool EnableAudioChangeAndroidMinimumBufferCapacity { get; set; } // 启用更改Android最小缓冲容量的音频

    [JsonProperty("audioAndroidMiniumBufferCapacity")]
    public int AudioAndroidMiniumBufferCapacity { get; set; } // Android音频最小缓冲容量

    [JsonProperty("vibrationLevel")]
    public int VibrationLevel { get; set; } // 震动等级

    [JsonProperty("vibrationIntensity")]
    public int VibrationIntensity { get; set; } // 震动强度

    [JsonProperty("usingNewVibrationSetting")]
    public bool UsingNewVibrationSetting { get; set; } // 使用新的震动设置

    [JsonProperty("motionBlur")]
    public bool MotionBlur { get; set; } // 动态模糊

    [JsonProperty("gyroAiming")]
    public bool GyroAiming { get; set; } // 陀螺仪瞄准

    [JsonProperty("gyroHorMoveSpeedIndex")]
    public int GyroHorMoveSpeedIndex { get; set; } // 陀螺仪水平移动速度索引

    [JsonProperty("gyroVerMoveSpeedIndex")]
    public int GyroVerMoveSpeedIndex { get; set; } // 陀螺仪垂直移动速度索引

    [JsonProperty("gyroHorReverse")]
    public bool GyroHorReverse { get; set; } // 陀螺仪水平反转

    [JsonProperty("gyroVerReverse")]
    public bool GyroVerReverse { get; set; } // 陀螺仪垂直反转

    [JsonProperty("gyroRotateType")]
    public int GyroRotateType { get; set; } // 陀螺仪旋转类型

    [JsonProperty("gyroExcludeRightStickVerInput")]
    public bool GyroExcludeRightStickVerInput { get; set; } // 陀螺仪排除右摇杆垂直输入

    [JsonProperty("firstHDRSetting")]
    public bool FirstHDRSetting { get; set; } // 首次HDR设置

    [JsonProperty("maxLuminosity")]
    public float MaxLuminosity { get; set; } // 最大亮度

    [JsonProperty("uiPaperWhite")]
    public float UiPaperWhite { get; set; } // UI纸白

    [JsonProperty("scenePaperWhite")]
    public float ScenePaperWhite { get; set; } // 场景纸白

    /// <summary>
    /// 2.200000047683716
    /// </summary>
    [JsonProperty("gammaValue")]
    public string GammaValue { get; set; } // 伽马值

    [JsonProperty("enableHDR")]
    public bool EnableHDR { get; set; } // 启用HDR

    [JsonProperty("_overrideControllerMapKeyList")]
    public List<string> OverrideControllerMapKeyList { get; set; } // 覆盖控制器映射键列表

    [JsonProperty("_overrideControllerMapValueList")]
    public List<string> OverrideControllerMapValueList { get; set; } // 覆盖控制器映射值列表

    [JsonProperty("rewiredMapMigrateRecord")]
    public List<string> RewiredMapMigrateRecord { get; set; } // 重布线映射迁移记录

    [JsonProperty("rewiredDisableKeyboard")]
    public bool RewiredDisableKeyboard { get; set; } // 重布线禁用键盘

    [JsonProperty("rewiredEnableKeyboard")]
    public bool RewiredEnableKeyboard { get; set; } // 重布线启用键盘

    [JsonProperty("rewiredEnableEDS")]
    public bool RewiredEnableEDS { get; set; } // 重布线启用EDS

    [JsonProperty("disableRewiredDelayInit")]
    public bool DisableRewiredDelayInit { get; set; } // 禁用重布线延迟初始化

    [JsonProperty("disableRewiredInitProtection")]
    public bool DisableRewiredInitProtection { get; set; } // 禁用重布线初始化保护

    [JsonProperty("disableSetJoyInfoForWebViewOnPCMobile")]
    public bool DisableSetJoyInfoForWebViewOnPCMobile { get; set; } // 禁用在PC和移动设备上的WebView设置Joy信息

    [JsonProperty("conflictKeyBindingElementId")]
    public List<int> ConflictKeyBindingElementId { get; set; } // 冲突的按键绑定元素ID

    [JsonProperty("conflictKeyBindingActionId")]
    public List<int> ConflictKeyBindingActionId { get; set; } // 冲突的按键绑定动作ID

    [JsonProperty("lastSeenPreDownloadTime")]
    public long LastSeenPreDownloadTime { get; set; } // 上次看到的预下载时间

    [JsonProperty("lastSeenSettingResourceTabScriptVersion")]
    public string LastSeenSettingResourceTabScriptVersion { get; set; } // 上次看到的设置资源标签脚本版本

    [JsonProperty("enableEffectAssembleInEditor")]
    public bool EnableEffectAssembleInEditor { get; set; } // 启用在编辑器中组装效果

    [JsonProperty("forceDisableQuestResourceManagement")]
    public bool ForceDisableQuestResourceManagement { get; set; } // 强制禁用任务资源管理

    [JsonProperty("needReportQuestResourceDeleteStatusFiles")]
    public bool NeedReportQuestResourceDeleteStatusFiles { get; set; } // 需要报告任务资源删除状态文件

    [JsonProperty("disableTeamPageBackgroundSwitch")]
    public bool DisableTeamPageBackgroundSwitch { get; set; } // 禁用团队页面背景切换

    [JsonProperty("disableHttpDns")]
    public bool DisableHttpDns { get; set; } // 禁用HTTP DNS

    [JsonProperty("mtrCached")]
    public bool MtrCached { get; set; } // MTR缓存

    [JsonProperty("mtrIsOpen")]
    public bool MtrIsOpen { get; set; } // MTR是否开启

    [JsonProperty("mtrMaxTTL")]
    public int MtrMaxTTL { get; set; } // MTR最大TTL

    [JsonProperty("mtrTimeOut")]
    public int MtrTimeOut { get; set; } // MTR超时时间

    [JsonProperty("mtrTraceCount")]
    public int MtrTraceCount { get; set; } // MTR跟踪次数

    [JsonProperty("mtrAbortTimeOutCount")]
    public int MtrAbortTimeOutCount { get; set; } // MTR中止超时计数

    [JsonProperty("mtrAutoTraceInterval")]
    public int MtrAutoTraceInterval { get; set; } // MTR自动跟踪间隔

    [JsonProperty("mtrTraceCDEachReason")]
    public int MtrTraceCDEachReason { get; set; } // MTR每个原因的冷却时间

    [JsonProperty("mtrTimeInterval")]
    public int MtrTimeInterval { get; set; } // MTR时间间隔

    [JsonProperty("mtrBanReasons")]
    public List<string> MtrBanReasons { get; set; } // MTR禁止原因

    [JsonProperty("_customDataKeyList")]
    public List<string> CustomDataKeyList { get; set; } // 自定义数据键列表

    [JsonProperty("_customDataValueList")]
    public List<string> CustomDataValueList { get; set; } // 自定义数据值列表

    [JsonProperty("_serializedCodeSwitches")]
    public List<int> SerializedCodeSwitches { get; set; } // 序列化代码开关

    [JsonProperty("urlCheckCached")]
    public bool UrlCheckCached { get; set; } // URL检查缓存

    [JsonProperty("urlCheckIsOpen")]
    public bool UrlCheckIsOpen { get; set; } // URL检查是否开启

    [JsonProperty("urlCheckAllIP")]
    public bool UrlCheckAllIP { get; set; } // URL检查所有IP

    [JsonProperty("urlCheckTimeOut")]
    public int UrlCheckTimeOut { get; set; } // URL检查超时时间

    [JsonProperty("urlCheckSueecssTraceCount")]
    public int UrlCheckSuccessTraceCount { get; set; } // URL检查成功跟踪计数

    [JsonProperty("urlCheckErrorTraceCount")]
    public int UrlCheckErrorTraceCount { get; set; } // URL检查错误跟踪计数

    [JsonProperty("urlCheckAbortTimeOutCount")]
    public int UrlCheckAbortTimeOutCount { get; set; } // URL检查中止超时计数

    [JsonProperty("urlCheckTimeInterval")]
    public int UrlCheckTimeInterval { get; set; } // URL检查时间间隔

    [JsonProperty("urlCheckCDEachReason")]
    public int UrlCheckCDEachReason { get; set; } // URL检查每个原因的冷却时间

    [JsonProperty("urlCheckBanReasons")]
    public List<string> UrlCheckBanReasons { get; set; } // URL检查禁止原因

    [JsonProperty("mtrUseOldWinVersion")]
    public bool MtrUseOldWinVersion { get; set; } // 使用旧版Windows的MTR

    [JsonProperty("greyTestDeviceUniqueId")]
    public string GreyTestDeviceUniqueId { get; set; } // 灰度测试设备唯一ID

    [JsonProperty("muteAudioOnAppMinimized")]
    public bool MuteAudioOnAppMinimized { get; set; } // 应用最小化时静音

    [JsonProperty("disableFallbackControllerType")]
    public bool DisableFallbackControllerType { get; set; } // 禁用回退控制器类型

    [JsonProperty("lastShowDoorProgress")]
    public float LastShowDoorProgress { get; set; } // 上次显示门进度

    [JsonProperty("globalPerfSettingVersion")]
    public int GlobalPerfSettingVersion { get; set; } // 全局性能设置版本


    public static string? GetStrFromRegistry()
    {
        if (GenshinRegistry.GetRegistryKey() is not { } hk)
        {
            return null;
        }

        using (hk)
        {
            string value_name = SearchRegistryName(hk);
            if (hk.GetValue(value_name) is not byte[] rawBytes)
            {
                return null;
            }

            var str = Encoding.UTF8.GetString(rawBytes);
            Debug.WriteLine(str);

            return str;
        }
    }

    public static GenshinGameSettings? Parse(string json)
    {
        return JsonConvert.DeserializeObject<GenshinGameSettings>(json);
    }


    private static string SearchRegistryName(RegistryKey key)
    {
        string value_name = string.Empty;
        string[] names = key.GetValueNames();

        foreach (string name in names)
        {
            if (name.Contains("GENERAL_DATA"))
            {
                value_name = name;
                break;
            }
        }

        if (value_name == string.Empty)
        {
            throw new ArgumentException(value_name);
        }

        return value_name;
    }
}
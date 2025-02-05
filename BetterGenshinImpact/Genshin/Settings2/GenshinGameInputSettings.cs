using Newtonsoft.Json;

namespace BetterGenshinImpact.Genshin.Settings2;

public class GenshinGameInputSettings
{
    [JsonProperty("scriptVersion")]
    public string ScriptVersion { get; set; } // 脚本版本

    [JsonProperty("mouseSensitivity")]
    public string MouseSensitivity { get; set; } // 鼠标灵敏度

    [JsonProperty("joypadSenseIndex")]
    public int JoypadSenseIndex { get; set; } // 手柄灵敏度索引

    [JsonProperty("joypadFocusSenseIndex")]
    public int JoypadFocusSenseIndex { get; set; } // 手柄焦点灵敏度索引

    [JsonProperty("joypadInvertCameraX")]
    public bool JoypadInvertCameraX { get; set; } // 手柄X轴反转摄像头

    [JsonProperty("joypadInvertCameraY")]
    public bool JoypadInvertCameraY { get; set; } // 手柄Y轴反转摄像头

    [JsonProperty("joypadInvertFocusCameraX")]
    public bool JoypadInvertFocusCameraX { get; set; } // 手柄X轴反转焦点摄像头

    [JsonProperty("joypadInvertFocusCameraY")]
    public bool JoypadInvertFocusCameraY { get; set; } // 手柄Y轴反转焦点摄像头

    [JsonProperty("mouseSenseIndex")]
    public int MouseSenseIndex { get; set; } // 鼠标灵敏度索引

    [JsonProperty("mouseFocusSenseIndex")]
    public int MouseFocusSenseIndex { get; set; } // 鼠标焦点灵敏度索引

    [JsonProperty("touchpadSenseIndex")]
    public int TouchpadSenseIndex { get; set; } // 触摸板灵敏度索引

    [JsonProperty("touchpadFocusSenseIndex")]
    public int TouchpadFocusSenseIndex { get; set; } // 触摸板焦点灵敏度索引

    [JsonProperty("enableTouchpadFocusAcceleration")]
    public bool EnableTouchpadFocusAcceleration { get; set; } // 启用触摸板焦点加速度

    [JsonProperty("lastJoypadDefaultScale")]
    public float LastJoypadDefaultScale { get; set; } // 最后一次手柄默认缩放

    [JsonProperty("lastJoypadFocusScale")]
    public float LastJoypadFocusScale { get; set; } // 最后一次手柄焦点缩放

    [JsonProperty("lastPCDefaultScale")]
    public float LastPCDefaultScale { get; set; } // 最后一次PC默认缩放

    [JsonProperty("lastPCFocusScale")]
    public float LastPCFocusScale { get; set; } // 最后一次PC焦点缩放

    [JsonProperty("lastTouchDefaultScale")]
    public float LastTouchDefaultScale { get; set; } // 最后一次触摸默认缩放

    [JsonProperty("lastTouchFcousScale")]
    public float LastTouchFocusScale { get; set; } // 最后一次触摸焦点缩放

    [JsonProperty("switchWalkRunByBtn")]
    public bool SwitchWalkRunByBtn { get; set; } // 通过按钮切换行走和奔跑

    [JsonProperty("skiffCameraAutoFix")]
    public bool SkiffCameraAutoFix { get; set; } // 小艇摄像头自动修正

    [JsonProperty("skiffCameraAutoFixInCombat")]
    public bool SkiffCameraAutoFixInCombat { get; set; } // 战斗中小艇摄像头自动修正

    [JsonProperty("cameraDistanceRatio")]
    public float CameraDistanceRatio { get; set; } // 摄像头距离比率

    [JsonProperty("wwiseVibration")]
    public bool WwiseVibration { get; set; } // Wwise振动

    [JsonProperty("isYInited")]
    public bool IsYInited { get; set; } // Y轴是否初始化

    [JsonProperty("joypadSenseIndexY")]
    public int JoypadSenseIndexY { get; set; } // 手柄Y轴灵敏度索引

    [JsonProperty("joypadFocusSenseIndexY")]
    public int JoypadFocusSenseIndexY { get; set; } // 手柄Y轴焦点灵敏度索引

    [JsonProperty("mouseSenseIndexY")]
    public int MouseSenseIndexY { get; set; } // 鼠标Y轴灵敏度索引

    [JsonProperty("mouseFocusSenseIndexY")]
    public int MouseFocusSenseIndexY { get; set; } // 鼠标Y轴焦点灵敏度索引

    [JsonProperty("touchpadSenseIndexY")]
    public int TouchpadSenseIndexY { get; set; } // 触摸板Y轴灵敏度索引

    [JsonProperty("touchpadFocusSenseIndexY")]
    public int TouchpadFocusSenseIndexY { get; set; } // 触摸板Y轴焦点灵敏度索引

    [JsonProperty("lastJoypadDefaultScaleY")]
    public float LastJoypadDefaultScaleY { get; set; } // 最后一次手柄Y轴默认缩放

    [JsonProperty("lastJoypadFocusScaleY")]
    public float LastJoypadFocusScaleY { get; set; } // 最后一次手柄Y轴焦点缩放

    [JsonProperty("lastPCDefaultScaleY")]
    public float LastPCDefaultScaleY { get; set; } // 最后一次PC Y轴默认缩放

    [JsonProperty("lastPCFocusScaleY")]
    public float LastPCFocusScaleY { get; set; } // 最后一次PC Y轴焦点缩放

    [JsonProperty("lastTouchDefaultScaleY")]
    public float LastTouchDefaultScaleY { get; set; } // 最后一次触摸Y轴默认缩放

    [JsonProperty("lastTouchFcousScaleY")]
    public float LastTouchFocusScaleY { get; set; } // 最后一次触摸Y轴焦点缩放

    public static GenshinGameInputSettings? Parse(string json)
    {
        return JsonConvert.DeserializeObject<GenshinGameInputSettings>(json);
    }
}
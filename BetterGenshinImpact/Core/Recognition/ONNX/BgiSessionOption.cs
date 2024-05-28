using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Model;
using Microsoft.ML.OnnxRuntime;
using System.ComponentModel;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

public class BgiSessionOption : Singleton<BgiSessionOption>
{
    public static string[] InferenceDeviceTypes { get; } = ["CPU", "GPU_DirectML"];

    public SessionOptions Options { get; set; } = TaskContext.Instance().Config.InferenceDevice switch
    {
        "CPU" => new SessionOptions(),
        "GPU_DirectML" => MakeSessionOptionWithDirectMlProvider(),
        _ => throw new InvalidEnumArgumentException("无效的推理设备")
    };

    public static SessionOptions MakeSessionOptionWithDirectMlProvider()
    {
        var sessionOptions = new SessionOptions();
        sessionOptions.AppendExecutionProvider_DML(0);
        return sessionOptions;
    }

    // /// <summary>
    // /// 重新加载每个推理器（测试没用，只能重启）
    // /// </summary>
    // public void RefreshInference()
    // {
    //     // 自动秘境每次都会NEW不用管
    //     // Yap、自动钓鱼
    //     GameTaskManager.RefreshTriggerConfigs();
    // }
}

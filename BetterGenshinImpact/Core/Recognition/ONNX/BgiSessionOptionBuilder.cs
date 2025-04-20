using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Model;
using Microsoft.ML.OnnxRuntime;
using System.ComponentModel;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

public class BgiSessionOptionBuilder : Singleton<BgiSessionOptionBuilder>
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
    public SessionOptions BuildWithRelativePath(string relativePath)
    {
        return Options;
    }
}

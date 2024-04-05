using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Model;
using Microsoft.ML.OnnxRuntime;
using System.ComponentModel;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

public class BgiSessionOption : Singleton<BgiSessionOption>
{
    public SessionOptions Options { get; set; } = TaskContext.Instance().Config.InferenceDevice switch
    {
        "CPU" => new SessionOptions(),
        "GPU" => SessionOptions.MakeSessionOptionWithCudaProvider(),
        _ => throw new InvalidEnumArgumentException("无效的推理设备")
    };
}

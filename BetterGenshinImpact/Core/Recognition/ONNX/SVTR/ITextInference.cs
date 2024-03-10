using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.ONNX.SVTR;

/// <summary>
///     文字识别推理(SVTR网络)
/// </summary>
public interface ITextInference
{
    public string Inference(Mat mat);
}

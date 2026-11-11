using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;

namespace BetterGenshinImpact.Core.Recognition.OCR;

/// <summary>
/// 内置的 PP-OCRv6 模型，是 OCR 可插拔能力的默认实现。其他版本、其他语言的模型由插件提供。
/// </summary>
/// <param name="sessions">绑定当前推理运行时的会话工厂。</param>
[PluggableImpl("ppocr_v6", "PP-OCRv6", IsDefault = true, Description = "主程序内置的模型。")]
public sealed class PpOcrV6Service(BgiOnnxSessionFactory sessions)
    : PaddleOcrService(sessions, PaddleOcrService.PaddleOcrModelType.V6);

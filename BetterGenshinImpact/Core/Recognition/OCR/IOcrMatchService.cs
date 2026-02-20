using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OCR;

/// <summary>
/// 基于 DP 模糊匹配的 OCR 服务接口，返回匹配置信度分数 (0~1)。
/// 独立于 IOcrService，仅由支持模糊匹配的引擎实现。
/// </summary>
public interface IOcrMatchService
{
    /// <summary>
    /// 使用检测器定位文字区域后，对每个区域进行模糊匹配，返回最高置信度 (0~1)。
    /// </summary>
    /// <param name="mat">输入图像（推荐三通道 BGR）</param>
    /// <param name="target">目标字符串</param>
    /// <returns>匹配置信度，0 表示完全不匹配，1 表示完全匹配</returns>
    double OcrMatch(Mat mat, string target);

    /// <summary>
    /// 不使用检测器，直接对整张图像进行模糊匹配，返回置信度 (0~1)。
    /// </summary>
    /// <param name="mat">输入图像（推荐三通道 BGR）</param>
    /// <param name="target">目标字符串</param>
    /// <returns>匹配置信度，0 表示完全不匹配，1 表示完全匹配</returns>
    double OcrMatchDirect(Mat mat, string target);
}

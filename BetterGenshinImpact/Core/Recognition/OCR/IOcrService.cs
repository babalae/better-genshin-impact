using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OCR;

/// <summary>
///     OCR 服务，也是可插拔能力“OCR 模型”：内置实现为 PP-OCRv6，插件可以提供其他模型，
///     用户在设置页“OCR 配置”中切换（插件页“功能选择”显示同一个选择）。
///     实时触发器和任务会同时调用，实现必须线程安全。实现的构造函数可以加载模型，实例在被切换后、进行中的调用结束时释放。
/// </summary>
[Pluggable("ocr", "OCR 模型", Description = "识别游戏界面文字使用的模型。")]
public interface IOcrService
{
    public string Ocr(Mat mat);

    public string OcrWithoutDetector(Mat mat);

    /// <summary>
    ///     不经过文字检测，识别一张已裁剪好的单行文字图像，返回文字和置信度。
    ///     调用方保留图像所有权。
    /// </summary>
    public OcrRecognizerResult OcrResultWithoutDetector(Mat mat);

    /// <summary>
    ///     不经过文字检测，按输入顺序批量识别多张已裁剪好的单行文字图像，返回每行的文字和置信度。
    ///     一次推理完成整批，调用方保留图像所有权。
    /// </summary>
    public OcrRecognizerResult[] OcrResultLinesWithoutDetector(Mat[] lines);

    public OcrResult OcrResult(Mat mat);
}

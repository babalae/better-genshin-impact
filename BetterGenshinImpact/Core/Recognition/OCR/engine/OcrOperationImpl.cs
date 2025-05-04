using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OCR.engine;

/// <summary>
/// 实现PPOCR的自定义操作，代码翻自python
/// </summary>
public class OcrOperationImpl
{
    /// <summary>
    ///     不支持 chw 之类的顺序
    ///     https://github.com/PaddlePaddle/PaddleOCR/blob/0ee4094988c568077bba35ddb239030ced1ff270/ppocr/data/imaug/operators.py#L62
    /// </summary>
    public static Mat NormalizeImageOperation(Mat data,
        float? scale, // scale float32
        float[]? mean, //mean
        float[]? std //std
    )
    {
        scale ??= 0.00392156862745f;
        mean ??= [0.485f, 0.456f, 0.406f];
        std ??= [0.229f, 0.224f, 0.225f];
        var result = new Mat();
        data.ConvertTo(result, MatType.CV_32FC3, (double)scale);
        Mat[] bgr = [];
        try
        {
            bgr = result.Split();
            for (var i = 0; i < bgr.Length; ++i)
                bgr[i].ConvertTo(bgr[i], MatType.CV_32FC1, 1 / std[i], (0.0 - mean[i]) / std[i]);

            Cv2.Merge(bgr, result);
        }
        finally
        {
            foreach (var channel in bgr) channel.Dispose();
        }

        return result;
    }
}
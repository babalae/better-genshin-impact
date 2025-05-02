using System.Buffers;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace BetterGenshinImpact.Core.Recognition.OCR;

public static class OcrUtils
{
    /// <summary>
    /// 预处理速度比unsafe快5倍以上,且吃的资源还少
    /// </summary>
    /// <param name="inputImage">输入图像，若不是灰度图会转换</param>
    /// <param name="tensorMemoryOwnser">tensor的Memory，用完需要释放</param>
    /// <returns></returns>
    public static Tensor<float> ToTensorYapDnn(Mat inputImage, out IMemoryOwner<float> tensorMemoryOwnser)
    {
        using var rt = new ResourcesTracker();
        Mat dst;
        // 221*32是个什么鬼
        if (inputImage.Channels() > 1)
        {
            var resize = rt.T(ResizeHelper.ResizeTo(inputImage, 221, 32));
            dst = rt.NewMat(resize.Size(), MatType.CV_8UC1, Scalar.Black);
            Cv2.CvtColor(resize, dst, ColorConversionCodes.BGR2GRAY);
        }
        else
        {
            dst = rt.T(ResizeHelper.ResizeTo(inputImage, 221, 32));
        }

        // 填充到 384x32
        var padded = rt.NewMat(new Size(384, 32), MatType.CV_8UC1, Scalar.Black);
        padded[new Rect(0, 0, 221, 32)] = dst;
        // 使用向量运算代替循环
        var blob = rt.T(CvDnn.BlobFromImage(padded, 1.0 / 255.0, default, default, false, false));
        var nCols = padded.Cols * padded.Rows;
        tensorMemoryOwnser = MemoryPool<float>.Shared.Rent(nCols);
        // 内存复制，如果直接传指针构建的话速度还不如多复制一份
        blob.AsSpan<float>().CopyTo(tensorMemoryOwnser.Memory.Span);
        return new DenseTensor<float>(tensorMemoryOwnser.Memory[..nCols], [1, 1, 32, 384]);
    }
}
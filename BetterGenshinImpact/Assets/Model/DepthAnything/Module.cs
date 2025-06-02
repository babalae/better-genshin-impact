using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace BetterGenshinImpact.Assets.Model.DepthAnything;

public static class Module
{
    private const int TargetSize = 518;
    private const bool ConvertToRgb = true;

    public static unsafe DenseTensor<float> ImageToTensor(Mat originalImg)
    {
        if (originalImg == null || originalImg.Empty())
        {
            throw new ArgumentNullException(nameof(originalImg), "Input image cannot be null or empty.");
        }

        Mat imgToProcess = originalImg;
        // 1. Resize (如果需要，可以创建新 Mat 以避免修改原始图像)
        // 如果原始图像尺寸已经是目标尺寸，可以跳过 resize
        if (originalImg.Width != TargetSize || originalImg.Height != TargetSize)
        {
            imgToProcess = new Mat(); // 创建新的 Mat 来存放 resize 结果
            Cv2.Resize(originalImg, imgToProcess, new Size(TargetSize, TargetSize));
        }


        // 2. 确保图像是3通道8位无符号整数类型
        if (imgToProcess.Channels() == 1) // 如果是灰度图，转为BGR
        {
            Mat colorMat = new Mat();
            Cv2.CvtColor(imgToProcess, colorMat, ColorConversionCodes.GRAY2BGR);
            if (imgToProcess != originalImg) imgToProcess.Dispose(); // 释放临时的 resize Mat
            imgToProcess = colorMat;
        }
        else if (imgToProcess.Channels() == 4) // 如果是BGRA，转为BGR
        {
            Mat bgrMat = new Mat();
            Cv2.CvtColor(imgToProcess, bgrMat, ColorConversionCodes.BGRA2BGR);
            if (imgToProcess != originalImg) imgToProcess.Dispose();
            imgToProcess = bgrMat;
        }
        else if (imgToProcess.Channels() != 3)
        {
            if (imgToProcess != originalImg && imgToProcess != null) imgToProcess.Dispose();
            throw new ArgumentException($"Input image must have 1, 3 or 4 channels, but got {imgToProcess.Channels()}.", nameof(originalImg));
        }

        if (imgToProcess.Depth() != MatType.CV_8U)
        {
             // 可以尝试转换: imgToProcess.ConvertTo(newMat, MatType.CV_8U);
            if (imgToProcess != originalImg && imgToProcess != null) imgToProcess.Dispose();
            throw new ArgumentException($"Input image must be of type CV_8U, but got {imgToProcess.Depth()}.", nameof(originalImg));
        }

        int height = imgToProcess.Rows; // 应为 TargetSize
        int width = imgToProcess.Cols;  // 应为 TargetSize

        var tensor = new DenseTensor<float>(new[] { 1, 3, height, width });
        
        // 确保 Mat 数据是连续的，如果不是，Clone() 可以使其连续
        if (!imgToProcess.IsContinuous()) {
            Mat temp = imgToProcess.Clone();
            if (imgToProcess != originalImg) imgToProcess.Dispose();
            imgToProcess = temp;
        }

        byte* ptr = (byte*)imgToProcess.DataPointer; // Mat.Data 已是 IntPtr，需要转为 byte*

        for (int i = 0; i < height; i++) // i for Height (rows)
        {
            for (int j = 0; j < width; j++)  // j for Width (cols)
            {
                int baseIndex = 3 * (width * i + j); // BGR offset for pixel (i, j)
                if (ConvertToRgb)
                {
                    tensor[0, 0, i, j] = ptr[baseIndex + 2] / 255f; // R
                    tensor[0, 1, i, j] = ptr[baseIndex + 1] / 255f; // G
                    tensor[0, 2, i, j] = ptr[baseIndex + 0] / 255f; // B
                }
                else // Keep BGR
                {
                    tensor[0, 0, i, j] = ptr[baseIndex + 0] / 255f; // B
                    tensor[0, 1, i, j] = ptr[baseIndex + 1] / 255f; // G
                    tensor[0, 2, i, j] = ptr[baseIndex + 2] / 255f; // R
                }
            }
        }

        // 如果 imgToProcess 是新创建的 Mat (不是原始的 originalImg)，则需要释放它
        if (imgToProcess != originalImg && imgToProcess != null)
        {
            imgToProcess.Dispose();
        }

        return tensor;
    }
    
    private static Mat<float> Infer(DenseTensor<float> input)
    {
        using var session = new InferenceSession("1.onnx");
        var inputTensor = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input", input),
        };
        using var results = session.Run(inputTensor);
        var output = results.First().AsTensor<float>().ToArray();
        var ret = new Mat<float>([1, 518, 518], output);
        return ret;
    }

    public static Mat<float> RunModel(Mat img)
    {
        var input = ImageToTensor(img);
        return Infer(input);
    }
}
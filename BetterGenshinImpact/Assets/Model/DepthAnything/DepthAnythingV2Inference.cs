using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Core.Config;
using OpenCvSharp; // For image processing
using Microsoft.ML.OnnxRuntime; // For ONNX model
using Microsoft.ML.OnnxRuntime.Tensors; // For DenseTensor

namespace BetterGenshinImpact.Assets.Model.DepthAnything;

public class DepthAnythingV2Inference
{
    private InferenceSession _session;
    private string _inputName;
    private int _modelInputWidth;
    private int _modelInputHeight;

    // ImageNet mean and std
    private static readonly float[] Mean = { 0.485f, 0.456f, 0.406f }; // RGB
    private static readonly float[] StdDev = { 0.229f, 0.224f, 0.225f }; // RGB

    public DepthAnythingV2Inference(string modelPath)
    {
        try
        {
            // Consider SessionOptions for GPU, etc.
            var options = new SessionOptions();
            // options.AppendExecutionProvider_CUDA(); // If GPU package is used and CUDA is available
            _session = new InferenceSession(modelPath, options);
            _inputName = _session.InputMetadata.Keys.First();
            
            // Get model input dimensions (assuming NCHW format [1, 3, H, W])
            var inputShape = _session.InputMetadata[_inputName].Dimensions;
            if (inputShape.Length != 4 || inputShape[0] != 1 || inputShape[1] != 3)
            {
                throw new ArgumentException("Model input shape is not the expected [1, 3, H, W]. Check your ONNX model.");
            }
            _modelInputHeight = inputShape[2]; // H
            _modelInputWidth = inputShape[3];  // W

        }
        catch (Exception ex)
        {
            throw;
        }
    }

    public unsafe Mat Infer(Mat imagePath, out Mat originalImageDisplay)
    {
        using (Mat originalImage = imagePath.Clone())
        {
            if (originalImage.Empty())
            {
                throw new System.IO.FileNotFoundException($"Image not found at {imagePath}");
            }
            originalImageDisplay = originalImage.Clone(); // For display later

            int originalHeight = originalImage.Rows;
            int originalWidth = originalImage.Cols;

            // 1. Preprocessing
            Mat resizedImage = new Mat();
            // Direct resize for simplicity, similar to Python example.
            // Official DA-V2 has more complex resize (keep aspect, pad to multiple of 14)
            Cv2.Resize(originalImage, resizedImage, new Size(_modelInputWidth, _modelInputHeight), 0, 0, InterpolationFlags.Cubic);

            Mat rgbImage = new Mat();
            Cv2.CvtColor(resizedImage, rgbImage, ColorConversionCodes.BGR2RGB);

            // Normalize and convert to NCHW float tensor
            var inputTensor = new DenseTensor<float>(new[] { 1, 3, _modelInputHeight, _modelInputWidth });
            
            // Ensure rgbImage is CV_8UC3
            if (rgbImage.Type() != MatType.CV_8UC3) {
                // Handle conversion or throw error
                rgbImage.ConvertTo(rgbImage, MatType.CV_8UC3);
            }

            byte* rgbPtr = (byte*)rgbImage.DataPointer;
            for (int y = 0; y < _modelInputHeight; y++)
            {
                for (int x = 0; x < _modelInputWidth; x++)
                {
                    int baseIdx = (y * _modelInputWidth + x) * 3; // 3 channels (RGB)
                    // Normalize: (pixel / 255.0 - mean) / std
                    inputTensor[0, 0, y, x] = (rgbPtr[baseIdx + 0] / 255.0f - Mean[0]) / StdDev[0]; // R
                    inputTensor[0, 1, y, x] = (rgbPtr[baseIdx + 1] / 255.0f - Mean[1]) / StdDev[1]; // G
                    inputTensor[0, 2, y, x] = (rgbPtr[baseIdx + 2] / 255.0f - Mean[2]) / StdDev[2]; // B
                }
            }
            
            resizedImage.Dispose();
            rgbImage.Dispose();

            // 2. Inference
            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, inputTensor) };
            
            using (var results = _session.Run(inputs)) // results is IDisposableReadOnlyCollection<DisposableNamedOnnxValue>
            {
                var outputTensor = results.First().AsTensor<float>(); // Assuming first output is the depth map

                // Output is typically [1, H, W]
                if (outputTensor.Rank != 3 || outputTensor.Dimensions[0] != 1)
                {
                     throw new InvalidOperationException("Unexpected output tensor shape.");
                }
                
                int outputHeight = outputTensor.Dimensions[1];
                int outputWidth = outputTensor.Dimensions[2];

                // 3. Postprocessing
                // Convert output tensor to Mat (single channel float)
                Mat rawDepthMat = new Mat(outputHeight, outputWidth, MatType.CV_32FC1);
                
                // It's more efficient to copy directly if possible, or loop
                // For simplicity, looping:
                float* rawDepthMatPtr = (float*)rawDepthMat.DataPointer;
                for (int y = 0; y < outputHeight; y++)
                {
                    for (int x = 0; x < outputWidth; x++)
                    {
                        rawDepthMatPtr[y * outputWidth + x] = outputTensor[0, y, x];
                    }
                }
                // A more direct way if memory layout matches:
                // System.Runtime.InteropServices.Marshal.Copy(outputTensor.Buffer.ToArray(), 0, rawDepthMat.Data, outputTensor.Length);
                // But DenseTensor.Buffer is not directly accessible like that. You'd copy outputTensor.ToArray() to a float[] then to Mat.

                Mat depthResized = new Mat();
                Cv2.Resize(rawDepthMat, depthResized, new Size(originalWidth, originalHeight), 0, 0, InterpolationFlags.Nearest);
                rawDepthMat.Dispose();

                return depthResized;
            }
        }
    }

    public static Mat Once(Mat input)
    {
        var DAV2 = new DepthAnythingV2Inference(Global.Absolute(@"Assets\Model\DepthAnything\1.onnx"));
        var ret = DAV2.Infer(input, out _);
        return ret;
    }
}

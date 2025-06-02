using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace BetterGenshinImpact.Assets.Model.DepthAnything;

public static class Module
{
    private static unsafe DenseTensor<float> ImageToTensor(Mat img)
    {
        Cv2.Resize(img, img, new Size(518, 518));
        var tensor = new DenseTensor<float>(new[] {1, 3, 518, 518});
        byte* ptr = img.DataPointer;
        for (int i = 0; i < 518; i++)
        {
            for (int j = 0; j < 518; j++)
            {
                tensor[0, 0, j, i] = ptr[3 * (518 * i + j)] / 255f;
                tensor[0, 1, j, i] = ptr[3 * (518 * i + j) + 1] / 255f;
                tensor[0, 2, j, i] = ptr[3 * (518 * i + j) + 2] / 255f;
            }
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
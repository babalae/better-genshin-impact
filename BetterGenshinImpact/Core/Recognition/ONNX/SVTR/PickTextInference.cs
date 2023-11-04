using BetterGenshinImpact.Core.Config;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BetterGenshinImpact.Core.Recognition.ONNX.SVTR;

/// <summary>
/// 来自于 Yap 的拾取文字识别
/// https://github.com/Alex-Beng/Yap
/// </summary>
public class PickTextInference : ITextInference
{
    private readonly InferenceSession _session;
    private readonly Dictionary<int, string> _wordDictionary;

    public PickTextInference()
    {
        var options = new SessionOptions();
        var modelPath = Global.Absolute("Assets\\Model\\Yap\\model_training.onnx");
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("Yap模型文件不存在", modelPath);
        }

        _session = new InferenceSession(modelPath, options);

        var wordJsonPath = Global.Absolute("Assets\\Model\\Yap\\index_2_word.json");
        if (!File.Exists(wordJsonPath))
        {
            throw new FileNotFoundException("Yap字典文件不存在", wordJsonPath);
        }

        var json = File.ReadAllText(wordJsonPath);
        _wordDictionary = JsonSerializer.Deserialize<Dictionary<int, string>>(json) ?? throw new Exception("index_2_word.json deserialize failed");
    }

    public string Inference(Mat mat)
    {
        var stopwatch = new Stopwatch();
        stopwatch.Start();
        // 将输入数据调整为 (1, 1, 32, 384) 形状的张量  
        var reshapedInputData = ToTensorUnsafe(mat);

        // 创建输入 NamedOnnxValue  
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("input", reshapedInputData) };

        // 运行模型推理  
        using var results = _session.Run(inputs);

        // 获取输出数据  
        var resultsArray = results.ToArray();
        var boxes = resultsArray[0].AsTensor<float>();

        var ans = "";
        var lastWord = "";
        for (var i = 0; i < boxes.Dimensions[0]; i++)
        {
            var maxIndex = 0;
            var maxValue = -1.0;
            for (var j = 0; j < _wordDictionary.Count; j++)
            {
                var value = boxes[i, 0, j];
                if (value > maxValue)
                {
                    maxValue = value;
                    maxIndex = j;
                }
            }

            var word = _wordDictionary[maxIndex];
            if (word != lastWord && word != "|")
            {
                ans += word;
            }

            lastWord = word;
        }

        stopwatch.Stop();
        Debug.WriteLine($"Yap模型识别 耗时{stopwatch.ElapsedMilliseconds}ms 结果: {ans}");
        return ans;
    }

    public static Tensor<float> ToTensorUnsafe(Mat src)
    {
        var channels = src.Channels();
        var nRows = src.Rows;
        var nCols = src.Cols * channels;
        if (src.IsContinuous())
        {
            nCols *= nRows;
            nRows = 1;
        }

        var inputData = new float[nCols];
        unsafe
        {
            for (var i = 0; i < nRows; i++)
            {
                var p = src.Ptr(i);
                var b = (byte*)p.ToPointer();
                for (var j = 0; j < nCols; j++)
                {
                    inputData[j] = b[j] / 255f;
                }
            }
        }

        return new DenseTensor<float>(new Memory<float>(inputData), new int[] { 1, 1, 32, 384 });
        ;
    }
}
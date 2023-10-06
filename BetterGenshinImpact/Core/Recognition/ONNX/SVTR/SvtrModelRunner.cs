using Microsoft.ML.OnnxRuntime;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using System.Text.Json;
using Range = OpenCvSharp.Range;
using Size = OpenCvSharp.Size;
using System.Drawing.Imaging;

namespace BetterGenshinImpact.Core.Recognition.ONNX.SVTR;

public class SvtrModelRunner
{
    private readonly InferenceSession _session;
    private readonly Dictionary<int, string> _wordDictionary;

    public SvtrModelRunner()
    {
        var options = new SessionOptions();
        _session = new InferenceSession(Global.Absolute("Config\\Model\\Yap\\model_training.onnx"), options);
        // 获取模型的输入节点名称  
        var inputName = _session.InputMetadata.Keys.First();
        Debug.WriteLine($"Input Name:{inputName}");

        var json = File.ReadAllText(Global.Absolute("Config\\Model\\Yap\\index_2_word.json"));
        _wordDictionary = JsonSerializer.Deserialize<Dictionary<int, string>>(json);
        if (_wordDictionary == null)
        {
            throw new Exception("index_2_word.json deserialize failed");
        }
    }

    public static Tensor<float> ToOnnxTensorUnsafe(Mat padded)
    {
        // Create the Tensor with the appropiate dimensions  for the NN
        Tensor<float> data = new DenseTensor<float>(new[] { 1, 1, padded.Height, padded.Width });


        // Rows = height, Cols = width
        var imageData = new float[padded.Height, padded.Width];
        for (int y = 0; y < padded.Rows; y++)
        {
            for (int x = 0; x < padded.Cols; x++)
            {
                byte b = padded.At<byte>(y, x);
                data[0, 0, y, x] = b / 255f;
                imageData[y, x] = b;
            }
        }

        Debug.WriteLine(imageData);
        return data;
    }

    public static Tensor<float> ToOnnxTensorUnsafe2(Mat padded)
    {
        var channels = padded.Channels();
        var nRows = padded.Rows;
        var nCols = padded.Cols * channels;
        if (padded.IsContinuous())
        {
            nCols *= nRows;
            nRows = 1;
        }

        var inputData = new float[nCols];
        unsafe
        {
            for (var i = 0; i < nRows; i++)
            {
                var p = padded.Ptr(i);
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

    public string RunInference(Mat padded)
    {
        // 将输入数据调整为 (1, 1, 32, 384) 形状的张量  
        //var reshapedInputData = new DenseTensor<float>(new Memory<float>(inputData), new int[] { 1, 1, 32, 384 });
        var reshapedInputData = ToOnnxTensorUnsafe2(padded);

        var y = reshapedInputData.Dimensions[2];
        var x = reshapedInputData.Dimensions[3];

        // 创建输入 NamedOnnxValue  
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("input", reshapedInputData) };

        // 运行模型推理  
        using var results = _session.Run(inputs);

        // 获取输出数据  
        var resultsArray = results.ToArray();
        Tensor<float> boxes = resultsArray[0].AsTensor<float>();

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
                ans = ans + word;
            }

            lastWord = word;
        }

        Debug.WriteLine("ans:" + ans);
        return ans;
    }

    public string RunInferenceMore(Mat mat)
    {
        Debug.Assert(mat.Depth() == MatType.CV_8UC1);
        //Cv2.ImShow("mat1", mat);
        mat = ResizeHelper.ResizeTo(mat, 221, 32);
        Cv2.ImWrite(Global.Absolute("resized.bmp"), mat);

        var padded = new Mat(new Size(384, 32), MatType.CV_8UC1, Scalar.Black);


        padded[new Rect(0, 0, mat.Width, mat.Height)] = mat;
        Cv2.ImWrite(Global.Absolute("padded.bmp"), padded);

        /*var channels = padded.Channels();
        var nRows = padded.Rows;
        var nCols = padded.Cols * channels;
        if (padded.IsContinuous())
        {
            nCols *= nRows;
            nRows = 1;
        }

        var inputData = new float[nCols];
        unsafe
        {
            for (var i = 0; i < nRows; i++)
            {
                var p = padded.Ptr(i);
                var b = (byte*)p.ToPointer();
                for (var j = 0; j < nCols; j++)
                {
                    inputData[j] = b[j] * 0.1f / 255;
                }
            }
        }*/


        //var imageData = new float[384, 32];
        // Rows = height, Cols = width
        //for (int y = 0; y < padded.Rows; y++)
        //{
        //    for (int x = 0; x < padded.Cols; x++)
        //    {
        //        imageData[x, y] = padded.At<byte>(x, y) * 0.1f / 255;
        //    }
        //}


        //var inputData = new float[384 * 32]; // 你的输入数据  
        //for (int y = 0; y < padded.Rows; y++)
        //{
        //    for (int x = 0; x < padded.Cols; x++)
        //    {
        //        inputData[y * 384 + x] = imageData[x, y];
        //    }
        //}

        return RunInference(padded);
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BetterGenshinImpact.Core.Config;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.ONNX.YOLO;

public class Predictor
{
    private readonly InferenceSession _session;
    private readonly string[] labels;

    public Predictor()
    {
        var options = new SessionOptions();
        var modelPath = Global.Absolute("Config\\Model\\Fish\\bgi_fish.onnx");
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("自动钓鱼模型文件不存在", modelPath);
        }

        _session = new InferenceSession(modelPath, options);


        var wordJsonPath = Global.Absolute("Config\\Model\\Fish\\label.json");
        if (!File.Exists(wordJsonPath))
        {
            throw new FileNotFoundException("自动钓鱼模型分类文件不存在", wordJsonPath);
        }

        var json = File.ReadAllText(wordJsonPath);
        labels = JsonSerializer.Deserialize<string[]>(json) ?? throw new Exception("label.json deserialize failed");
    }

   
}
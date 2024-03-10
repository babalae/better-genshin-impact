using BetterGenshinImpact.Core.Config;
using Microsoft.ML.OnnxRuntime;
using System;
using System.IO;
using System.Text.Json;

namespace BetterGenshinImpact.Core.Recognition.ONNX.YOLO;

[Obsolete]
public class Predictor
{
    private readonly InferenceSession _session;
    private readonly string[] labels;

    public Predictor()
    {
        var options = new SessionOptions();
        var modelPath = Global.Absolute("Assets\\Model\\Fish\\bgi_fish.onnx");
        if (!File.Exists(modelPath)) throw new FileNotFoundException("自动钓鱼模型文件不存在", modelPath);

        _session = new InferenceSession(modelPath, options);

        var wordJsonPath = Global.Absolute("Assets\\Model\\Fish\\label.json");
        if (!File.Exists(wordJsonPath)) throw new FileNotFoundException("自动钓鱼模型分类文件不存在", wordJsonPath);

        var json = File.ReadAllText(wordJsonPath);
        labels = JsonSerializer.Deserialize<string[]>(json) ?? throw new Exception("label.json deserialize failed");
    }
}

using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.GameUI;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.GetGridIcons;

/// <summary>
/// 获取Grid界面的物品图标
/// </summary>
public class GridIconsAccuracyTestTask : ISoloTask
{
    private readonly ILogger logger = App.GetLogger<GetGridIconsTask>();

    private CancellationToken ct;

    public string Name => "获取Grid界面物品图标独立任务";

    private readonly int? maxNumToTest;

    private readonly GridScreenName gridScreenName;

    public GridIconsAccuracyTestTask(GridScreenName gridScreenName, int? maxNumToTest = null)
    {
        this.gridScreenName = gridScreenName;
        this.maxNumToTest = maxNumToTest;
    }

    public async Task Start(CancellationToken ct)
    {
        this.ct = ct;

        #region 加载model
        using var session = new InferenceSession(@".\GameTask\GetGridIcons\gridIcon.onnx"); // todo 所有数据炼好后放到onnx统一存放的位置去

        var metadata = session.ModelMetadata;

        if (!metadata.CustomMetadataMap.TryGetValue("prefix_list", out string? prefixListJson))
        {
            logger.LogError("模型文件缺少prefix_list");
            return;
        }
        List<string> prefixList = System.Text.Json.JsonSerializer.Deserialize<List<string>>(prefixListJson) ?? throw new Exception();   // 不预测前缀
        #endregion
        #region 加载原型向量
        var allLines = File.ReadLines(@".\GameTask\GetGridIcons\训练集原型特征.csv").Skip(1);    // 跳过首行列名
        Dictionary<string, float[]> prototypes = new Dictionary<string, float[]>();
        foreach (string line in allLines)
        {
            var columns = line.Split(",").ToArray();
            var bytes = Convert.FromBase64String(columns[1]);
            int totalFloats = bytes.Length / sizeof(float);
            float[] flatData = new float[totalFloats];
            Buffer.BlockCopy(bytes, 0, flatData, 0, bytes.Length);
            prototypes.Add(columns[0], flatData);
        }
        #endregion

        using var ra0 = CaptureToRectArea();
        GridScreenParams gridParams = GridScreenParams.Templates[this.gridScreenName];
        Rect gridRoi = gridParams.GetRect(ra0);

        int count = this.maxNumToTest ?? int.MaxValue;
        double total_acc = 0.0;
        double total_count = 0;

        GridScreen gridScreen = new GridScreen(gridRoi, gridParams, this.logger, this.ct);
        await foreach (ImageRegion itemRegion in gridScreen)
        {
            itemRegion.Click();
            Task task1 = Delay(300, ct);
            var sadf = task1.Status;

            // 用模型推理得到的结果
            Task<(string, int)> task2 = Task.Run(() =>
            {
                return Infer(itemRegion.SrcMat, session, prototypes);
            }, ct);

            await Task.WhenAll(task1, task2);
            (string, int) result = task2.Result;

            // 用CV方法得到的结果
            using var ra1 = CaptureToRectArea();
            using ImageRegion nameRegion = ra1.DeriveCrop(new Rect((int)(ra1.Width * 0.682), (int)(ra1.Width * 0.0625), (int)(ra1.Width * 0.256), (int)(ra1.Width * 0.03125)));
            var ocrResult = OcrFactory.Paddle.OcrResult(nameRegion.SrcMat);
            string itemName = ocrResult.Text;

            using ImageRegion starRegion = ra1.DeriveCrop(new Rect((int)(ra1.Width * 0.682), (int)(ra1.Width * 0.1823), (int)(ra1.Width * 0.105), (int)(ra1.Width * 0.02345)));
            int itemStarNum = GetGridIconsTask.GetStars(starRegion.SrcMat);

            // 统计结果
            total_count++;
            if (itemName.Contains(result.Item1) && result.Item2 == itemStarNum)
            {
                total_acc++;
                logger.LogInformation($"{result.Item1}|{result.Item2}星，✔，正确率{total_acc / total_count:0.00}");
            }
            else
            {
                logger.LogInformation($"{result.Item1}|{result.Item2}星，应为：{itemName}|{itemStarNum}星，❌，正确率{total_acc / total_count:0.00}");
            }

            count--;
            if (count <= 0)
            {
                logger.LogInformation("检查次数已耗尽");
                break;
            }
        }
    }

    // todo: 单元测试
    public static (string, int) Infer(Mat mat, InferenceSession session, Dictionary<string, float[]> prototypes)
    {
        using Mat resized = mat.Resize(new Size(125, 153));
        using Mat rgb = resized.CvtColor(ColorConversionCodes.BGR2RGB);
        var tensor = new DenseTensor<float>(new[] { 1, 3, rgb.Height, rgb.Width });  // todo 放到BgiOnnxFactory那边去做个Mat->NamedOnnxValue的通用方法？
        for (int y = 0; y < rgb.Height; y++)
        {
            for (int x = 0; x < rgb.Width; x++)
            {
                tensor[0, 0, y, x] = rgb.At<Vec3b>(y, x)[0] / 255f;
                tensor[0, 1, y, x] = rgb.At<Vec3b>(y, x)[1] / 255f;
                tensor[0, 2, y, x] = rgb.At<Vec3b>(y, x)[2] / 255f;
            }
        }
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("input", tensor) };
        using var results = session.Run(inputs);
        float[] feature_matrix = results[0].AsEnumerable<float>().ToArray();
        string? pred_name = null;
        double? min2 = null;
        foreach (KeyValuePair<string, float[]> prototype in prototypes)
        {
            double distance2 = 0;
            for (int i = 0; i < 64; i++)
            {
                distance2 += Math.Pow(prototype.Value[i] - feature_matrix[i], 2f);
            }
            if (min2 == null || distance2 < min2)
            {
                pred_name = prototype.Key;
                min2 = distance2;
            }
        }
        if (pred_name == null || min2 == null)
        {
            throw new Exception("特征数据为空");
        }
        min2 = Math.Sqrt(min2.Value);
        int pred_star = results[2].AsEnumerable<float>().ToList().IndexOf(results[2].AsEnumerable<float>().Max());
        return (pred_name, pred_star);
    }
}
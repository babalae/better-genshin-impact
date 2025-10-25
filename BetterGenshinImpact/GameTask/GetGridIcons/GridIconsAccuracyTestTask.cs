using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.GameUI;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.View.Drawable;
using Fischless.WindowsInput;
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
    private readonly InputSimulator input = Simulation.SendInput;

    private CancellationToken ct;

    public string Name => "获取Grid界面物品图标独立任务";

    private readonly int? maxNumToTest;

    private readonly GridScreenName gridScreenName;

    public GridIconsAccuracyTestTask(GridScreenName gridScreenName, int? maxNumToTest = null)
    {
        this.gridScreenName = gridScreenName;
        this.maxNumToTest = maxNumToTest;
    }

    /// <summary>
    /// 加载图标识别模型
    /// </summary>
    /// <param name="prototypes">原型向量</param>
    /// <returns>推理会话</returns>
    /// <exception cref="Exception"></exception>
    public static InferenceSession LoadModel(out Dictionary<string, float[]> prototypes)
    {
        #region 加载model
        var session = new InferenceSession(Global.Absolute(@"Assets\Model\Item\gridIcon.onnx"));

        var metadata = session.ModelMetadata;

        if (!metadata.CustomMetadataMap.TryGetValue("prefix_list", out string? prefixListJson))
        {
            throw new Exception("模型文件缺少prefix_list");
        }
        List<string> prefixList = System.Text.Json.JsonSerializer.Deserialize<List<string>>(prefixListJson) ?? throw new Exception();   // 不预测前缀
        #endregion
        #region 加载原型向量
        var allLines = File.ReadLines(Global.Absolute(@"Assets\Model\Item\items.csv")).Skip(1);    // 跳过首行列名
        prototypes = new Dictionary<string, float[]>();
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
        return session;
    }

    public async Task Start(CancellationToken ct)
    {
        this.ct = ct;

        switch (this.gridScreenName)
        {
            case GridScreenName.Weapons:
            case GridScreenName.Artifacts:
            case GridScreenName.CharacterDevelopmentItems:
            case GridScreenName.Food:
            case GridScreenName.Materials:
            case GridScreenName.Gadget:
            case GridScreenName.Quest:
            case GridScreenName.PreciousItems:
            case GridScreenName.Furnishings:
                await new ReturnMainUiTask().Start(ct);
                await AutoArtifactSalvageTask.OpenInventory(this.gridScreenName, this.input, this.logger, this.ct);
                break;
            default:
                logger.LogInformation("{name}暂不支持自动打开，请提前手动打开界面", gridScreenName.GetDescription());
                break;
        }

        using InferenceSession session = LoadModel(out Dictionary<string, float[]> prototypes);

        int count = this.maxNumToTest ?? int.MaxValue;
        double total_acc = 0.0;
        double total_count = 0;

        GridScreen gridScreen = new GridScreen(GridParams.Templates[this.gridScreenName], this.logger, this.ct);
        gridScreen.OnAfterTurnToNewPage += GridScreen.DrawItemsAfterTurnToNewPage;
        gridScreen.OnBeforeScroll += () => VisionContext.Instance().DrawContent.ClearAll();
        try
        {
            await foreach (ImageRegion itemRegion in gridScreen)
            {
                itemRegion.Click();
                Task task1 = Delay(300, ct);

                // 用模型推理得到的结果
                Task<(string?, int)> task2 = Task.Run(() =>
                {
                    using Mat icon = itemRegion.SrcMat.GetGridIcon();
                    return Infer(icon, session, prototypes);
                }, ct);

                await Task.WhenAll(task1, task2);
                (string?, int) result = task2.Result;
                string? predName = result.Item1;
                int predStarNum = result.Item2;

                // 用CV方法得到的结果
                using var ra1 = CaptureToRectArea();
                using ImageRegion nameRegion = ra1.DeriveCrop(new Rect((int)(ra1.Width * 0.682), (int)(ra1.Width * 0.0625), (int)(ra1.Width * 0.256), (int)(ra1.Width * 0.03125)));
                var ocrResult = OcrFactory.Paddle.OcrResult(nameRegion.SrcMat);
                string itemName = ocrResult.Text;

                using ImageRegion starRegion = ra1.DeriveCrop(new Rect((int)(ra1.Width * 0.682), (int)(ra1.Width * 0.1823), (int)(ra1.Width * 0.105), (int)(ra1.Width * 0.02345)));
                int itemStarNum = GetGridIconsTask.GetStars(starRegion.SrcMat);

                // 统计结果
                total_count++;
                if (predName == null)
                {
                    logger.LogInformation($"模型没有识别，应为：{itemName}|{itemStarNum}星，❌，正确率{total_acc / total_count:0.00}");
                }
                else if (itemName.Contains(predName) && predStarNum == itemStarNum)
                {
                    total_acc++;
                    logger.LogInformation($"{predName}|{predStarNum}星，✔，正确率{total_acc / total_count:0.00}");
                }
                else
                {
                    logger.LogInformation($"{predName}|{predStarNum}星，应为：{itemName}|{itemStarNum}星，❌，正确率{total_acc / total_count:0.00}");
                }

                count--;
                if (count <= 0)
                {
                    logger.LogInformation("检查次数已耗尽");
                    break;
                }
            }
        }
        finally
        {
            VisionContext.Instance().DrawContent.ClearAll();
        }
    }

    /// <summary>
    /// 请自行裁剪缩放到125*125尺寸
    /// </summary>
    /// <param name="mat"></param>
    /// <param name="session"></param>
    /// <param name="prototypes"></param>
    /// <returns>(预测名称, 预测星级)</returns>
    /// <exception cref="Exception"></exception>
    public static (string?, int) Infer(Mat mat, InferenceSession session, Dictionary<string, float[]> prototypes)
    {
        if (mat.Size().Width != 125 || mat.Size().Height != 125)
        {
            throw new ArgumentOutOfRangeException(nameof(mat), "输入图像尺寸应为125*125");
        }
        using Mat rgb = mat.CvtColor(ColorConversionCodes.BGR2RGB);
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
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("input_image", tensor) };
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
                min2 = distance2;
                if (min2 < 10 * 10) // todo：负样本距离10直接读取模型
                {
                    pred_name = prototype.Key;
                }
            }
        }
        if (min2 == null)
        {
            throw new Exception("特征数据为空");
        }
        // min2 = Math.Sqrt(min2.Value);
        int pred_star = results[2].AsEnumerable<float>().ToList().IndexOf(results[2].AsEnumerable<float>().Max());
        return (pred_name, pred_star);
    }
}
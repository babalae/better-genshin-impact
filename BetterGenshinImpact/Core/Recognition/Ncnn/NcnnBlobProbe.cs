using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.Core.Recognition.Ncnn;

/// <summary>
///     设置页的只读诊断探测：按当前调优参数加载动态形状的 OCR det / rec 模型（det_dyn / rec_dyn），
///     报告真实的输入 / 输出 blob 名与一次真实推理的输出形状。
///     会话用完即销毁，不进入业务会话池，也不修改已保存设置。
/// </summary>
internal static class NcnnBlobProbe
{
    /// <summary>与 <c>NcnnRec</c> 的模型高度保持一致（pnnx 固定高度）。</summary>
    private const int RecHeight = 48;

    /// <summary>探测输入的代表性尺寸：动态模型接受任意合法形状，取常用值以缩短耗时。</summary>
    private const int ProbeDetSide = 320;
    private const int ProbeRecWidth = 320;

    public static string Describe(string? shimPath, string modelDir, NcnnTuningSettings detTuning,
        NcnnTuningSettings recTuning, int gpuIndex)
    {
        NcnnShim shim;
        try
        {
            shim = string.IsNullOrEmpty(shimPath) ? new NcnnShim() : new NcnnShim(shimPath);
        }
        catch (Exception e)
        {
            return "ncnn_shim.dll 加载失败：" + e.Message;
        }

        using (shim)
        {
            var builder = new StringBuilder();
            Append(builder, shim, modelDir, "OCR 检测", "det_dyn", ProbeDetSide, ProbeDetSide, detTuning, gpuIndex);
            Append(builder, shim, modelDir, "OCR 识别", "rec_dyn", ProbeRecWidth, RecHeight, recTuning, gpuIndex);
            return builder.ToString().TrimEnd();
        }
    }

    private static void Append(StringBuilder builder, NcnnShim shim, string modelDir, string label, string stem,
        int width, int height, NcnnTuningSettings tuning, int gpuIndex)
    {
        var paramPath = Path.Combine(modelDir, stem + ".param");
        var modelPath = Path.Combine(modelDir, stem + ".bin");
        if (!File.Exists(paramPath) || !File.Exists(modelPath))
        {
            builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{label}：产物缺失（{stem}）"));
            return;
        }

        nint handle;
        try
        {
            handle = shim.Create(tuning.ToNetOptions(paramPath, modelPath, gpuIndex));
        }
        catch (Exception e)
        {
            builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{label}：加载失败（{stem}）{e.Message}"));
            return;
        }

        if (handle == nint.Zero)
        {
            builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{label}：加载失败（{stem}），shim 返回空会话"));
            return;
        }

        try
        {
            var inputs = shim.QueryInputBlobs(handle);
            var outputs = shim.QueryOutputBlobs(handle);
            builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{label}（{stem}）"));
            builder.AppendLine("  输入 blob：" + Join(inputs) +
                string.Create(CultureInfo.InvariantCulture, $" · 形状 3×{height}×{width}"));
            builder.AppendLine("  输出 blob：" + Join(outputs));
            if (inputs.Length == 0 || outputs.Length == 0) return;

            // 零输入只为拿形状：数值结果无意义，模型固定形状保证不会因内容改变维度。
            var zeros = new float[3 * width * height];
            var results = shim.InferEx(handle, [NcnnInput.FromFloat(inputs[0], zeros, width, height, 3)], [outputs[0]]);
            if (results.Count == 0)
            {
                builder.AppendLine("  实际输出：未返回张量");
                return;
            }

            var info = results[0].Info;
            builder.AppendLine("  实际输出形状：" + info.Describe(results[0].Data.Length));
        }
        catch (Exception e)
        {
            builder.AppendLine("  探测失败：" + e.Message);
        }
        finally
        {
            shim.Destroy(handle);
        }
    }

    private static string Join(IReadOnlyList<string> names)
        => names.Count == 0 ? "（无）" : string.Join(" / ", names);
}

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

/// <summary>保存领奖页已有结果及异常卡片，不重复推理或改变库存。</summary>
internal static class TrainingGuideRewardDiagnostics
{
    public static void RecordComparison(Mat screen, int page,
        IReadOnlyList<(string Name, int Count)> previous, IReadOnlyList<(string Name, int Count)> current,
        string reason)
    {
        if (!TrainingGuideDiagnostics.Enabled) return;
        var id = $"reward-compare-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}";
        TrainingGuideDiagnostics.LogReward(id,
            $"原因={reason}；前页={page - 1}；当前页={page}；" +
            $"前页结果=[{string.Join(" | ", previous.Select(r => $"{r.Name}={r.Count}"))}]；" +
            $"当前结果=[{string.Join(" | ", current.Select(r => $"{r.Name}={r.Count}"))}]");
        TrainingGuideDiagnostics.Save(screen, "comparison", id);
    }

    public static void Record(Mat screen, IReadOnlyList<Rect> rects,
        IReadOnlyList<(string? Name, int Count)> results, int page, ILogger logger,
        Func<string, bool>? allowUnreliableCount = null)
    {
        if (!TrainingGuideDiagnostics.Enabled) return;
        var id = $"reward-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}-page-{page}";
        try
        {
            TrainingGuideDiagnostics.Save(screen, "screen", id);
            TrainingGuideDiagnostics.LogReward(id,
                $"页码={page}；定位卡片={rects.Count}；识别结果={results.Count}；数量=-1表示未识别，名称为空时原流程未执行数量OCR");
            using var band = new Mat(screen, new Rect(220, 444, 1480, 220));
            for (var i = 0; i < rects.Count; i++)
            {
                try
                {
                    var result = i < results.Count ? results[i] : (Name: (string?)null, Count: -1);
                    TrainingGuideDiagnostics.LogReward(id,
                        $"位置={i + 1}；区域={rects[i]}（相对奖励条）；实际名称={result.Name ?? "未识别"}；实际数量={result.Count}；严格校验通过={!string.IsNullOrEmpty(result.Name) && ((result.Name != null && allowUnreliableCount?.Invoke(result.Name) == true) || result.Count > 0)}");
                    if (string.IsNullOrEmpty(result.Name) ||
                        (!(result.Name != null && allowUnreliableCount?.Invoke(result.Name) == true) && result.Count <= 0))
                    {
                        using var card = new Mat(band, rects[i]);
                        TrainingGuideDiagnostics.Save(card, $"card-{i + 1}", id);
                    }
                }
                catch (Exception e)
                {
                    TrainingGuideDiagnostics.LogReward(id,
                        $"位置={i + 1}；诊断失败={e.GetBaseException().Message}");
                }
            }
        }
        catch (Exception e)
        {
            logger.LogWarning("培养领奖诊断失败 [{Id}]：{Message}", id, e.GetBaseException().Message);
        }
    }
}

// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Ocr;

/// <summary>OcrExecutionProfile：原神自动配音的业务类型。</summary>
public static class OcrExecutionProfile
{
    /// <summary>异步读取并校验 OCR 执行诊断或图像快照。</summary>
    public static async Task<IReadOnlyDictionary<string,int>> ReadAsync(string path,CancellationToken cancellationToken)
    {
        try
        {
            await using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,4096,true);
            using var json=await JsonDocument.ParseAsync(file,cancellationToken:cancellationToken).ConfigureAwait(false);
            var counts=new Dictionary<string,int>(StringComparer.Ordinal);
            foreach (var item in json.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("cat",out var category) || category.GetString()!="Node"
                    || !item.TryGetProperty("name",out var name) || !(name.GetString()?.EndsWith("_kernel_time",StringComparison.Ordinal)??false)
                    || !item.TryGetProperty("args",out var args) || !args.TryGetProperty("provider",out var provider)) continue;
                if (provider.GetString() is {Length:>0} value) counts[value]=counts.GetValueOrDefault(value)+1;
            }
            if (counts.GetValueOrDefault("DmlExecutionProvider")==0)
                throw new OcrException(OcrErrorCode.Inference,"OCR profile 未记录执行过的 DirectML 算子，拒绝将全 CPU 执行报告为 DirectML。");
            return counts;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        { throw new OcrException(OcrErrorCode.Inference,$"无法验证 OCR provider profile：{exception.Message}",exception); }
    }
}

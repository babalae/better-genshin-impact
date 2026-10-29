// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Ocr;

/// <summary>CtcDecoder：原神自动配音的业务类型。</summary>
public static class CtcDecoder
{
    /// <summary>按模型字典执行 CTC 去重、空白过滤和置信度汇总。</summary>
    public static OcrText Decode(ReadOnlySpan<float> probabilities, int steps, IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 2 || steps < 0 || (long)steps * tokens.Count != probabilities.Length)
            throw new OcrException(OcrErrorCode.Inference, "OCR 输出形状与字典不匹配。");
        var text = new StringBuilder();
        var previous = -1;
        double confidence = 0;
        var count = 0;
        for (var step = 0; step < steps; step++)
        {
            var row = probabilities.Slice(step * tokens.Count, tokens.Count);
            var best = 0;
            for (var index = 0; index < row.Length; index++)
            {
                if (!float.IsFinite(row[index]) || row[index] < 0 || row[index] > 1)
                    throw new OcrException(OcrErrorCode.Inference, "OCR 输出含无效概率。");
                if (row[index] > row[best]) best = index;
            }
            if (best != 0 && best != previous)
            {
                text.Append(tokens[best]);
                confidence += row[best];
                count++;
            }
            previous = best;
        }
        return new(text.ToString(), count == 0 ? 0 : confidence / count);
    }
}

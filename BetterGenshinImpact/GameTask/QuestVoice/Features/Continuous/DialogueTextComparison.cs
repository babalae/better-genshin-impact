using System;
using System.Text;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Continuous;

/// <summary>纯文本比较结果；待确认不等于同句，必须由当前会话的新 OCR 证据决定。</summary>
public enum DialogueTextRelation
{
    /// <summary>正文相同，或仅存在受限的高可信格式差异。</summary>
    Equivalent,
    /// <summary>疑似单字误识或边缘噪声，保留已接受正文并尽快请求确认。</summary>
    NeedsConfirmation,
    /// <summary>明确不同或不满足保守容错规则，由会话及时接受新对白。</summary>
    Different
}

/// <summary>对白 OCR 的无状态比较；比较键独立于原始文本，绝不改写送入 TTS 的正文。</summary>
public static class DialogueTextComparison
{
    /// <summary>仅中文长句的单汉字编辑需要确认；短句改变及时交给新语音。</summary>
    private const int MinimumConfirmationHanCharacters = 8;
    /// <summary>逗号缺失及单个边缘拉丁字符的中文正文最短长度。</summary>
    private const int MinimumNoiseHanCharacters = 6;

    /// <summary>规范宽度、常见同义标点及中文排版空白；保留内部标点、英文分词和全部正文字符。</summary>
    public static string CreateComparisonKey(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return string.Empty;
        var normalized = rawText.Normalize(NormalizationForm.FormKC);
        var key = new StringBuilder(normalized.Length);
        var pendingSpace = false;
        foreach (var value in normalized)
        {
            if (char.IsWhiteSpace(value)) { pendingSpace = true; continue; }
            var current = value switch
            {
                '。' => '.',
                '“' or '”' => '"',
                '‘' or '’' => '\'',
                _ => value
            };
            // 仅在两侧均为非汉字词字符时保留分隔，避免把 Model A/ModelA、1 2/12 合并。
            if (pendingSpace && key.Length > 0 && IsSeparatedWordCharacter(key[key.Length - 1]) && IsSeparatedWordCharacter(current))
                key.Append(' ');
            key.Append(current);
            pendingSpace = false;
        }
        return key.ToString();
    }

    /// <summary>比较原始文本；Equivalent 可复用现有任务，NeedsConfirmation 不得直接提交或删字。</summary>
    public static DialogueTextRelation Compare(string? acceptedRawText, string? incomingRawText)
    {
        var accepted = CreateComparisonKey(acceptedRawText);
        var incoming = CreateComparisonKey(incomingRawText);
        if (accepted == incoming) return DialogueTextRelation.Equivalent;
        if (accepted.Length == 0 || incoming.Length == 0) return DialogueTextRelation.Different;

        var acceptedBody = GetSentenceBody(accepted);
        var incomingBody = GetSentenceBody(incoming);
        // 允许一侧缺失句末标点，但已有的 ?/! 等不同句末语气不作同义处理。
        // 省略号可能被 OCR 识别为 …/…… 或不同数量的句点，按同类终止符处理。
        var acceptedEnding = accepted.AsSpan(acceptedBody.Length);
        var incomingEnding = incoming.AsSpan(incomingBody.Length);
        if (!acceptedEnding.IsEmpty && !incomingEnding.IsEmpty && !acceptedEnding.SequenceEqual(incomingEnding)
            && !(IsDotRun(acceptedEnding) && IsDotRun(incomingEnding)))
            return DialogueTextRelation.Different;
        if (acceptedBody.Length == 0 || incomingBody.Length == 0) return DialogueTextRelation.Different;
        if (acceptedBody == incomingBody) return DialogueTextRelation.Equivalent;
        if (IsSafeSingleCommaDifference(acceptedBody, incomingBody)) return DialogueTextRelation.Equivalent;
        if (IsSingleEdgeNoiseDifference(acceptedBody, incomingBody) || IsSingleHanEdit(acceptedBody, incomingBody))
            return DialogueTextRelation.NeedsConfirmation;
        return DialogueTextRelation.Different;
    }

    /// <summary>首句存在中文正文边缘的单个 K/M/H 时请求独立证据；不将其从 raw 或 key 中删除。</summary>
    public static bool RequiresInitialConfirmation(string? rawText)
    {
        var body = GetSentenceBody(CreateComparisonKey(rawText));
        return body.Length > 1 &&
            ((IsEdgeNoise(body[0]) && IsChineseNoiseBody(body.AsSpan(1))) ||
             (IsEdgeNoise(body[^1]) && IsChineseNoiseBody(body.AsSpan(0, body.Length - 1))));
    }

    /// <summary>只拆出可缺失的句末句号、问号、叹号；内部标点及尾逗号仍属于正文。</summary>
    private static string GetSentenceBody(string key)
    {
        var length = key.Length;
        while (length > 0 && key[length - 1] is '.' or '?' or '!') length--;
        return length == key.Length ? key : key[..length];
    }

    /// <summary>仅容忍语气助词后的单逗号增删；两侧至少三汉字且无否定字，仍是保守启发式。</summary>
    private static bool IsSafeSingleCommaDifference(string left, string right)
    {
        if (left.Length < right.Length) (left, right) = (right, left);
        if (left.Length != right.Length + 1 || right.Length < MinimumNoiseHanCharacters) return false;
        var comma = left.IndexOf(',');
        if (comma < 3 || comma > left.Length - 4 || left.LastIndexOf(',') != comma) return false;
        // 助词自身保留停顿线索，排除“我的爸爸，姐姐在这里”等名词归属切分。
        if (left[comma - 1] is not ('啊' or '呀' or '啦' or '哦' or '呢' or '吧' or '哟' or '呐')) return false;
        foreach (var value in right)
        {
            // 中文逗号仍可能影响语义；否定、英文、数字、其他标点不进入直接等价启发式。
            if (!IsHan(value) || value is '不' or '没' or '别' or '勿' or '莫' or '无' or '非' or '未') return false;
        }
        return left.AsSpan(0, comma).SequenceEqual(right.AsSpan(0, comma)) &&
               left.AsSpan(comma + 1).SequenceEqual(right.AsSpan(comma));
    }

    /// <summary>单边恰好多出一个 KMH 仅分类为待确认，真正的拉丁字符不被当作等价正文。</summary>
    private static bool IsSingleEdgeNoiseDifference(string left, string right)
    {
        if (left.Length < right.Length) (left, right) = (right, left);
        if (left.Length != right.Length + 1 || !IsChineseNoiseBody(right.AsSpan())) return false;
        return (IsEdgeNoise(left[0]) && left.AsSpan(1).SequenceEqual(right.AsSpan())) ||
               (IsEdgeNoise(left[^1]) && left.AsSpan(0, right.Length).SequenceEqual(right.AsSpan()));
    }

    /// <summary>边缘字符后的正文只能包含汉字与标点，避免把型号、数字、英文名当作噪声。</summary>
    private static bool IsChineseNoiseBody(ReadOnlySpan<char> body)
    {
        var hanCharacters = 0;
        foreach (var value in body)
        {
            if (IsHan(value)) hanCharacters++;
            else if (!char.IsPunctuation(value)) return false;
        }
        return hanCharacters >= MinimumNoiseHanCharacters;
    }

    /// <summary>对较长中文句仅识别一次汉字替换、插入或删除；不使用任意编辑距离直接合并。</summary>
    private static bool IsSingleHanEdit(string left, string right)
    {
        if (Math.Abs(left.Length - right.Length) > 1 ||
            CountHanCharacters(left) < MinimumConfirmationHanCharacters ||
            CountHanCharacters(right) < MinimumConfirmationHanCharacters) return false;
        var common = 0;
        while (common < left.Length && common < right.Length && left[common] == right[common]) common++;
        if (left.Length == right.Length)
            return common < left.Length && IsHan(left[common]) && IsHan(right[common]) &&
                   left.AsSpan(common + 1).SequenceEqual(right.AsSpan(common + 1));
        if (left.Length < right.Length) (left, right) = (right, left);
        return common < left.Length && IsHan(left[common]) &&
               left.AsSpan(common + 1).SequenceEqual(right.AsSpan(common));
    }

    /// <summary>统计汉字数量，不能把长英文或数字串误归入中文漏字确认。</summary>
    private static int CountHanCharacters(string text)
    {
        var count = 0;
        foreach (var value in text) if (IsHan(value)) count++;
        return count;
    }

    /// <summary>覆盖常用和扩展 A 汉字；罕用代理对不参与模糊容错，按明确差异处理。</summary>
    private static bool IsHan(char value) => value is >= '\u3400' and <= '\u4dbf' or >= '\u4e00' and <= '\u9fff';

    /// <summary>全为句点说明这是省略号/句末点号的数量差异，不构成语义变化。</summary>
    private static bool IsDotRun(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty) return false;
        foreach (var item in value) if (item != '.') return false;
        return true;
    }

    /// <summary>仅研究记录中出现的三个大写边缘字符需要补充证据。</summary>
    private static bool IsEdgeNoise(char value) => value is 'K' or 'M' or 'H';

    /// <summary>非汉字的字母或数字间保留空白，以保留英文分词及数字分组。</summary>
    private static bool IsSeparatedWordCharacter(char value) => char.IsLetterOrDigit(value) && !IsHan(value);
}
using System;

namespace BetterGenshinImpact.GameTask.AutoCombo.ComboBuild;

/// <summary>
/// 自动连招建树可用的 LLM 服务商。
///
/// 取值会写入配置 JSON，属于持久化数据，已有取值不可改动，只能新增。
/// </summary>
public static class AutoComboLlmProvider
{
    /// <summary>
    /// OpenAI 兼容端点（/chat/completions）。可指向任意兼容服务，也可指向本机模型。
    /// </summary>
    public const string OpenAiCompatible = "openai";

    /// <summary>
    /// Anthropic 官方 Messages API。
    /// </summary>
    public const string Anthropic = "anthropic";

    /// <summary>
    /// 未配置、或配置了当前版本不认识的取值时使用的服务商。
    /// 用 OpenAI 兼容端点作为默认值，可让升级前只填了服务地址的旧配置继续按原行为工作。
    /// </summary>
    public const string Default = OpenAiCompatible;

    /// <summary>
    /// 把配置中的取值收敛到受支持的集合，未知取值一律按默认服务商处理。
    /// </summary>
    public static string Normalize(string? provider)
    {
        return Anthropic.Equals(provider, StringComparison.OrdinalIgnoreCase) ? Anthropic : Default;
    }
}

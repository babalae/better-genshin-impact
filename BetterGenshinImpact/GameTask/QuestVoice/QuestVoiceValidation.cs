using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;

namespace BetterGenshinImpact.GameTask.QuestVoice;

/// <summary>配置检查结果；文件就绪不等于已经验证推理、合成或输出设备。</summary>
public sealed record QuestVoiceValidation(bool IsValid, string Detail, string RoutingSummary, AppSettings? Settings);

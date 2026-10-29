namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Diagnostics;

/// <summary>集中管理发布详细程度；当前测试版保留阶段与正文，生命周期和错误始终可见。</summary>
public sealed record QuestVoiceOverlayOptions(bool ShowStages = true, bool ShowDialogue = true);

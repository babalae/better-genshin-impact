// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
namespace BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;

/// <summary>OCR 接受阈值；模型固定使用 BetterGI 内置的 PP-OCRv6 识别模型。</summary>
public sealed record OcrSettings(double NameConfidence = .9, double DialogueConfidence = .8);

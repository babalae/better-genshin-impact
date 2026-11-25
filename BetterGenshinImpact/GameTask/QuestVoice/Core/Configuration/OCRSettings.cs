// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
namespace BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;

/// <summary>OCR 接受阈值；模型和设备跟随 BetterGI 全局 OCR 设置，阈值按 PP-OCRv6 的置信度标定。</summary>
public sealed record OcrSettings(double NameConfidence = .9, double DialogueConfidence = .8);

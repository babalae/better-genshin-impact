namespace BetterGenshinImpact.GameTask.AutoSkip.Audio;

/// <summary>
/// 人声检测的判定阈值与时长。检测状态机与诊断面板共用同一份定义，避免两处数值漂移。
/// </summary>
internal static class DialogueOptionVoiceThresholds
{
    /// <summary>确认说话的概率阈值</summary>
    public const float SpeechProbability = 0.60f;

    /// <summary>疑似说话（弱语音）的概率阈值，用于重置静音计时</summary>
    public const float MaybeSpeechProbability = 0.35f;

    /// <summary>确认说话所需的连续时长（毫秒）</summary>
    public const int SpeechRiseMilliseconds = 160;

    /// <summary>确认说话后，判定语音结束所需的连续静音时长（毫秒）</summary>
    public const int SilenceMilliseconds = 1500;
}

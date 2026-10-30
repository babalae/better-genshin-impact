// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using System.Text.Json;
namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Continuous;
/// <summary>ContinuousSettingsLoader：原神自动配音的业务类型。</summary>
public sealed class ContinuousSettingsLoader
{
    /// <summary>校验配置的边界、引用和参数关系。</summary>
    public static void Validate(ContinuousSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.StableMilliseconds is < 1 or > 60000 || settings.MinimumStableFrames is < 2 or > 1000
            || settings.MaximumObservationGapMilliseconds <= settings.StableMilliseconds
            || settings.MaximumObservationGapMilliseconds > 300000
            || settings.LeaveDialogueMilliseconds is < 1 or > 300000
            || settings.LeaveDialogueMilliseconds > settings.MaximumObservationGapMilliseconds
            || settings.RecheckMilliseconds < settings.StableMilliseconds || settings.RecheckMilliseconds > 300000
            || settings.SignatureColumns is < 8 or > 1024 || settings.SignatureRows is < 4 or > 128
            || settings.SignatureColumns * settings.SignatureRows > 65536
            || !double.IsFinite(settings.SignatureDifferenceThreshold)
            || settings.SignatureDifferenceThreshold is <= 0 or > 1)
            throw new ArgumentException("Invalid continuous timing or signature bounds/relationships.", nameof(settings));
    }
}

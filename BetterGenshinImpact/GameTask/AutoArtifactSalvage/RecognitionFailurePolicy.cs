using System.ComponentModel;

namespace BetterGenshinImpact.GameTask.AutoArtifactSalvage
{
    public enum RecognitionFailurePolicy
    {
        [Description("跳过")]
        Skip,
        [Description("终止")]
        Abort
    }
}

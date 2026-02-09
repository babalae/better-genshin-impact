using System.ComponentModel;
using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.GameTask.AutoArtifactSalvage
{
    public enum RecognitionFailurePolicy
    {
        [Description(Lang.S["GameTask_10380_92636e"])]
        Skip,
        [Description(Lang.S["GameTask_10379_ff6c6a"])]
        Abort
    }
}

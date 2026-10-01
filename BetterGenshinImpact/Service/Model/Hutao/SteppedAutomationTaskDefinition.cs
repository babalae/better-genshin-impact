using System.Collections.Generic;

namespace BetterGenshinImpact.Service.Model.Hutao;

// 与 Snap.Hutao.Remastered 的 SteppedAutomationTaskDefinition 保持 JSON 形状一致。
internal sealed class SteppedAutomationTaskDefinition : AutomationTaskDefinition
{
    public required List<AutomationTaskStepDefinition> Steps { get; set; }

    public required int CurrentStepIndex { get; set; }

    public required bool IsIndeterminate { get; set; }
}

using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.SoloTaskSettings;

/// <summary>体力计划子任务设置弹窗入口；由视图层实现，调用方只接收确认后的新参数。</summary>
public interface IResinPlanSettingsDialogs
{
    /// <summary>复制并编辑一个子任务的参数；由体力计划决定的字段不显示且保持原值。</summary>
    /// <param name="spec">子任务设置说明。</param>
    /// <param name="parameters">子任务当前参数。</param>
    /// <returns>确认后的完整参数；取消时返回 null。</returns>
    JObject? EditSubTask(ResinPlanSubTaskSpec spec, JObject parameters);
}

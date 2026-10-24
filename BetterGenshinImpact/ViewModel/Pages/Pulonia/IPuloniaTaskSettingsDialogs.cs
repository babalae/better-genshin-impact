using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 高级设置窗口打开公共设置与采集 CD 表单的入口；由视图层实现并注入，视图模型不直接创建窗口或读取全局配置。
/// </summary>
public interface IPuloniaTaskSettingsDialogs
{
    /// <summary>
    /// 以配置组表单编辑一个任务类型的公共设置（行走、战斗、食物或 Shell）。
    /// </summary>
    /// <param name="taskType">pathing、javascript 或 shell。</param>
    /// <param name="isGroup">编辑对象是否为分组，决定表单说明。</param>
    /// <param name="effective">当前有效参数，表单以它为初始值。</param>
    /// <returns>确认时返回表单中的完整公共参数或恢复继承请求；取消返回 null。</returns>
    PuloniaTaskSettingsDialogResult? EditCommonSettings(string taskType, bool isGroup, JObject effective);

    /// <summary>
    /// 打开采集 CD 表单；草稿由调用方建立，确认后由调用方读取其中的值。
    /// </summary>
    /// <param name="draft">采集 CD 草稿。</param>
    /// <param name="isLeaf">编辑对象是否为单个地图追踪任务，决定表单说明。</param>
    /// <returns>确认时返回结果（Value 为空）；取消返回 null。</returns>
    PuloniaTaskSettingsDialogResult? EditCollectionCooldown(PuloniaCollectionCooldownViewModel draft, bool isLeaf);
}

/// <summary>设置表单的确认结果。</summary>
/// <param name="Value">编辑后的参数；恢复继承或由草稿承载结果时为空。</param>
/// <param name="RestoreInheritance">用户是否选择恢复继承。</param>
public sealed record PuloniaTaskSettingsDialogResult(JObject? Value, bool RestoreInheritance);

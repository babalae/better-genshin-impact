using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 共享设置、设置方案、路线 CD、运行预览与 JS 脚本设置窗口的入口；由视图层实现并注入，视图模型不直接创建窗口或读取全局配置。
/// </summary>
public interface IPuloniaTaskSettingsDialogs
{
    /// <summary>
    /// 打开共享设置窗口；修改即时生效，关闭窗口后释放编辑器。
    /// </summary>
    /// <param name="editor">节点模式的共享设置编辑器。</param>
    void EditSharedSettings(PuloniaSharedSettingsEditorViewModel editor);

    /// <summary>
    /// 打开设置方案管理窗口，并选中指定方案。
    /// </summary>
    /// <param name="session">编辑会话。</param>
    /// <param name="selectProfileId">要选中的方案；为空时选中第一个。</param>
    void ManageProfiles(PuloniaPlanEditingSession session, string? selectProfileId);

    /// <summary>
    /// 打开路线 CD 表单；草稿由调用方建立，确认后由调用方读取其中的值。
    /// </summary>
    /// <param name="draft">路线 CD 草稿。</param>
    /// <param name="isLeaf">编辑对象是否为单个地图追踪任务，决定表单说明。</param>
    /// <returns>确认时返回结果（Value 为空）；取消返回 null。</returns>
    PuloniaTaskSettingsDialogResult? EditCollectionCooldown(PuloniaCollectionCooldownViewModel draft, bool isLeaf);

    /// <summary>
    /// 打开运行预览窗口；窗口关闭后返回用户是否选择了开始执行。
    /// </summary>
    /// <param name="viewModel">运行预览。</param>
    /// <returns>选择“开始执行”时为 true。</returns>
    bool ShowRunPreview(PuloniaTaskRunPreviewViewModel viewModel);

    /// <summary>
    /// 打开 JS 脚本自定义设置表单；字段由调用方按项目 settings_ui 建立。
    /// </summary>
    /// <param name="viewModel">脚本设置草稿。</param>
    /// <returns>确认时返回完整的 settings 对象；取消返回 null。</returns>
    JObject? EditJavaScriptSettings(PuloniaJsScriptSettingsDialogViewModel viewModel);
}

/// <summary>设置表单的确认结果。</summary>
/// <param name="Value">编辑后的参数；恢复继承或由草稿承载结果时为空。</param>
/// <param name="RestoreInheritance">用户是否选择恢复继承。</param>
public sealed record PuloniaTaskSettingsDialogResult(JObject? Value, bool RestoreInheritance);

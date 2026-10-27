using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>采集 CD 是一项对象值的共享设置：草稿读取与整体覆盖复用文档撤销和自动保存边界，表单窗口由设置页打开。</summary>
public partial class PuloniaTaskPlanDocumentViewModel
{
    /// <summary>从节点处的有效共享设置创建独立草稿；目录引用组预览只解释该组规则。</summary>
    internal PuloniaCollectionCooldownViewModel CreateCollectionCooldownDraft(PuloniaTaskNodeViewModel node)
    {
        var resolution = ResolveNodeSettings(node);
        var key = PuloniaSettingCatalog.CollectionCooldownKey;
        var draft = new PuloniaCollectionCooldownViewModel();
        draft.Load(resolution.Values[key], DescribeSettingSource(resolution.Sources[key], node));
        draft.SetResource(node.Model.Path ?? node.Model.Source?.Path,
            node.Model.Resource ?? node.Model.Source?.Resource, node.Model.Source?.Kind == "directory");
        return draft;
    }

    /// <summary>只更新采集 CD 这一项共享设置，不改变其他设置；整次变更支持撤销。</summary>
    internal void ApplyCollectionCooldown(PuloniaTaskNodeViewModel node, JObject original, JObject? edited, bool restore)
    {
        if (restore)
        {
            ResetSetting(node, PuloniaSettingCatalog.CollectionCooldownKey);
            return;
        }
        if (JToken.DeepEquals(original, edited)) return;
        PuloniaCollectionCooldown.ValidateSettings(edited, "采集 CD");
        SetSetting(node, PuloniaSettingCatalog.CollectionCooldownKey, edited!);
    }
}

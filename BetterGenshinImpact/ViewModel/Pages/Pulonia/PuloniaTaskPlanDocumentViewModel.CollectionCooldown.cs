using System.Linq;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>地图追踪 CD 草稿读取与整对象继承，复用文档撤销和自动保存边界；表单窗口由高级设置窗口打开。</summary>
public partial class PuloniaTaskPlanDocumentViewModel
{
    /// <summary>从有效参数创建独立草稿，目录引用组预览只解释该组规则。</summary>
    internal PuloniaCollectionCooldownViewModel CreateCollectionCooldownDraft(PuloniaTaskNodeViewModel node)
    {
        var parameters = node.IsLeaf
            ? ResolveEffectiveParameters(node.Model, GetAncestorModels(node.Parent), out _)
            : GetCommonSettingsParameters(node, "pathing");
        var source = "分组继承设置";
        if (node.IsLeaf)
        {
            ResolveEffectiveParameters(node.Model, GetAncestorModels(node.Parent), out var sources);
            source = sources.TryGetValue(PuloniaCollectionCooldown.ParameterName, out var origin) ? origin : "能力默认值";
        }
        var draft = new PuloniaCollectionCooldownViewModel();
        draft.Load(parameters[PuloniaCollectionCooldown.ParameterName], source);
        draft.SetResource(node.Model.Path ?? node.Model.Source?.Path,
            node.Model.Resource ?? node.Model.Source?.Resource, node.Model.Source?.Kind == "directory");
        return draft;
    }

    /// <summary>只更新采集 CD 参数对象，不改变其他设置；整次变更支持撤销。</summary>
    internal void ApplyCollectionCooldown(PuloniaTaskNodeViewModel node, JObject original, JObject? edited, bool restore)
    {
        if (!restore && JToken.DeepEquals(original, edited)) return;
        if (!restore) PuloniaCollectionCooldown.ValidateSettings(edited, "采集 CD");
        ApplyMutation(() =>
        {
            if (node.IsLeaf)
            {
                if (restore) node.Model.Parameters.Remove(PuloniaCollectionCooldown.ParameterName);
                else node.Model.Parameters[PuloniaCollectionCooldown.ParameterName] = edited!.DeepClone();
                return;
            }
            var scope = node.Model.ParameterOverrides.FirstOrDefault(item => item.TaskType == "pathing" && item.ResourceId is null && item.SchemaVersion == 1);
            if (restore)
            {
                scope?.Values.Remove(PuloniaCollectionCooldown.ParameterName);
                if (scope?.Values.Count == 0) node.Model.ParameterOverrides.Remove(scope);
                return;
            }
            if (scope is null)
            {
                scope = new PuloniaTaskParameterOverride { TaskType = "pathing" };
                node.Model.ParameterOverrides.Add(scope);
            }
            scope.Values[PuloniaCollectionCooldown.ParameterName] = edited!.DeepClone();
        }, node);
    }
}

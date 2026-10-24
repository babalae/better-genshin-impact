using System.Linq;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 节点或分组的类型公共参数（行走、战斗、食物与 Shell 设置）的读取与提交，所有提交均纳入文档撤销与自动保存。
/// 表单窗口由高级设置窗口通过对话框接口打开，文档不创建窗口、不读取全局配置。
/// </summary>
public partial class PuloniaTaskPlanDocumentViewModel
{
    /// <summary>
    /// 取得表单有效值；分组只预览该类型的通用覆盖，不混入某个具体资源的配置。
    /// </summary>
    internal JObject GetCommonSettingsParameters(PuloniaTaskNodeViewModel node, string taskType)
    {
        if (node.IsLeaf)
            return ResolveEffectiveParameters(node.Model, GetAncestorModels(node.Parent), out _);
        // 引用节点显式公共设置独立于外部祖先；与运行构建的引用边界保持一致。
        var ancestors = node.Model.Source?.Kind == "plan" ? [node.Model] : GetAncestorModels(node);
        return ResolveEffectiveParameters(new PuloniaTask { TaskType = taskType }, ancestors, out _);
    }

    /// <summary>
    /// 将表单变更写入叶子参数或类型通用覆盖；恢复继承只移除当前作用域的公共设置。
    /// </summary>
    internal void ApplyCommonSettings(PuloniaTaskNodeViewModel node, string taskType, JObject original,
        JObject edited, bool restoreInheritance)
    {
        var changes = new JObject(edited.Properties().Where(property =>
                PuloniaTaskCommonSettings.IsCommonParameter(taskType, property.Name)
                && !JToken.DeepEquals(original[property.Name], property.Value))
            .Select(property => new JProperty(property.Name, property.Value.DeepClone())));
        if (!restoreInheritance && changes.Count == 0)
            return;

        ApplyMutation(() =>
        {
            if (node.IsLeaf)
            {
                UpdateValues(node.Model.Parameters);
                return;
            }

            var scopes = node.Model.ParameterOverrides.Where(item => item.TaskType == taskType
                && item.ResourceId is null && item.SchemaVersion == 1).ToArray();
            if (restoreInheritance)
            {
                foreach (var scope in scopes)
                {
                    UpdateValues(scope.Values);
                    if (scope.Values.Count == 0)
                        node.Model.ParameterOverrides.Remove(scope);
                }
                return;
            }

            var target = scopes.LastOrDefault();
            if (target is null)
            {
                target = new PuloniaTaskParameterOverride { TaskType = taskType };
                node.Model.ParameterOverrides.Add(target);
            }
            UpdateValues(target.Values);
        }, node);

        // 只处理本表单拥有的键，保留脚本 settings、Shell 命令和其他扩展参数。
        void UpdateValues(JObject values)
        {
            if (restoreInheritance)
            {
                foreach (var property in values.Properties()
                             .Where(property => PuloniaTaskCommonSettings.IsCommonParameter(taskType, property.Name)).ToArray())
                    property.Remove();
            }
            else
            {
                foreach (var property in changes.Properties())
                    values[property.Name] = property.Value.DeepClone();
            }
        }
    }
}

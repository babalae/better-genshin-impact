using System.Collections.Generic;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>
/// 用户在创建弹窗中确认后生成的一次性任务创建结果。
/// </summary>
public sealed class PuloniaTaskCreationResult
{
    /// <summary>
    /// 用户确认后可以原子插入计划树的完整根节点。
    /// </summary>
    public PuloniaTask Task { get; }

    /// <summary>
    /// 所选 JS 项目 settings_ui 声明的设置项；任务计划在添加后据此弹出脚本设置窗口。非 JS 任务为空。
    /// </summary>
    public IReadOnlyList<SettingItem> JavaScriptSettingItems { get; }

    /// <summary>
    /// 建立不可变的任务创建结果。
    /// </summary>
    /// <param name="task">待插入的任务根节点。</param>
    /// <param name="javaScriptSettingItems">JS 项目声明的设置项；非 JS 任务为空。</param>
    public PuloniaTaskCreationResult(PuloniaTask task, IReadOnlyList<SettingItem>? javaScriptSettingItems = null)
    {
        Task = task;
        JavaScriptSettingItems = javaScriptSettingItems ?? [];
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 设置方案的读取、保存与删除：方案是独立文件，不进入任何计划的撤销历史；保存后统一刷新全部打开文档。
/// </summary>
public partial class PuloniaPlanEditingSession
{
    /// <summary>当前已加载的设置方案，按名称排序。</summary>
    internal IReadOnlyList<PuloniaSettingsProfile> Profiles => _profiles;

    /// <summary>
    /// 保存方案（新建或修改），成功后刷新全部文档的方案列表；修订冲突时抛出且不修改已加载的列表。
    /// </summary>
    /// <param name="profile">要保存的方案草稿。</param>
    /// <returns>保存后的新修订。</returns>
    internal async Task<PuloniaSettingsProfile> SaveProfileAsync(PuloniaSettingsProfile profile)
    {
        var saved = await _store.SaveProfileAsync(profile);
        await ReloadProfilesAsync();
        return _profiles.FirstOrDefault(item => item.Id == saved.Id) ?? saved;
    }

    /// <summary>
    /// 删除方案：任何打开中的计划（包括未保存草稿）仍使用时拒绝，存储层再检查已保存的计划。
    /// </summary>
    /// <param name="profile">要删除的方案。</param>
    /// <exception cref="InvalidOperationException">方案仍被使用。</exception>
    internal async Task DeleteProfileAsync(PuloniaSettingsProfile profile)
    {
        var usages = GetProfileUsages(profile.Id);
        if (usages.Count > 0)
            throw new InvalidOperationException("以下位置仍在使用此方案，请先改掉后再删除：\n" + string.Join("\n", usages));
        await _store.DeleteProfileAsync(profile.Id, profile.Revision);
        await ReloadProfilesAsync();
    }

    /// <summary>
    /// 列出打开中的计划里使用指定方案的节点，形如“计划「每日」· 分组「蒙德」”。
    /// </summary>
    internal IReadOnlyList<string> GetProfileUsages(string profileId)
        => AllDocuments.SelectMany(document => document.EnumerateNodes()
                .Where(node => node.Model.ProfileId == profileId)
                .Select(node => $"计划「{document.Name}」· " + (node.IsRoot ? "计划级设置" : $"「{node.Name}」")))
            .ToArray();

    /// <summary>
    /// 重新读取方案并刷新全部文档的树标记、摘要和打开中的设置窗口；损坏文件只提示，不阻断其他方案。
    /// </summary>
    internal async Task ReloadProfilesAsync()
    {
        var result = await _store.ListProfilesAsync();
        _profiles = result.Items;
        foreach (var document in AllDocuments)
            document.SetProfiles(_profiles);
        if (result.Failures.Count > 0)
            ReportStatus($"有 {result.Failures.Count} 个设置方案文件无法读取，已跳过：" + string.Join("；", result.Failures.Select(item => item.ToString())));
    }
}

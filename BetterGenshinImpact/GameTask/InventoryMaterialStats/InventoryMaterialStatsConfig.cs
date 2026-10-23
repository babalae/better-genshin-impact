using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.GameTask.Model.GameUI;

namespace BetterGenshinImpact.GameTask.InventoryMaterialStats;

/// <summary>
/// 背包材料统计独立任务配置。扫描分类按背包页，默认全选。
/// </summary>
[Serializable]
public partial class InventoryMaterialStatsConfig : ObservableObject
{
    /// <summary>
    /// 已安装 JS 拷贝的文件夹名。空则运行时若只有一份则自动选用。
    /// </summary>
    [ObservableProperty]
    private string _scriptFolderName = string.Empty;

    [ObservableProperty]
    private bool _scanCharacterDevelopmentItems = true;

    [ObservableProperty]
    private bool _scanFood = true;

    [ObservableProperty]
    private bool _scanMaterials = true;

    /// <summary>
    /// 当前勾选背包页对应的脚本分类目录名。
    /// </summary>
    public IReadOnlyList<string> GetEnabledCategories()
    {
        var pages = GetEnabledPages();
        return InventoryMaterialStatsCategories.CategoryToPage
            .Where(kv => pages.Contains(kv.Value))
            .Select(kv => kv.Key)
            .ToList();
    }

    public HashSet<GridScreenName> GetEnabledPages()
    {
        var pages = new HashSet<GridScreenName>();
        if (ScanCharacterDevelopmentItems) pages.Add(GridScreenName.CharacterDevelopmentItems);
        if (ScanFood) pages.Add(GridScreenName.Food);
        if (ScanMaterials) pages.Add(GridScreenName.Materials);
        return pages;
    }
}

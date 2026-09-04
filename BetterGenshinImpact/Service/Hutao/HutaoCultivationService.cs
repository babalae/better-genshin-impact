using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service.Model.Hutao;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service.Hutao;

internal sealed class HutaoCultivationService : IHutaoCultivationService
{
    private readonly HutaoNamedPipe hutaoNamedPipe;
    private readonly IScriptService scriptService;
    private readonly ILogger<HutaoCultivationService> logger;

    public HutaoCultivationService(HutaoNamedPipe hutaoNamedPipe, IScriptService scriptService, ILogger<HutaoCultivationService> logger)
    {
        this.hutaoNamedPipe = hutaoNamedPipe;
        this.scriptService = scriptService;
        this.logger = logger;
    }

    public bool IsHutaoAvailable()
    {
        return hutaoNamedPipe.IsHutaoRunning();
    }

    public async Task<(bool Started, string Message)> FetchAndFarmAsync()
    {
        AutomationCultivationProject? project = hutaoNamedPipe.TryQueryCurrentCultivationProject();
        if (project is null)
        {
            return (false, "未能获取胡桃养成存档，请确认胡桃已运行并选中养成项目");
        }

        // 材料名 -> 需求数量；材料名 -> 掉落该材料的怪物名。
        Dictionary<string, uint> required = [];
        Dictionary<string, List<string>> monstersByName = [];
        foreach (AutomationCultivationEntry entry in project.Entries)
        {
            foreach (AutomationCultivationItem item in entry.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Name) || !IsFarmableMaterial(item.ItemId, item.RankLevel))
                {
                    continue;
                }

                required[item.Name] = required.GetValueOrDefault(item.Name) + item.Count;

                if (item.Monsters is { Count: > 0 } && !monstersByName.ContainsKey(item.Name))
                {
                    monstersByName[item.Name] = item.Monsters;
                }
            }
        }

        Dictionary<string, uint> owned = [];
        foreach (AutomationInventoryItem item in project.InventoryItems)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
            {
                continue;
            }

            owned[item.Name] = owned.GetValueOrDefault(item.Name) + item.Count;
        }

        List<string> deficit = [];
        foreach ((string name, uint need) in required)
        {
            if (need > owned.GetValueOrDefault(name))
            {
                deficit.Add(name);
            }
        }

        if (deficit.Count == 0)
        {
            return (true, "养成材料已齐全，无需刷取");
        }

        HashSet<string> matchedFiles = [];
        List<string> unmatched = [];
        foreach (string name in deficit)
        {
            List<string> targets = [name];
            if (monstersByName.TryGetValue(name, out List<string>? monsters))
            {
                targets.AddRange(monsters ?? []);
            }

            List<string> files = FindPathingTaskFiles(targets);
            if (files.Count == 0)
            {
                unmatched.Add(name);
                continue;
            }

            matchedFiles.UnionWith(files);
        }

        if (unmatched.Count > 0)
        {
            logger.LogWarning("以下材料未找到地图追踪路线：{Materials}", string.Join("、", unmatched));
        }

        if (matchedFiles.Count == 0)
        {
            return (false, $"未找到可刷取的路线：{string.Join("、", unmatched)}");
        }

        List<ScriptGroupProject> projects = [];
        foreach (string file in matchedFiles)
        {
            FileInfo fileInfo = new(file);
            string folder = Path.GetRelativePath(MapPathingViewModel.PathJsonPath, fileInfo.DirectoryName!);
            projects.Add(ScriptGroupProject.BuildPathingProject(fileInfo.Name, folder));
        }

        await scriptService.RunMulti(projects);
        return (true, $"已开始刷取 {projects.Count} 条路线");
    }

    private static List<string> FindPathingTaskFiles(List<string> targetNames)
    {
        List<string> result = [];
        if (!Directory.Exists(MapPathingViewModel.PathJsonPath))
        {
            return result;
        }

        HashSet<string> targets = new(targetNames);
        foreach (string file in Directory.EnumerateFiles(MapPathingViewModel.PathJsonPath, "*.json", SearchOption.AllDirectories))
        {
            if (HasAncestorNamed(file, MapPathingViewModel.PathJsonPath, targets))
            {
                result.Add(file);
            }
        }

        return result;
    }

    private static bool HasAncestorNamed(string file, string basePath, HashSet<string> targets)
    {
        string relative = Path.GetRelativePath(basePath, file);
        string[] parts = relative.Split(Path.DirectorySeparatorChar);
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (targets.Contains(parts[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMaterialItem(uint id)
    {
        return id is >= 100001 and <= 104000;
    }

    private static bool IsMonsterItem(uint id, uint qualityType)
    {
        return id is >= 112001 and <= 114000 && qualityType == 3; // QUALITY_BLUE
    }

    private static bool IsBossItem(uint id, uint qualityType)
    {
        return id is >= 113001 and <= 114000 && qualityType == 4; // QUALITY_PURPLE
    }

    private static bool IsWeeklyBossItem(uint id, uint qualityType)
    {
        return id is >= 113001 and <= 114000 && qualityType == 5; // QUALITY_ORANGE
    }

    private static bool IsElementStone(uint id, uint qualityType)
    {
        return id is >= 104101 and <= 104174 && qualityType == 5; // QUALITY_ORANGE
    }

    private static bool IsTalentBook(uint id, uint qualityType)
    {
        return id is >= 104301 and <= 105000 && qualityType == 4; // QUALITY_PURPLE
    }

    private static bool IsFarmableMaterial(uint id, uint qualityType)
    {
        // 排除 Boss 材料、周本材料、天赋书、元素石
        if (IsBossItem(id, qualityType) || IsWeeklyBossItem(id, qualityType) || IsTalentBook(id, qualityType) || IsElementStone(id, qualityType))
        {
            return false;
        }

        // 排除摩拉(202)与角色经验书(104001-104003)
        if (id == 202 || id is >= 104001 and <= 104003)
        {
            return false;
        }

        // 采集物(材料) + 怪物掉落(蓝色)
        return IsMaterialItem(id) || IsMonsterItem(id, qualityType);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BetterGenshinImpact.GameTask.Common.Reward;

/// <summary>
/// 奖励停止数据目录：物品、配方与首领档案。
/// </summary>
public sealed class TargetItemCatalog
{
    private const string ResourceName = "BetterGenshinImpact.Resources.TargetItem.target-item-data.json";

    private static readonly Lazy<TargetItemCatalog> LazyInstance = new(() => new TargetItemCatalog());
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver()
    };

    private readonly IReadOnlyList<RewardCraftRecipe> _recipes;
    private readonly IReadOnlyDictionary<string, BossProfile> _bossProfiles;
    private readonly IReadOnlyList<TargetItemOption> _domainItems;
    private readonly IReadOnlyDictionary<string, DomainRewardProfile> _domainProfiles;
    private readonly IReadOnlyList<ExpItemOption> _leylineExpItems;
    private readonly string _leylineMoraItemName;

    /// <summary>
    /// 全局唯一的数据目录。
    /// </summary>
    public static TargetItemCatalog Instance => LazyInstance.Value;

    /// <summary>
    /// 秘境目标物品下拉选项（全部 168 项，UI 按秘境过滤后使用）。
    /// </summary>
    public IReadOnlyList<TargetItemOption> DomainItems => _domainItems;

    /// <summary>
    /// 秘境掉落档案数量。
    /// </summary>
    public int DomainProfileCount => _domainProfiles.Count;

    /// <summary>
    /// 已有掉落档案的秘境名。
    /// </summary>
    public IEnumerable<string> DomainProfileNames => _domainProfiles.Keys;

    /// <summary>
    /// 按秘境名取该区域的掉落目标（3 个挑战的并集）。
    /// </summary>
    /// <returns>存在档案时返回 true；空名/圣遗物秘境/未收录秘境返回 false。</returns>
    public bool TryGetDomainItems(string? domainName, out IReadOnlyList<TargetItemOption> items)
    {
        if (string.IsNullOrWhiteSpace(domainName) || !_domainProfiles.TryGetValue(domainName, out var profile))
        {
            items = [];
            return false;
        }

        items = profile.Drops;
        return true;
    }

    /// <summary>
    /// 地脉花三档经验书。
    /// </summary>
    public IReadOnlyList<ExpItemOption> LeylineExpItems => _leylineExpItems;

    /// <summary>
    /// 摩拉物品名。
    /// </summary>
    public string LeylineMoraItemName => _leylineMoraItemName;

    /// <summary>
    /// 首领、秘境与地脉物品去重后的总数。
    /// </summary>
    public int ItemCount { get; }

    /// <summary>
    /// 数据文件中的配方总数。
    /// </summary>
    public int RecipeCount => _recipes.Count;

    /// <summary>
    /// 首领档案总数。
    /// </summary>
    public int BossProfileCount => _bossProfiles.Count;

    private TargetItemCatalog()
    {
        var data = LoadData();
        if (data.SchemaVersion != 3)
        {
            throw new InvalidDataException($"奖励停止数据版本应为 3，实际为 {data.SchemaVersion}");
        }

        // 1. 地脉：经验书与摩拉不在首领/秘境数据中，单独读取。
        var expItems = data.Leyline.ExpItems
            .Select(x => new ExpItemOption(x.Name, x.ExpValue))
            .OrderBy(x => x.ExpValue)
            .ToList();
        if (expItems.Count != 3)
        {
            throw new InvalidDataException($"奖励停止数据经验书数量应为 3，实际为 {expItems.Count}");
        }

        foreach (var item in expItems)
        {
            if (string.IsNullOrWhiteSpace(item.Name) || item.ExpValue <= 0)
            {
                throw new InvalidDataException($"奖励停止数据经验书配置无效：{item.Name}");
            }
        }

        if (expItems.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != expItems.Count)
        {
            throw new InvalidDataException("奖励停止数据经验书名称重复");
        }

        var moraName = data.Leyline.Mora;
        if (string.IsNullOrWhiteSpace(moraName))
        {
            throw new InvalidDataException("奖励停止数据缺少摩拉物品");
        }

        var allNames = new HashSet<string>(StringComparer.Ordinal);

        // 2. 秘境：按域类型派生物品类别，收集掉落名称。
        var domainEntries = new List<(string Name, RewardDomainType Type, List<string> Drops)>();
        foreach (var domain in data.Domains)
        {
            if (string.IsNullOrWhiteSpace(domain.Name) || domainEntries.Any(x => x.Name == domain.Name))
            {
                throw new InvalidDataException($"奖励停止数据秘境名无效或重复：{domain.Name}");
            }

            if (!Enum.TryParse<RewardDomainType>(domain.Type, true, out var domainType))
            {
                throw new InvalidDataException($"奖励停止数据秘境类型无效：{domain.Name} / {domain.Type}");
            }

            var drops = new List<string>(domain.Drops.Count);
            var dropSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var drop in domain.Drops)
            {
                if (string.IsNullOrWhiteSpace(drop))
                {
                    throw new InvalidDataException($"奖励停止数据秘境掉落为空：{domain.Name}");
                }

                if (!dropSet.Add(drop))
                {
                    throw new InvalidDataException($"奖励停止数据秘境掉落重复：{domain.Name} / {drop}");
                }

                if (!allNames.Add(drop))
                {
                    throw new InvalidDataException($"奖励停止数据物品重复：{drop}");
                }

                drops.Add(drop);
            }

            var expectedCount = domainType == RewardDomainType.TalentMaterial ? 9 : 12;
            if (drops.Count != expectedCount)
            {
                throw new InvalidDataException($"奖励停止数据秘境掉落数量应为 {expectedCount}：{domain.Name} / {drops.Count}");
            }

            domainEntries.Add((domain.Name, domainType, drops));
        }

        if (domainEntries.Count != 16)
        {
            throw new InvalidDataException($"奖励停止数据秘境档案数量应为 16，实际为 {domainEntries.Count}");
        }

        // 3. 首领：材料与宝石名称加入物品集合，宝石系列名取五星物品名。
        var bossProfiles = new Dictionary<string, BossProfile>(StringComparer.Ordinal);
        foreach (var boss in data.Bosses)
        {
            if (string.IsNullOrWhiteSpace(boss.Name))
            {
                throw new InvalidDataException("奖励停止数据存在空首领名");
            }

            if (string.IsNullOrWhiteSpace(boss.BossMaterial))
            {
                throw new InvalidDataException($"奖励停止数据首领材料为空：{boss.Name}");
            }

            allNames.Add(boss.BossMaterial);

            var families = new List<BossGemFamily>(boss.GemFamilies.Count);
            var familyNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var family in boss.GemFamilies)
            {
                var items = family.Items
                    .OrderBy(x => x.Rarity)
                    .Select(x =>
                    {
                        if (string.IsNullOrWhiteSpace(x.Name))
                        {
                            throw new InvalidDataException($"奖励停止数据首领宝石为空：{boss.Name}");
                        }

                        allNames.Add(x.Name);
                        return new BossGemRarityItem(x.Rarity, x.Name);
                    })
                    .ToList();

                if (items.Count != 4
                    || items.Select(x => x.Rarity).Distinct().OrderBy(x => x).SequenceEqual([2, 3, 4, 5]) == false)
                {
                    throw new InvalidDataException($"首领宝石系列必须包含 2/3/4/5 星：{boss.Name}");
                }

                var familyName = items.Single(x => x.Rarity == 5).ItemName;
                if (!familyNames.Add(familyName))
                {
                    throw new InvalidDataException($"首领宝石系列名重复：{boss.Name} / {familyName}");
                }

                families.Add(new BossGemFamily(familyName, items));
            }

            if (families.Count == 0)
            {
                throw new InvalidDataException($"首领缺少宝石系列：{boss.Name}");
            }

            if (!bossProfiles.TryAdd(boss.Name, new BossProfile(boss.Name, boss.BossMaterial, families)))
            {
                throw new InvalidDataException($"首领档案重复：{boss.Name}");
            }
        }

        // 4. 地脉名称加入物品集合。
        foreach (var item in expItems)
        {
            if (!allNames.Add(item.Name))
            {
                throw new InvalidDataException($"奖励停止数据物品重复：{item.Name}");
            }
        }

        if (!allNames.Add(moraName))
        {
            throw new InvalidDataException($"奖励停止数据物品重复：{moraName}");
        }

        // 5. 配方：两端都必须能由首领/秘境/地脉数据派生。
        var recipes = new List<RewardCraftRecipe>(data.Recipes.Count);
        var recipeInputs = new HashSet<string>(StringComparer.Ordinal);
        var recipeOutputs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var recipe in data.Recipes)
        {
            if (string.IsNullOrWhiteSpace(recipe.Input) || string.IsNullOrWhiteSpace(recipe.Output))
            {
                throw new InvalidDataException("奖励停止数据存在空配方物品名");
            }

            if (!allNames.Contains(recipe.Input) || !allNames.Contains(recipe.Output))
            {
                throw new InvalidDataException($"奖励停止数据配方物品不存在：{recipe.Input} → {recipe.Output}");
            }

            if (recipe.InputCount <= 0 || recipe.OutputCount <= 0)
            {
                throw new InvalidDataException($"奖励停止数据配方数量无效：{recipe.Input} → {recipe.Output}");
            }

            if (!recipeInputs.Add(recipe.Input) || !recipeOutputs.Add(recipe.Output))
            {
                throw new InvalidDataException($"奖励停止数据存在重复配方：{recipe.Input} → {recipe.Output}");
            }

            recipes.Add(new RewardCraftRecipe(recipe.Input, recipe.Output, recipe.InputCount, recipe.OutputCount));
        }

        // 6. 域物品下拉：天赋书在前、武器材料在后，各自按名称排序；域的 Drops 沿用该全局顺序。
        var domainItems = domainEntries
            .Where(x => x.Type == RewardDomainType.TalentMaterial)
            .SelectMany(x => x.Drops)
            .OrderBy(x => x, StringComparer.Ordinal)
            .Select(x => new TargetItemOption(x, $"{CategoryLabel(RewardDomainType.TalentMaterial)}｜{x}"))
            .Concat(domainEntries
                .Where(x => x.Type == RewardDomainType.WeaponMaterial)
                .SelectMany(x => x.Drops)
                .OrderBy(x => x, StringComparer.Ordinal)
                .Select(x => new TargetItemOption(x, $"{CategoryLabel(RewardDomainType.WeaponMaterial)}｜{x}")))
            .ToList();

        var domainProfiles = new Dictionary<string, DomainRewardProfile>(StringComparer.Ordinal);
        foreach (var entry in domainEntries)
        {
            var dropSet = entry.Drops.ToHashSet(StringComparer.Ordinal);
            domainProfiles.Add(entry.Name, new DomainRewardProfile(
                entry.Name,
                entry.Type,
                domainItems.Where(x => dropSet.Contains(x.Name)).ToList()));
        }

        _recipes = recipes;
        _bossProfiles = bossProfiles;
        _domainItems = domainItems;
        _domainProfiles = domainProfiles;
        _leylineExpItems = expItems;
        _leylineMoraItemName = moraName;
        ItemCount = allNames.Count;
    }

    /// <summary>
    /// 取首领档案。
    /// </summary>
    public bool TryGetBossProfile(string bossName, out BossProfile profile)
    {
        return _bossProfiles.TryGetValue(bossName, out profile!);
    }

    /// <summary>
    /// 校验秘境目标：目标必须属于当前秘境区域的掉落表。
    /// </summary>
    /// <param name="domainName">当前选择的秘境区域名。</param>
    /// <param name="targets">目标列表。</param>
    public void ValidateDomainTargets(string? domainName, IEnumerable<TargetItem> targets)
    {
        if (string.IsNullOrWhiteSpace(domainName) || !_domainProfiles.TryGetValue(domainName, out var profile))
        {
            throw new TargetItemConfigurationException(
                $"当前秘境没有掉落物数据：{domainName ?? "<未选择>"}。圣遗物秘境、自动选择或未选择固定秘境不支持掉落物数量模式");
        }

        var list = targets?.ToList() ?? [];
        if (list.Count == 0)
        {
            throw new TargetItemConfigurationException("请至少配置一个秘境目标物品");
        }

        var allowed = profile.Drops.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in list)
        {
            if (string.IsNullOrWhiteSpace(target.ItemName) || !allowed.Contains(target.ItemName))
            {
                throw new TargetItemConfigurationException(
                    $"目标不在当前秘境掉落表：{domainName} / {target.ItemName}");
            }

            if (target.TargetCount < 1)
            {
                throw new TargetItemConfigurationException($"秘境目标数量必须大于 0：{target.ItemName}");
            }

            if (!names.Add(target.ItemName))
            {
                throw new TargetItemConfigurationException($"秘境目标物品重复：{target.ItemName}");
            }
        }
    }

    /// <summary>
    /// 校验首领奖励停止配置：目标必须是当前首领的材料或宝石掉落物。
    /// </summary>
    public void ValidateBossSettings(BossProfile profile, IEnumerable<TargetItem> targets)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var list = targets?.ToList() ?? [];
        if (list.Count == 0)
        {
            throw new TargetItemConfigurationException("请至少配置一个首领掉落目标");
        }

        var allowed = new HashSet<string>(StringComparer.Ordinal) { profile.BossMaterial };
        foreach (var family in profile.GemFamilies)
        {
            foreach (var item in family.Items)
            {
                allowed.Add(item.ItemName);
            }
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in list)
        {
            if (string.IsNullOrWhiteSpace(target.ItemName) || !allowed.Contains(target.ItemName))
            {
                throw new TargetItemConfigurationException($"目标不是当前首领的掉落物：{target.ItemName}");
            }

            if (target.TargetCount < 1)
            {
                throw new TargetItemConfigurationException($"首领掉落目标数量必须大于 0：{target.ItemName}");
            }

            if (!names.Add(target.ItemName))
            {
                throw new TargetItemConfigurationException($"首领掉落目标物品重复：{target.ItemName}");
            }
        }
    }

    /// <summary>
    /// 校验地脉花奖励停止配置：藏金之花只收摩拉，启示之花只收经验书。
    /// </summary>
    public void ValidateLeylineSettings(string leyLineType, IEnumerable<TargetItem> targets)
    {
        if (leyLineType == "藏金之花")
        {
            var list = targets?.ToList() ?? [];
            if (list.Count == 0)
            {
                throw new TargetItemConfigurationException("请至少配置一个地脉花目标物品");
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var target in list)
            {
                if (string.IsNullOrWhiteSpace(target.ItemName) || target.ItemName != _leylineMoraItemName)
                {
                    throw new TargetItemConfigurationException($"藏金之花目标必须是摩拉：{target.ItemName}");
                }

                if (target.TargetCount < 1)
                {
                    throw new TargetItemConfigurationException($"地脉花目标数量必须大于 0：{target.ItemName}");
                }

                if (!names.Add(target.ItemName))
                {
                    throw new TargetItemConfigurationException($"地脉花目标物品重复：{target.ItemName}");
                }
            }

            return;
        }

        if (leyLineType != "启示之花")
        {
            throw new TargetItemConfigurationException($"不支持的地脉花类型：{leyLineType}");
        }

        var expList = targets?.ToList() ?? [];
        if (expList.Count == 0)
        {
            throw new TargetItemConfigurationException("请至少配置一个地脉花目标物品");
        }

        var allowed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in _leylineExpItems)
        {
            allowed.Add(item.Name);
        }

        var expNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in expList)
        {
            if (string.IsNullOrWhiteSpace(target.ItemName) || !allowed.Contains(target.ItemName))
            {
                throw new TargetItemConfigurationException($"目标不是地脉花经验奖励：{target.ItemName}");
            }

            if (target.TargetCount < 1)
            {
                throw new TargetItemConfigurationException($"地脉花目标数量必须大于 0：{target.ItemName}");
            }

            if (!expNames.Add(target.ItemName))
            {
                throw new TargetItemConfigurationException($"地脉花目标物品重复：{target.ItemName}");
            }
        }
    }

    /// <summary>
    /// 计算秘境目标进度。
    /// </summary>
    public TargetProgress CalculateDomain(
        IReadOnlyList<TargetItem> targets,
        IReadOnlyDictionary<string, int> totals)
    {
        var specs = targets.Select(x => new TargetItemSpec(x.ItemName, x.TargetCount)).ToList();
        return TargetItemProgressCalculator.Calculate(specs, totals, _recipes);
    }

    /// <summary>
    /// 计算首领奖励进度：首领材料无配方、宝石低档到高档的折算由配方驱动。
    /// </summary>
    public TargetProgress CalculateBoss(
        IReadOnlyList<TargetItem> targets,
        IReadOnlyDictionary<string, int> totals)
    {
        var specs = targets.Select(x => new TargetItemSpec(x.ItemName, x.TargetCount)).ToList();
        return TargetItemProgressCalculator.Calculate(specs, totals, _recipes);
    }

    /// <summary>
    /// 计算地脉花奖励进度：按各目标物品的增量数量。
    /// </summary>
    public TargetProgress CalculateLeyline(
        IReadOnlyList<TargetItem> targets,
        IReadOnlyDictionary<string, int> gains)
    {
        var progress = new List<TargetItemProgress>(targets.Count);
        foreach (var target in targets)
        {
            var count = gains.TryGetValue(target.ItemName, out var value) ? Math.Max(0, value) : 0;
            var counted = Math.Min(count, target.TargetCount);
            progress.Add(new TargetItemProgress(
                target.ItemName,
                target.TargetCount,
                counted,
                count >= target.TargetCount));
        }

        return new TargetProgress(progress);
    }

    private static string CategoryLabel(RewardDomainType type)
    {
        return type == RewardDomainType.TalentMaterial ? "天赋书" : "武器材料";
    }

    private static TargetItemData LoadData()
    {
        using var stream = typeof(TargetItemCatalog).Assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException($"未找到奖励停止数据资源：{ResourceName}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return JsonConvert.DeserializeObject<TargetItemData>(reader.ReadToEnd(), JsonSettings)
               ?? throw new InvalidDataException("奖励停止数据为空");
    }
}

/// <summary>
/// 奖励停止数据根对象。
/// </summary>
internal sealed class TargetItemData
{
    /// <summary>
    /// 数据版本。
    /// </summary>
    public int SchemaVersion { get; set; }

    /// <summary>
    /// 地脉花物品。
    /// </summary>
    public LeylineDto Leyline { get; set; } = new();

    /// <summary>
    /// 合成配方列表。
    /// </summary>
    public List<RecipeDto> Recipes { get; set; } = [];

    /// <summary>
    /// 首领档案列表。
    /// </summary>
    public List<BossDto> Bosses { get; set; } = [];

    /// <summary>
    /// 秘境掉落档案列表。
    /// </summary>
    public List<DomainDto> Domains { get; set; } = [];
}

/// <summary>
/// 秘境掉落档案数据。
/// </summary>
internal sealed class DomainDto
{
    /// <summary>
    /// 秘境区域名。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 秘境类型。
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 该区域 3 个挑战的掉落并集。
    /// </summary>
    public List<string> Drops { get; set; } = [];
}

/// <summary>
/// 地脉花物品数据。
/// </summary>
internal sealed class LeylineDto
{
    /// <summary>
    /// 三档经验书。
    /// </summary>
    public List<ExpItemDto> ExpItems { get; set; } = [];

    /// <summary>
    /// 摩拉物品名。
    /// </summary>
    public string Mora { get; set; } = string.Empty;
}

/// <summary>
/// 经验书数据。
/// </summary>
internal sealed class ExpItemDto
{
    /// <summary>
    /// 经验书名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 单本经验值。
    /// </summary>
    public int ExpValue { get; set; }
}

/// <summary>
/// 合成配方数据。
/// </summary>
internal sealed class RecipeDto
{
    /// <summary>
    /// 输入物品名。
    /// </summary>
    public string Input { get; set; } = string.Empty;

    /// <summary>
    /// 输出物品名。
    /// </summary>
    public string Output { get; set; } = string.Empty;

    /// <summary>
    /// 输入数量。
    /// </summary>
    public int InputCount { get; set; }

    /// <summary>
    /// 输出数量。
    /// </summary>
    public int OutputCount { get; set; }
}

/// <summary>
/// 首领档案数据。
/// </summary>
internal sealed class BossDto
{
    /// <summary>
    /// 首领名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 首领材料名。
    /// </summary>
    public string BossMaterial { get; set; } = string.Empty;

    /// <summary>
    /// 宝石系列列表。
    /// </summary>
    public List<GemFamilyDto> GemFamilies { get; set; } = [];
}

/// <summary>
/// 首领宝石系列数据。
/// </summary>
internal sealed class GemFamilyDto
{
    /// <summary>
    /// 四档宝石物品。
    /// </summary>
    public List<GemItemDto> Items { get; set; } = [];
}

/// <summary>
/// 宝石系列单档物品。
/// </summary>
internal sealed class GemItemDto
{
    /// <summary>
    /// 稀有度。
    /// </summary>
    public int Rarity { get; set; }

    /// <summary>
    /// 物品名。
    /// </summary>
    public string Name { get; set; } = string.Empty;
}

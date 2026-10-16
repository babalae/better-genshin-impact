using BetterGenshinImpact.GameTask.Model.GameUI;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.InventoryMaterialStats;

/// <summary>
/// 订阅脚本 <c>assets/images</c> 子目录与背包页的对应。
/// </summary>
public static class InventoryMaterialStatsCategories
{
    public const string BlessingEssence = "祝圣精华";
    public const string MonsterDrops = "怪物掉落素材";
    public const string WeeklyBoss = "周本素材";
    public const string WorldBoss = "角色突破素材";
    public const string Gems = "宝石";
    public const string TalentMaterials = "角色天赋素材";
    public const string WeaponAscension = "武器突破素材";
    public const string GatheredFood = "采集食物";
    public const string Dishes = "料理";
    public const string ForgingMaterials = "锻造素材";
    public const string GeneralMaterials = "一般素材";
    public const string CookingIngredients = "烹饪食材";
    public const string Wood = "木材";
    public const string BaitAndFish = "鱼饵鱼类";

    public static readonly IReadOnlyDictionary<string, GridScreenName> CategoryToPage =
        new Dictionary<string, GridScreenName>
        {
            [BlessingEssence] = GridScreenName.CharacterDevelopmentItems,
            [MonsterDrops] = GridScreenName.CharacterDevelopmentItems,
            [WeeklyBoss] = GridScreenName.CharacterDevelopmentItems,
            [WorldBoss] = GridScreenName.CharacterDevelopmentItems,
            [Gems] = GridScreenName.CharacterDevelopmentItems,
            [TalentMaterials] = GridScreenName.CharacterDevelopmentItems,
            [WeaponAscension] = GridScreenName.CharacterDevelopmentItems,
            [GatheredFood] = GridScreenName.Food,
            [Dishes] = GridScreenName.Food,
            [ForgingMaterials] = GridScreenName.Materials,
            [GeneralMaterials] = GridScreenName.Materials,
            [CookingIngredients] = GridScreenName.Materials,
            [Wood] = GridScreenName.Materials,
            [BaitAndFish] = GridScreenName.Materials,
        };

    public static IReadOnlyList<string> AllFolderNames => CategoryToPage.Keys.ToList();
}

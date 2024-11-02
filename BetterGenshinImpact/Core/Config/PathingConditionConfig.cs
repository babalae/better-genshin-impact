using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;

namespace BetterGenshinImpact.Core.Config;

[Serializable]
public partial class PathingConditionConfig : ObservableObject
{
    // 路径追踪条件配置
    [ObservableProperty]
    private ObservableCollection<Condition> _partyConditions = [];

    [ObservableProperty]
    private ObservableCollection<Condition> _avatarConditions = [];

    public PathingConditionConfig()
    {
        _partyConditions.CollectionChanged += OnConditionsChanged;
        _avatarConditions.CollectionChanged += OnConditionsChanged;
    }

    private void OnConditionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(PartyConditions));
    }

    /// <summary>
    /// 找出当前应该切换的队伍名称
    /// </summary>
    /// <param name="materialName">采集物名称</param>
    /// <param name="specialActions">特殊动作</param>
    /// <returns></returns>
    public string? FilterPartyName(string materialName, List<string?> specialActions)
    {
        if (specialActions is { Count: > 0 })
        {
            // 特殊动作匹配队伍名
            foreach (var action in specialActions)
            {
                if (string.IsNullOrEmpty(action))
                {
                    continue;
                }

                var condition = PartyConditions.FirstOrDefault(c => c.Subject == "动作" && c.Object != null && c.Object.Contains(action));
                if (condition is { Result: not null })
                {
                    return condition.Result;
                }
            }
        }

        // 采集物匹配队伍名
        var materialCondition = PartyConditions.FirstOrDefault(c => c.Subject == "采集物" && c.Object != null && c.Object.Contains(materialName));
        if (materialCondition is { Result: not null })
        {
            return materialCondition.Result;
        }
        else
        {
            materialCondition = PartyConditions.FirstOrDefault(c => c.Subject == "采集物" && c.Object != null && c.Object.Contains("全部"));
            if (materialCondition is { Result: not null })
            {
                return materialCondition.Result;
            }
        }

        return null;
    }

    /// <summary>
    /// 通过条件配置生成队伍配置
    /// </summary>
    /// <returns></returns>
    public PathingPartyConfig BuildPartyConfigByCondition(CombatScenes combatScenes)
    {
        PathingPartyConfig partyConfig = new();
        // 使用最优先匹配上的条件
        foreach (var avatarCondition in AvatarConditions)
        {
            if (avatarCondition.Result == "循环短E" || avatarCondition.Result == "循环长E")
            {
                foreach (var avatar in combatScenes.Avatars)
                {
                    if (avatarCondition is { Object: not null } && avatarCondition.Object.Contains(avatar.Name))
                    {
                        partyConfig.GuardianAvatarIndex = avatar.Index.ToString();
                        partyConfig.GuardianElementalSkillSecondInterval = avatar.SkillCd.ToString(CultureInfo.CurrentCulture);
                        partyConfig.GuardianElementalSkillLongPress = avatarCondition.Result == "循环长E";
                        break;
                    }
                }
                if (!string.IsNullOrEmpty(partyConfig.GuardianAvatarIndex))
                {
                    break;
                }
            }
        }

        foreach (var avatarCondition in AvatarConditions)
        {
            if (avatarCondition.Result == "作为主要行走人员")
            {
                foreach (var avatar in combatScenes.Avatars)
                {
                    if (avatarCondition is { Object: not null } && avatarCondition.Object.Contains(avatar.Name))
                    {
                        partyConfig.MainAvatarIndex = avatar.Index.ToString();
                        break;
                    }
                }
                if (!string.IsNullOrEmpty(partyConfig.MainAvatarIndex))
                {
                    break;
                }
            }
        }

        // 默认元素力采集角色
        // var elementalCollectAvatars = ElementalCollectAvatarConfigs.Lists;
        // foreach (var avatar in combatScenes.Avatars)
        // {
        //     var elementalCollectAvatar = elementalCollectAvatars.FirstOrDefault(x => x.Name == avatar.Name);
        //     if (elementalCollectAvatar != null)
        //     {
        //         if (elementalCollectAvatar.ElementalType == ElementalType.Hydro)
        //         {
        //             partyConfig.HydroCollectAvatarIndex = avatar.Index.ToString();
        //         }
        //         else if (elementalCollectAvatar.ElementalType == ElementalType.Electro)
        //         {
        //             partyConfig.ElectroCollectAvatarIndex = avatar.Index.ToString();
        //         }
        //         else if (elementalCollectAvatar.ElementalType == ElementalType.Anemo)
        //         {
        //             partyConfig.AnemoCollectAvatarIndex = avatar.Index.ToString();
        //         }
        //     }
        // }

        return partyConfig;
    }
}

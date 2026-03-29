using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Handler;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Exceptions;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using static BetterGenshinImpact.GameTask.SystemControl;
using ActionEnum = BetterGenshinImpact.GameTask.AutoPathing.Model.Enum.ActionEnum;

namespace BetterGenshinImpact.GameTask.AutoPathing;

public class PathingPartyManager
{
    private readonly CancellationToken _ct;
    private readonly PathingHealthController _healthController;
    private CombatScenes? _combatScenes;
    private PathingPartyConfig? _partyConfig;

    public PathingPartyConfig PartyConfig
    {
        get => _partyConfig ?? PathingPartyConfig.BuildDefault();
        set => _partyConfig = value;
    }

    public CombatScenes? CombatScenes => _combatScenes;

    public PathingPartyManager(CancellationToken ct, PathingHealthController healthController, PathingPartyConfig? initialPartyConfig)
    {
        _ct = ct;
        _healthController = healthController;
        _partyConfig = initialPartyConfig;
    }

    public async Task<bool> SwitchPartyBefore(PathingTask task)
    {
        var ra = CaptureToRectArea();

        // 切换队伍前判断是否全队死亡 // 可能队伍切换失败导致的死亡
        if (Bv.ClickIfInReviveModal(ra))
        {
            await Bv.WaitForMainUi(_ct); // 等待主界面加载完成
            Logger.LogInformation("复苏完成");
            await Delay(4000, _ct);
            // 血量肯定不满，直接去七天神像回血
            await _healthController.TpStatueOfTheSevenAsync();
        }

        if (PartyConfig.SkipPartySwitch)
        {
            return true;
        }

        var pRaList = ra.FindMulti(AutoFightAssets.Instance.PRa); // 判断是否联机
        if (pRaList.Count > 0)
        {
            Logger.LogInformation("处于联机状态下，不切换队伍");
        }
        else
        {
            if (PartyConfig is { Enabled: false })
            {
                // 调度器未配置的情况下，根据地图追踪条件配置切换队伍
                var partyName = FilterPartyNameByConditionConfig(task);
                if (!await SwitchParty(partyName))
                {
                    Logger.LogError("切换队伍失败，无法执行此路径！请检查地图追踪设置！");
                    return false;
                }
            }
            else if (!string.IsNullOrEmpty(PartyConfig.PartyName))
            {
                if (!await SwitchParty(PartyConfig.PartyName))
                {
                    Logger.LogError("切换队伍失败，无法执行此路径！请检查配置组中的地图追踪配置！");
                    return false;
                }
            }
        }

        return true;
    }

    private async Task<bool> SwitchParty(string? partyName)
    {
        bool success = true;
        if (!string.IsNullOrEmpty(partyName))
        {
            if (RunnerContext.Instance.PartyName == partyName)
            {
                return success;
            }

            bool forceTp = PartyConfig.IsVisitStatueBeforeSwitchParty;

            if (forceTp) // 强制传送模式
            {
                await new TpTask(_ct).TpToStatueOfTheSeven(); // fix typos
                success = await new SwitchPartyTask().Start(partyName, _ct);
            }
            else // 优先原地切换模式
            {
                try
                {
                    success = await new SwitchPartyTask().Start(partyName, _ct);
                }
                catch (PartySetupFailedException)
                {
                    await new TpTask(_ct).TpToStatueOfTheSeven();
                    success = await new SwitchPartyTask().Start(partyName, _ct);
                }
            }

            if (success)
            {
                RunnerContext.Instance.PartyName = partyName;
                RunnerContext.Instance.ClearCombatScenes();
            }
        }

        return success;
    }

    private static string? FilterPartyNameByConditionConfig(PathingTask task)
    {
        var pathingConditionConfig = TaskContext.Instance().Config.PathingConditionConfig;
        var materialName = task.GetMaterialName();
        var specialActions = task.Positions
            .Select(p => p.Action)
            .Where(action => !string.IsNullOrEmpty(action))
            .Distinct()
            .ToList();
        var partyName = pathingConditionConfig.FilterPartyName(materialName, specialActions);
        return partyName;
    }

    public async Task<bool> ValidateGameWithTask(PathingTask task)
    {
        _combatScenes = await RunnerContext.Instance.GetCombatScenes(_ct);
        if (_combatScenes == null)
        {
            return false;
        }

        // 没有强制配置的情况下，使用地图追踪内的条件配置
        // 必须放在这里，因为要通过队伍识别来得到最终结果
        var pathingConditionConfig = TaskContext.Instance().Config.PathingConditionConfig;
        var skipPartySwitch = PartyConfig.SkipPartySwitch;
        if (PartyConfig is { Enabled: false })
        {
            PartyConfig = pathingConditionConfig.BuildPartyConfigByCondition(_combatScenes);
            PartyConfig.SkipPartySwitch = skipPartySwitch;
        }

        // 校验角色是否存在
        if (task.HasAction(ActionEnum.NahidaCollect.Code))
        {
            var avatar = _combatScenes.SelectAvatar("纳西妲");
            if (avatar == null)
            {
                Logger.LogError("此路径存在纳西妲收集动作，队伍中没有纳西妲角色，无法执行此路径！");
                return false;
            }

            // _actionAvatarIndexMap.Add("nahida_collect", avatar.Index.ToString());
        }

        // 把所有需要切换的角色编号记录下来
        Dictionary<string, ElementalType> map = new()
        {
            { ActionEnum.HydroCollect.Code, ElementalType.Hydro },
            { ActionEnum.ElectroCollect.Code, ElementalType.Electro },
            { ActionEnum.AnemoCollect.Code, ElementalType.Anemo }
        };

        foreach (var (action, el) in map)
        {
            if (!ValidateElementalActionAvatarIndex(task, action, el, _combatScenes))
            {
                return false;
            }
        }

        return true;
    }

    private bool ValidateElementalActionAvatarIndex(PathingTask task, string action, ElementalType el,
        CombatScenes combatScenes)
    {
        if (task.HasAction(action))
        {
            foreach (var avatar in combatScenes.GetAvatars())
            {
                if (ElementalCollectAvatarConfigs.Get(avatar.Name, el) != null)
                {
                    return true;
                }
            }

            Logger.LogError("此路径存在 {El}元素采集 动作，队伍中没有对应元素角色:{Names}，无法执行此路径！", el.ToChinese(),
                string.Join(",", ElementalCollectAvatarConfigs.GetAvatarNameList(el)));
            return false;
        }
        else
        {
            return true;
        }
    }

    public async Task<Avatar?> SwitchAvatar(string index, bool needSkill = false)
    {
        if (string.IsNullOrEmpty(index))
        {
            return null;
        }

        var avatar = _combatScenes?.SelectAvatar(int.Parse(index));
        if (avatar == null) return null;
        if (needSkill && !avatar.IsSkillReady())
        {
            Logger.LogInformation("角色{Name}技能未冷却，跳过。", avatar.Name);
            return null;
        }

        var success = avatar.TrySwitch(5); //多切换一次，否则如果切人纠正要等下一个循环
        if (success)
        {
            await Delay(100, _ct);
            return avatar;
        }

        Logger.LogInformation("尝试切换角色{Name}失败！", avatar.Name);
        return null;
    }
}
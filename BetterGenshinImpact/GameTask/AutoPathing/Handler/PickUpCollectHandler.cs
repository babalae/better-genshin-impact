using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using System.Linq;
using System.Collections.Generic;
using BetterGenshinImpact.GameTask.AutoFight.Config;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

    /// <summary>
/// 使用万叶或琴团通过战技吸取拾取物品，优先万叶，如果没有万叶则使用琴团
/// </summary>
public class PickUpCollectHandler : IActionHandler
{
    /// <summary>
    /// 新增命令时，以角色名称开头(必填)，“-”后定义动作(必填，用于预解析，不能单写角色名称)，空格后定义参数(必填)
    /// 1、"action": "pick_up_collect","action_params":为空或不填，在队伍中寻找CharacterNames中第一个找到的角色，找到就会执行PickUpActions第一个找到的相关角色命令。
    /// 2、"action": "pick_up_collect","action_params":"琴"，只填角色名称（或别名），会执行PickUpActions第一个找到的相关角色命令。
    /// 3、"action": "pick_up_collect","action_params":"琴-短E"，填了具体角色和动作，直接找PickUpActions找到该命令执行。
    /// </summary>
    public static readonly string[] PickUpActions =
    [
        "枫原万叶-长E keydown(E),wait(0.7),keyup(E),attack(0.2),wait(0.5)",
        "枫原万叶-短E e,attack(0.15)",
        "琴-短E wait(0.1),keydown(E),wait(0.4),moveby(1000,0),wait(0.2),moveby(1000,0),wait(0.2),moveby(1000,0),wait(0.2),moveby(1000,-3500),wait(1.8),keyup(E),wait(0.3),click(middle)",
        "琴-长E wait(0.1),click(middle),keydown(E),click(middle),wait(0.4),moveby(500,0),wait(0.1),moveby(500,0),wait(0.1)," +
        "moveby(500,0),wait(0.1),moveby(500,0),wait(0.1),moveby(500,0),wait(0.1),moveby(500,0),wait(0.1),moveby(500,0),wait(0.1)," +
        "moveby(500,0),wait(0.1),moveby(500,0),wait(0.1),moveby(500,0),wait(0.1),moveby(500,0),wait(0.1),moveby(500,0),wait(0.1)," +
        "moveby(500,0),wait(0.1),moveby(500,0),wait(0.1),moveby(500,0),wait(0.1),moveby(500,0),wait(0.1),moveby(500,0),wait(0.1)," +
        "moveby(500,0),wait(0.1),moveby(500,0),wait(0.1),moveby(500,0),wait(0.1),moveby(500,0),wait(0.1),moveby(500,0),wait(0.1)," +
        "moveby(500,0),wait(0.1),moveby(500,0),wait(0.1),moveby(500,0),wait(0.1),moveby(500,0),wait(0.1),moveby(500,0),wait(0.1)," +
        "moveby(500,0),wait(0.1),moveby(500,0),wait(0.1),moveby(500,0),wait(0.1),moveby(500,0),wait(0.1),moveby(500,0),wait(0.1)," +
        "moveby(500,0),wait(0.1),moveby(500,0),wait(0.1),moveby(500,0),wait(0.1),moveby(500,0),wait(0.1),moveby(500,0),wait(0.1)," +
        "moveby(500,0),wait(0.1),moveby(500,0),wait(0.1),moveby(500,0),moveby(1000,3500),wait(1.8),keyup(E),wait(0.3),click(middle),wait(0.3)",
    ];
    
    // 预解析所有角色名
    private static readonly HashSet<string> CharacterNames = new HashSet<string>(
        PickUpActions
            .Select(action => action.Split(' ')[0]) 
            .Select(GetBaseCharacterName)            
            .Distinct()
    );
    
    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        Logger.LogInformation("简易策略：执行 {Nhd} 动作","聚集材料");

        var combatScenes = await RunnerContext.Instance.GetCombatScenes(ct);
        if (combatScenes == null)
        {
            Logger.LogError("队伍识别未初始化成功！");
            return;
        }

        Avatar? picker = null;
        var commandsList = new List<string>();
        
        if (waypointForTrack != null)
        {
            if (!string.IsNullOrEmpty(waypointForTrack.ActionParams))
            {
                var commands = waypointForTrack.ActionParams.Split(',');
                
                foreach (var command in commands)
                {
   
                    try
                    {
                        var alias = DefaultAutoFightConfig.AvatarAliasToStandardName(command);
                        commandsList.Add(!string.IsNullOrEmpty(alias) ? alias : command);
                    }
                    catch (Exception e)
                    {
                        commandsList.Add(command);
                        Console.WriteLine(e);
                    }
                }
            }
            else
            {
                // 1、ActionParams没填参数，尝试选择，如果找到，后续会执行第一个找到该角色的相关命令
                foreach (var characterName in CharacterNames)
                {
                    var pickerNull = combatScenes.SelectAvatar(characterName);
                    if (pickerNull is null)
                    {
                        continue;
                    }
                    commandsList.Add(characterName);
                    break;
                }
            }
        }

        foreach (var commands in commandsList)
        {
            if (CharacterNames.Contains(commands))
            {
                picker = combatScenes.SelectAvatar(commands);
            }
            else
            {
                var characterName = GetCharacterName(commands);
                picker = combatScenes.SelectAvatar(characterName);
            }

            if (picker is not null)
            {
                picker.TrySwitch();
                await picker.WaitSkillCd(ct);
            }
            else
            {
                continue;
            }
            
            PickUpMaterial(combatScenes,commands); // 开始执行动作
        }
    }
    
    /// <summary>
    /// 执行聚集材料动作
    /// <param name="combatScenes"></param>
    /// <param name="pickerName"></param>
    /// </summary>
   private void PickUpMaterial(CombatScenes combatScenes, string? pickerName = null)
    {
        try
        {
            var foundAvatar = false;
            string[] actionsToUse;
            var characterName = string.Empty;
            
            if (pickerName != null)
            {
                actionsToUse = PickUpActions.Where(action => 
                    action.StartsWith(pickerName + " ", StringComparison.OrdinalIgnoreCase)).ToArray();
                
                if (actionsToUse.Length == 0)
                {
                    if (CharacterNames.Contains(pickerName)) //2.只填了角色名，则用基础角色名筛选，执行pickerName相关的第一个命令
                    {
                        var actions = PickUpActions.FirstOrDefault(action => action.StartsWith(pickerName, StringComparison.OrdinalIgnoreCase));
                        actionsToUse = actions == null ? new string[0] : new string[] {actions};
                        
                        // 替换第一个空格前的字符为 pickerName
                        if (actionsToUse.Length > 0 && actionsToUse[0].Contains(' '))
                        {
                            string action = actionsToUse[0];
                            int firstSpaceIndex = action.IndexOf(' ');
                            actionsToUse[0] = pickerName + action.Substring(firstSpaceIndex);
                        }
                    }
                    else
                    {
                        Logger.LogError($"未找到角色 {pickerName} 对应的动作");
                        return; 
                    }
                }
                else
                {
                    // 提取角色名称
                    characterName = GetCharacterName(pickerName);

                    // 3.填了具体命令，则用具体命令筛选，并将命令中的角色替换为角色名称
                    actionsToUse = actionsToUse
                        .Select(action => action.Replace(pickerName + " ", characterName + " ", StringComparison.OrdinalIgnoreCase))
                        .ToArray(); 
                }
            }
            else
            {
                Logger.LogError("未找到ActionParams");
                return;
            }

            foreach (var pickUpActionStr in actionsToUse)
            {
                var pickUpAction = CombatScriptParser.ParseContext(pickUpActionStr);
                foreach (var command in pickUpAction.CombatCommands)
                {
                    var avatar = combatScenes.SelectAvatar(command.Name);
                    if (avatar != null)
                    {
                        command.Execute(combatScenes);
                        foundAvatar = true;
                    }
                }
                if (foundAvatar)
                {
                    var selectedAvatar = combatScenes.SelectAvatar(characterName);
                    if (selectedAvatar is not null)
                    { 
                        Sleep(200);//等待CD显示
                        selectedAvatar.AfterUseSkill();
                    }
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            // 处理异常
            Console.WriteLine($"PickUpCollectHandler 异常: {ex.Message}");
        }
    }
    
    // 直接匹配预解析的角色名
    private static string GetCharacterName(string pickerName)
    {
        foreach (var name in CharacterNames)
        {
            if (pickerName.StartsWith(name))
                return name;
        }
        
        return pickerName;
    }
    
    /// <summary>
    /// 从完整动作名提取基础角色名
    /// </summary>
    private static string GetBaseCharacterName(string fullActionName)
    {
        // 找到第一个"-"号的位置
        var dashIndex = fullActionName.IndexOf('-');

        // 如果存在"-"号，则返回"-"号前的部分
        return dashIndex > 0 ? fullActionName.Substring(0, dashIndex) : string.Empty;
    }

}

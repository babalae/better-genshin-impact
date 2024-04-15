using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.Model;
using System.Threading.Tasks;
using System.Threading;
using BetterGenshinImpact.Core.Config;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.Service;

namespace BetterGenshinImpact.GameTask.AutoFight;

/// <summary>
/// 一键战斗宏
/// </summary>
public class OneKeyFightTask : Singleton<OneKeyFightTask>
{
    public static readonly string HoldOnMode = "按住时重复";
    public static readonly string TickMode = "触发";

    private Dictionary<string, List<CombatCommand>>? _avatarMacros;
    private CancellationTokenSource _cts = new();
    private Task? _fightTask;

    public void Run()
    {
        if (!IsEnabled())
        {
            return;
        }
        _avatarMacros ??= LoadAvatarMacros();

        if (IsHoldOnMode())
        {
            if (_fightTask == null || _fightTask.IsCompleted)
            {
                _fightTask = FightTask(_cts);
                _fightTask.Start();
            }
            Thread.Sleep(100);
        }
        else if (IsTickMode())
        {
            if (_cts.Token.IsCancellationRequested)
            {
                _cts = new CancellationTokenSource();
                Task.Run(() => FightTask(_cts));
            }
            else
            {
                _cts.Cancel();
            }
        }
    }

    /// <summary>
    /// 循环执行战斗宏
    /// </summary>
    private Task FightTask(CancellationTokenSource cts)
    {
        var content = GetContentFromDispatcher();
        var combatScenes = new CombatScenes().InitializeTeam(content);
        // 找到出战角色
        var activeAvatar = combatScenes.Avatars.First(avatar => avatar.IsActive(content));

        if (_avatarMacros != null && _avatarMacros.TryGetValue(activeAvatar.Name, out var combatCommands))
        {
            return new Task(() =>
            {
                while (!cts.Token.IsCancellationRequested && IsEnabled())
                {
                    // 通用化战斗策略
                    foreach (var command in combatCommands)
                    {
                        command.Execute(combatScenes);
                    }
                }
            });
        }
        else
        {
            return Task.CompletedTask;
        }
    }

    public static Dictionary<string, List<CombatCommand>> LoadAvatarMacros()
    {
        var json = File.ReadAllText(Global.Absolute("User/avatar_macro.json"));
        var avatarMacros = JsonSerializer.Deserialize<List<AvatarMacro>>(json, ConfigService.JsonOptions);
        if (avatarMacros == null)
        {
            return new Dictionary<string, List<CombatCommand>>();
        }
        var result = new Dictionary<string, List<CombatCommand>>();
        foreach (var avatarMacro in avatarMacros)
        {
            var commands = avatarMacro.LoadCommands();
            if (commands != null)
            {
                result.Add(avatarMacro.Name, commands);
            }
        }
        return result;
    }

    public static bool IsEnabled()
    {
        return TaskContext.Instance().Config.MacroConfig.CombatMacroEnabled;
    }

    public static bool IsHoldOnMode()
    {
        return TaskContext.Instance().Config.MacroConfig.CombatMacroHotkeyMode == HoldOnMode;
    }

    public static bool IsTickMode()
    {
        return TaskContext.Instance().Config.MacroConfig.CombatMacroHotkeyMode == TickMode;
    }
}

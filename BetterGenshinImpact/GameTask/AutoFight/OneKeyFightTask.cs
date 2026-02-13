using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoFight;

/// <summary>
/// 一键战斗宏
/// </summary>
public class OneKeyFightTask : Singleton<OneKeyFightTask>
{
    public static readonly string HoldOnMode = "按住时重复";
    public static readonly string TickMode = "触发";

    private Dictionary<string, List<CombatCommand>>? _avatarMacros;
    private CancellationTokenSource? _cts = null;
    private Task? _fightTask;

    private bool _isKeyDown = false;
    private int _activeMacroPriority = -1;
    private DateTime _lastUpdateTime = DateTime.MinValue;

    private CombatScenes? _currentCombatScenes;

    public void KeyDown()
    {
        if (_isKeyDown || !IsEnabled())
        {
            return;
        }

        _isKeyDown = true;
        if (_activeMacroPriority != TaskContext.Instance().Config.MacroConfig.CombatMacroPriority ||
            IsAvatarMacrosEdited())
        {
            _activeMacroPriority = TaskContext.Instance().Config.MacroConfig.CombatMacroPriority;
            _avatarMacros = LoadAvatarMacros();
            Logger.LogInformation("加载一键宏配置完成");
        }

        if (IsHoldOnMode())
        {
            if (_cts == null || _cts.Token.IsCancellationRequested)
            {
                _cts = new CancellationTokenSource();
                _fightTask = FightTask(_cts.Token);
                if (!_fightTask.IsCompleted)
                {
                    _fightTask.Start();
                }
            }
        }
        else if (IsTickMode())
        {
            if (_cts == null || _cts.Token.IsCancellationRequested)
            {
                _cts = new CancellationTokenSource();
                _fightTask = FightTask(_cts.Token);
                if (!_fightTask.IsCompleted)
                {
                    _fightTask.Start();
                }
            }
            else
            {
                _cts.Cancel();
            }
        }
    }

    public void KeyUp()
    {
        _isKeyDown = false;
        if (!IsEnabled())
        {
            return;
        }

        if (IsHoldOnMode())
        {
            _cts?.Cancel();
        }
    }

    // public void Run()
    // {
    //     if (!IsEnabled())
    //     {
    //         return;
    //     }
    //     _avatarMacros ??= LoadAvatarMacros();
    //
    //     if (IsHoldOnMode())
    //     {
    //         if (_fightTask == null || _fightTask.IsCompleted)
    //         {
    //             _fightTask = FightTask(_cts);
    //             _fightTask.Start();
    //         }
    //         Thread.Sleep(100);
    //     }
    //     else if (IsTickMode())
    //     {
    //         if (_cts.Token.IsCancellationRequested)
    //         {
    //             _cts = new CancellationTokenSource();
    //             Task.Run(() => FightTask(_cts));
    //         }
    //         else
    //         {
    //             _cts.Cancel();
    //         }
    //     }
    // }

    /// <summary>
    /// 循环执行战斗宏
    /// </summary>
    private Task FightTask(CancellationToken ct)
    {
        var imageRegion = CaptureToRectArea();
        var combatScenes = new CombatScenes().InitializeTeam(imageRegion);
        if (!combatScenes.CheckTeamInitialized())
        {
            if (_currentCombatScenes == null)
            {
                Logger.LogError("首次队伍角色识别失败");
                return Task.CompletedTask;
            }
            else
            {
                Logger.LogWarning("队伍角色识别失败，使用上次识别结果，队伍未切换时无影响");
            }
        }
        else
        {
            _currentCombatScenes = combatScenes;
        }

        // 找到出战角色
        // var activeAvatar = _currentCombatScenes.GetAvatars().First(avatar => avatar.IsActive(imageRegion));
        var avatarName = _currentCombatScenes.CurrentAvatar(true, imageRegion, ct);
        if (avatarName is null)
        {
            Logger.LogError("无法识别出战角色");
            return Task.CompletedTask;
        }

        var activeAvatar = _currentCombatScenes.SelectAvatar(avatarName);
        if (activeAvatar is null)
        {
            Logger.LogError("获取出战角色{Name}失败", avatarName);
            return Task.CompletedTask;
        }

        if (_avatarMacros != null && _avatarMacros.TryGetValue(activeAvatar.Name, out var combatCommands))
        {
            return new Task(() =>
            {
                var round = 1;
                while (!ct.IsCancellationRequested && IsEnabled())
                {
                    Logger.LogInformation("→ {Name}执行宏 (第{Round}轮)", activeAvatar.Name, round);
                    if (IsHoldOnMode() && !_isKeyDown)
                    {
                        break;
                    }

                    // 通用化战斗策略
                    foreach (var command in combatCommands)
                    {
                        if (command.ActivatingRound != null && command.ActivatingRound.Count > 0 && !command.ActivatingRound.Contains(round))
                        {
                            // 跳过强制首轮指令
                            continue;
                        }
                        command.Execute(activeAvatar);
                    }
                    round++;
                }

                Logger.LogInformation("→ {Name}停止宏", activeAvatar.Name);
            });
        }
        else
        {
            Logger.LogWarning("→ {Name}配置[{Priority}]为空，请先配置一键宏", activeAvatar.Name, _activeMacroPriority);
            return Task.CompletedTask;
        }
    }

    public Dictionary<string, List<CombatCommand>> LoadAvatarMacros()
    {
        var jsonPath = GetAvatarMacroJsonPath();
        var json = File.ReadAllText(jsonPath);
        _lastUpdateTime = File.GetLastWriteTime(jsonPath);
        var avatarMacros = JsonSerializer.Deserialize<List<AvatarMacro>>(json, ConfigService.JsonOptions);
        if (avatarMacros == null)
        {
            return [];
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

    public bool IsAvatarMacrosEdited()
    {
        // 通过修改时间判断是否编辑过
        var jsonPath = GetAvatarMacroJsonPath();
        var lastWriteTime = File.GetLastWriteTime(jsonPath);
        return lastWriteTime > _lastUpdateTime;
    }
    
    public static string GetAvatarMacroJsonPath()
    {
        var path = Global.Absolute("User/avatar_macro.json");
        if (!File.Exists(path))
        {
            File.Copy(Global.Absolute("User/avatar_macro_default.json"), path);
        }
        return path;
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
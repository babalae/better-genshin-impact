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
    public static readonly string HoldOnMode = "按住时重复(新)";
    public static readonly string HoldFinishMode = "按住时重复(旧)";
    public static readonly string TickMode = "触发";

    private Dictionary<string, List<CombatCommand>>? _avatarMacros;
    private CancellationTokenSource? _cts = null;
    private Task? _fightTask;

    private volatile bool _isKeyDown = false;
    private int _activeMacroPriority = -1;
    private DateTime _lastUpdateTime = DateTime.MinValue;

    private CombatScenes? _currentCombatScenes;
    private Avatar? _lastMacroAvatar;
    private readonly object _pressedKeysLock = new();
    private readonly HashSet<string> _pressedKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pressedMouseKeys = new(StringComparer.OrdinalIgnoreCase);

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

        if (IsHoldOnMode() || IsHoldFinishMode())
        {
            if (_cts == null || _cts.Token.IsCancellationRequested)
            {
                _cts = new CancellationTokenSource();
                _fightTask = FightTask(_cts.Token, IsHoldOnMode());
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
                _fightTask = FightTask(_cts.Token, false);
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

        if (IsHoldOnMode() || IsHoldFinishMode())
        {
            _cts?.Cancel();
            if (IsHoldOnMode())
            {
                // 新一键宏允许指令保持按下状态，松开热键时需要立即释放残留按键。
                ReleasePressedMacroKeys();
            }
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
    private Task FightTask(CancellationToken ct, bool releasePressedKeysOnStop)
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
        if (releasePressedKeysOnStop)
        {
            // 新一键宏停止时要用同一个角色对象补发 KeyUp/MouseUp。
            _lastMacroAvatar = activeAvatar;
        }

        if (_avatarMacros != null && _avatarMacros.TryGetValue(activeAvatar.Name, out var combatCommands))
        {
            if (!releasePressedKeysOnStop)
            {
                return new Task(() =>
                {
                    var round = 1;
                    while (!ct.IsCancellationRequested && IsEnabled())
                    {
                        Logger.LogInformation("→ {Name}执行宏 (第{Round}轮)", activeAvatar.Name, round);
                        if ((IsHoldOnMode() || IsHoldFinishMode()) && !_isKeyDown)
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

            // 新一键宏会追踪宏内按下的键，避免取消任务后键盘或鼠标状态残留。
            return new Task(() =>
            {
                try
                {
                    var round = 1;
                    while (!ct.IsCancellationRequested && IsEnabled())
                    {
                        Logger.LogInformation("→ {Name}执行宏 (第{Round}轮)", activeAvatar.Name, round);
                        if ((IsHoldOnMode() || IsHoldFinishMode()) && !_isKeyDown)
                        {
                            break;
                        }

                        // 通用化战斗策略
                        foreach (var command in combatCommands)
                        {
                            if (releasePressedKeysOnStop && (ct.IsCancellationRequested || !_isKeyDown))
                            {
                                // 新一键宏松开热键后不再继续执行后续指令，直接进入 finally 释放按键。
                                break;
                            }

                            if (command.ActivatingRound != null && command.ActivatingRound.Count > 0 && !command.ActivatingRound.Contains(round))
                            {
                                // 跳过强制首轮指令
                                continue;
                            }
                            ExecuteCommand(activeAvatar, command);
                        }
                        round++;
                    }
                }
                finally
                {
                    if (releasePressedKeysOnStop)
                    {
                        ReleasePressedMacroKeys(activeAvatar);
                    }

                    Logger.LogInformation("→ {Name}停止宏", activeAvatar.Name);
                }
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

    public static bool IsHoldFinishMode()
    {
        return TaskContext.Instance().Config.MacroConfig.CombatMacroHotkeyMode == HoldFinishMode;
    }

    public static bool IsTickMode()
    {
        return TaskContext.Instance().Config.MacroConfig.CombatMacroHotkeyMode == TickMode;
    }

    /// 新一键宏执行指令时记录按下状态，便于停止时释放未抬起的键。
    private void ExecuteCommand(Avatar avatar, CombatCommand command)
    {
        command.Execute(avatar);

        if (command.Method == Method.KeyDown)
        {
            TrackPressedKey(command.Args![0]);
        }
        else if (command.Method == Method.KeyUp)
        {
            TrackReleasedKey(command.Args![0]);
        }
        else if (command.Method == Method.MouseDown)
        {
            TrackPressedMouseKey(command.Args is { Count: > 0 } ? command.Args[0] : "left");
        }
        else if (command.Method == Method.MouseUp)
        {
            TrackReleasedMouseKey(command.Args is { Count: > 0 } ? command.Args[0] : "left");
        }
    }

    private void TrackPressedKey(string key)
    {
        lock (_pressedKeysLock)
        {
            _pressedKeys.Add(key);
        }
    }

    private void TrackReleasedKey(string key)
    {
        lock (_pressedKeysLock)
        {
            _pressedKeys.Remove(key);
        }
    }

    private void TrackPressedMouseKey(string key)
    {
        lock (_pressedKeysLock)
        {
            _pressedMouseKeys.Add(key);
        }
    }

    private void TrackReleasedMouseKey(string key)
    {
        lock (_pressedKeysLock)
        {
            _pressedMouseKeys.Remove(key);
        }
    }

    /// <summary>
    /// 释放新一键宏记录的键盘和鼠标按下状态。
    /// </summary>
    private void ReleasePressedMacroKeys(Avatar? avatar = null)
    {
        string[] keys;
        string[] mouseKeys;
        lock (_pressedKeysLock)
        {
            keys = [.. _pressedKeys];
            mouseKeys = [.. _pressedMouseKeys];
            _pressedKeys.Clear();
            _pressedMouseKeys.Clear();
        }

        var releaseAvatar = avatar ?? _lastMacroAvatar;
        if (releaseAvatar == null)
        {
            return;
        }

        foreach (var key in keys)
        {
            releaseAvatar.KeyUp(key);
        }

        foreach (var mouseKey in mouseKeys)
        {
            releaseAvatar.MouseUp(mouseKey);
        }
    }
}
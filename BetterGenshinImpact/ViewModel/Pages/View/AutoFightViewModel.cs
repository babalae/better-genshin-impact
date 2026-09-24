using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoFight;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Diagnostics;
using System.IO;

namespace BetterGenshinImpact.ViewModel.Pages.View;

public partial class AutoFightViewModel : ObservableObject, IViewModel
{
    public AllConfig Config { get; set; }

    public AutoFightViewModel()
    {
        Config = TaskContext.Instance().Config;
        _strategyList = LoadCustomScript(Global.Absolute(@"User\AutoGeniusInvokation"));
        _combatStrategyList = BuildCombatStrategyList();
        _combatStrategyListWithoutCombo = BuildCombatStrategyListWithoutCombo();
    }

    public AutoFightViewModel(AllConfig config)
    {
        Config = config;
        _strategyList = LoadCustomScript(Global.Absolute(@"User\AutoGeniusInvokation"));
        _combatStrategyList = BuildCombatStrategyList();
        _combatStrategyListWithoutCombo = BuildCombatStrategyListWithoutCombo();
    }

    /// <summary>战斗策略下拉列表：固定项（根据队伍自动选择 / 自动连招）+ 用户自定义策略</summary>
    private string[] BuildCombatStrategyList()
    {
        return ["根据队伍自动选择", AutoFightParam.ComboStrategyName, .. LoadCustomScript(Global.Absolute(@"User\AutoFight"))];
    }

    /// <summary>战斗策略下拉列表（不含自动连招）：供暂未接入自动连招的任务（首领讨伐/幽境危战/地脉花）使用</summary>
    private string[] BuildCombatStrategyListWithoutCombo()
    {
        return ["根据队伍自动选择", .. LoadCustomScript(Global.Absolute(@"User\AutoFight"))];
    }

    [ObservableProperty]
    private string[] _combatStrategyList;

    [ObservableProperty]
    private string[] _combatStrategyListWithoutCombo;

    [ObservableProperty]
    private string[] _strategyList;

    private string[] LoadCustomScript(string folder)
    {
        Directory.CreateDirectory(folder);
        var files = Directory.GetFiles(folder, "*.*",
            SearchOption.AllDirectories);

        var count = 0;
        foreach (var file in files)
        {
            if (file.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                count++;
        }

        var strategyList = new string[count];
        var idx = 0;
        foreach (var file in files)
        {
            string? ext = null;
            if (file.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                ext = ".txt";
            }
            else if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                ext = ".json";
            }

            if (ext != null)
            {
                var relativePath = Path.GetRelativePath(folder, file);
                var strategyName = Path.ChangeExtension(relativePath, null);
                if (strategyName.StartsWith('\\') || strategyName.StartsWith('/'))
                {
                    strategyName = strategyName[1..];
                }

                strategyList[idx++] = strategyName;
            }
        }

        return strategyList;
    }

    [RelayCommand]
    public void OnStrategyDropDownOpened(string type)
    {
        switch (type)
        {
            case "Combat":
                CombatStrategyList = BuildCombatStrategyList();
                CombatStrategyListWithoutCombo = BuildCombatStrategyListWithoutCombo();
                break;

            case "GeniusInvocation":
                StrategyList = LoadCustomScript(Global.Absolute(@"User\AutoGeniusInvokation"));
                break;
        }
    }

    [RelayCommand]
    public void OnOpenLocalScriptRepo()
    {
        Config.ScriptConfig.ScriptRepoHintDotVisible = false;
        ScriptRepoUpdater.Instance.OpenScriptRepoWindow();
    }

    [RelayCommand]
    public void OnOpenFightFolder()
    {
        Process.Start("explorer.exe", Global.Absolute(@"User\AutoFight\"));
    }
}
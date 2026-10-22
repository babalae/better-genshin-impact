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

    public AutoFightViewModel(AllConfig config)
    {
        Config = config;
        _strategyList = LoadCustomScript(Global.Absolute(@"User\AutoGeniusInvokation"));
    }

    // 战斗策略目录由 CombatStrategyPicker 按需加载；此处只保留七圣召唤列表。
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
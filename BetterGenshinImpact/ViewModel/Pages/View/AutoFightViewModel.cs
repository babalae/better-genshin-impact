using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Wpf.Ui;

namespace BetterGenshinImpact.ViewModel.Pages.View;

public partial class AutoFightViewModel : ObservableObject, IViewModel
{
    public AllConfig Config { get; set; }

    public AutoFightViewModel()
    {
        Config = TaskContext.Instance().Config;
        _strategyList = LoadCustomScript(Global.Absolute(@"User\AutoGeniusInvokation"));
        _combatStrategyList = ["根据队伍自动选择", .. LoadCustomScript(Global.Absolute(@"User\AutoFight"))];
    }

    public AutoFightViewModel(AllConfig config)
    {
        Config = config;
        _strategyList = LoadCustomScript(Global.Absolute(@"User\AutoGeniusInvokation"));
        _combatStrategyList = ["根据队伍自动选择", .. LoadCustomScript(Global.Absolute(@"User\AutoFight"))];
    }

    [ObservableProperty]
    private string[] _combatStrategyList;

    [ObservableProperty]
    private string[] _strategyList;

    private string[] LoadCustomScript(string folder)
    {
        var files = Directory.GetFiles(folder, "*.*",
            SearchOption.AllDirectories);

        var strategyList = new string[files.Length];
        for (var i = 0; i < files.Length; i++)
        {
            if (files[i].EndsWith(".txt"))
            {
                var strategyName = files[i].Replace(folder, "").Replace(".txt", "");
                if (strategyName.StartsWith('\\'))
                {
                    strategyName = strategyName[1..];
                }

                strategyList[i] = strategyName;
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
                CombatStrategyList = ["根据队伍自动选择", .. LoadCustomScript(Global.Absolute(@"User\AutoFight"))];
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
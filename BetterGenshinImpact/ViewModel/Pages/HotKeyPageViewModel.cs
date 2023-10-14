using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Input;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.QuickEnhanceArtifact;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;
using CommunityToolkit.Mvvm.ComponentModel;
using HotKeySettingModel = BetterGenshinImpact.Model.HotKeySettingModel;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class HotKeyPageViewModel : ObservableObject
{
    public AllConfig Config { get; set; }

    [ObservableProperty] private ObservableCollection<HotKeySettingModel> _hotKeySettingModels = new();

    public HotKeyPageViewModel(IConfigService configService)
    {
        // 获取配置
        Config = configService.Get();

        // 构建快捷键配置列表
        BuildHotKeySettingModelList();

        foreach (var hotKeyConfig in HotKeySettingModels)
        {
            hotKeyConfig.RegisterHotKey();
            hotKeyConfig.PropertyChanged += (sender, e) =>
            {
                if (sender is HotKeySettingModel model)
                {
                    Debug.WriteLine($"{model.FunctionName} 快捷键变更为 {model.HotKey}");

                    // 反射更新配置
                    var pi = Config.HotKeyConfig.GetType().GetProperty(model.ConfigPropertyName, BindingFlags.Public | BindingFlags.Instance);
                    if (null != pi && pi.CanWrite)
                    {
                        pi.SetValue(Config.HotKeyConfig, model.HotKey.ToString(), null);
                    }

                    model.UnRegisterHotKey();
                    model.RegisterHotKey();
                }
            };
        }
    }

    private void BuildHotKeySettingModelList()
    {
        var autoPickEnabledHotKeySettingModel = new HotKeySettingModel(
            "自动拾取",
            nameof(Config.HotKeyConfig.AutoPickEnabledHotkey),
            Config.HotKeyConfig.AutoPickEnabledHotkey,
            hotKey =>
            {
                TaskContext.Instance().Config.AutoPickConfig.Enabled = !TaskContext.Instance().Config.AutoPickConfig.Enabled;
            }
        );
        HotKeySettingModels.Add(autoPickEnabledHotKeySettingModel);

        var autoSkipEnabledHotKeySettingModel = new HotKeySettingModel(
            "自动剧情",
            nameof(Config.HotKeyConfig.AutoSkipEnabledHotkey),
            Config.HotKeyConfig.AutoSkipEnabledHotkey,
            hotKey =>
            {
                TaskContext.Instance().Config.AutoSkipConfig.Enabled = !TaskContext.Instance().Config.AutoSkipConfig.Enabled;
            }
        );
        HotKeySettingModels.Add(autoSkipEnabledHotKeySettingModel);

        var autoFishingEnabledHotKeySettingModel = new HotKeySettingModel(
            "自动钓鱼",
            nameof(Config.HotKeyConfig.AutoFishingEnabledHotkey),
            Config.HotKeyConfig.AutoFishingEnabledHotkey,
            hotKey =>
            {
                TaskContext.Instance().Config.AutoFishingConfig.Enabled = !TaskContext.Instance().Config.AutoFishingConfig.Enabled;
            }
        );
        HotKeySettingModels.Add(autoFishingEnabledHotKeySettingModel);

        var turnAroundHotKeySettingModel = new HotKeySettingModel(
            "那维莱特 - 转圈圈",
            nameof(Config.HotKeyConfig.TurnAroundHotkey),
            Config.HotKeyConfig.TurnAroundHotkey,
            hotKey => { QuickEnhanceArtifact.Done(); }
        );
        HotKeySettingModels.Add(turnAroundHotKeySettingModel);

        var enhanceArtifactHotKeySettingModel = new HotKeySettingModel(
            "快速强化圣遗物",
            nameof(Config.HotKeyConfig.EnhanceArtifactHotkey),
            Config.HotKeyConfig.EnhanceArtifactHotkey,
            hotKey => { Debug.WriteLine(Config.HotKeyConfig.EnhanceArtifactHotkey); }
        );
        HotKeySettingModels.Add(enhanceArtifactHotKeySettingModel);
    }
}
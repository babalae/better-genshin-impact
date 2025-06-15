using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.Service.Interface;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.View.Controls.Drawer;
using BetterGenshinImpact.ViewModel.Message;
using CommunityToolkit.Mvvm.Messaging;
using Wpf.Ui;
using Wpf.Ui.Violeta.Controls;
using Button = Wpf.Ui.Controls.Button;
using StackPanel = Wpf.Ui.Controls.StackPanel;
using TextBlock = Wpf.Ui.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class JsListViewModel : ViewModel
{
    private readonly ILogger<JsListViewModel> _logger = App.GetLogger<JsListViewModel>();
    private readonly string scriptPath = Global.ScriptPath();

    [ObservableProperty]
    private ObservableCollection<ScriptProject> _scriptItems = [];

    private readonly IScriptService _scriptService;

    public AllConfig Config { get; set; }
    
    public DrawerViewModel DrawerVm { get; } = new DrawerViewModel();


    public JsListViewModel(IScriptService scriptService, IConfigService configService)
    {
        _scriptService = scriptService;
        Config = configService.Get();

        // 注册消息
        WeakReferenceMessenger.Default.Register<RefreshDataMessage>(this, (r, m) => InitScriptListViewData());
    }

    private void InitScriptListViewData()
    {
        ScriptItems.Clear();
        var directoryInfos = LoadScriptFolder(scriptPath);
        foreach (var f in directoryInfos)
        {
            try
            {
                ScriptItems.Add(new ScriptProject(f.Name));
            }
            catch (Exception e)
            {
                Toast.Warning($"脚本 {f.Name} 载入失败：{e.Message}");
            }
        }
    }

    private IEnumerable<DirectoryInfo> LoadScriptFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var di = new DirectoryInfo(folder);

        return di.GetDirectories();
    }

    public override void OnNavigatedTo()
    {
        InitScriptListViewData();
    }

    [RelayCommand]
    public void OnOpenScriptsFolder()
    {
        Process.Start("explorer.exe", scriptPath);
    }

    [RelayCommand]
    public void OnOpenScriptProjectFolder(ScriptProject? item)
    {
        Process.Start("explorer.exe", item == null ? scriptPath : item.ProjectPath);
    }

    [RelayCommand]
    public async Task OnStartRun(ScriptProject? item)
    {
        if (item == null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(item.Manifest.SettingsUi))
        {
            Toast.Information("此脚本存在配置，不配置可能无法正常运行，建议请添加至【调度器】，并右键修改配置后使用！");
            _logger.LogWarning("此脚本存在配置，可能无法直接从脚本界面运行，建议请添加至【调度器】，并右键修改配置后使用！");
        }

        await _scriptService.RunMulti([new ScriptGroupProject(item)]);
    }

    [RelayCommand]
    public void OnRefresh(ScriptProject? item)
    {
        InitScriptListViewData();
    }

    [RelayCommand]
    public void OnGoToJsScriptUrl()
    {
        Process.Start(new ProcessStartInfo("https://bettergi.com/feats/autos/jsscript.html") { UseShellExecute = true });
    }

    [RelayCommand]
    public void OnOpenLocalScriptRepo()
    {
        Config.ScriptConfig.ScriptRepoHintDotVisible = false;
        ScriptRepoUpdater.Instance.OpenScriptRepoWindow();
    }
    
    [RelayCommand]
    private void OpenScriptDetailDrawer(object scriptItem)
    {
        if (scriptItem == null) return;
    
        // 设置抽屉位置和大小
        DrawerVm.DrawerPosition = DrawerPosition.Right;
        DrawerVm.DrawerWidth = 400;
    
        // 创建要在抽屉中显示的内容
        var content = CreateScriptDetailContent(scriptItem);
    
        // 打开抽屉
        DrawerVm.OpenDrawer(content);
    }

    private object CreateScriptDetailContent(object scriptItem)
    {
        // 创建显示脚本详情的控件
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)),
            Padding = new Thickness(20)
        };
        var panel = new StackPanel();
        border.Child = panel;
    
        // 假设scriptItem是你的脚本对象，根据实际类型进行调整
        if (scriptItem is ScriptProject script)
        {
            panel.Children.Add(new TextBlock { 
                Text = script.Manifest.Name, 
                FontSize = 20, 
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            });
        
            panel.Children.Add(new TextBlock { 
                Text = $"版本: {script.Manifest.Version}", 
                Margin = new Thickness(0, 5, 0, 5)
            });
        
            panel.Children.Add(new TextBlock { 
                Text = script.Manifest.Description, 
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 15)
            });
        
            // 添加操作按钮
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal };
        
            var runButton = new Button { 
                Content = "执行脚本", 
                Margin = new Thickness(0, 0, 10, 0)
            };
            runButton.Click += async (s, e) =>  await OnStartRun(script);
            buttonPanel.Children.Add(runButton);
        
            var openFolderButton = new Button { Content = "打开目录" };
            openFolderButton.Click += (s, e) => OnOpenScriptProjectFolder(script);
            buttonPanel.Children.Add(openFolderButton);
        
            panel.Children.Add(buttonPanel);
        }
    
        return border;
    }
}

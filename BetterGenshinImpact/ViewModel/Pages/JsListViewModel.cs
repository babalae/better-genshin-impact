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
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.View.Controls.Drawer;
using BetterGenshinImpact.View.Controls.Webview;
using BetterGenshinImpact.ViewModel.Message;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Web.WebView2.Wpf;
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

    [ObservableProperty] private ObservableCollection<ScriptProject> _scriptItems = [];

    private readonly IScriptService _scriptService;

    public AllConfig Config { get; set; }

    public DrawerViewModel DrawerVm { get; } = new DrawerViewModel();

    private WebView2? _webView2;

    private WebpagePanel? _mdWebpagePanel;

    private TaskCompletionSource<bool>? _navigationCompletionSource;
    private const int NavigationTimeoutMs = 10000; // 10秒超时
    
    [ObservableProperty] 
    private ScriptProject? _selectedScriptProject;
    
    [ObservableProperty]
    private bool _isRightClickSelection;

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
        Process.Start(new ProcessStartInfo("https://bettergi.com/feats/autos/jsscript.html")
            { UseShellExecute = true });
    }

    [RelayCommand]
    public void OnOpenLocalScriptRepo()
    {
        Config.ScriptConfig.ScriptRepoHintDotVisible = false;
        ScriptRepoUpdater.Instance.OpenScriptRepoWindow();
    }

    [RelayCommand]
    private void SetRightClickSelection(string isRightClick)
    {
        IsRightClickSelection = "True".Equals(isRightClick, StringComparison.OrdinalIgnoreCase);
    }
    
    [RelayCommand]
    private void OpenScriptDetailDrawer(object? scriptItem)
    {
        if (scriptItem == null || IsRightClickSelection)
        {
            return;
        }

        if (scriptItem is ScriptProject scriptProject)
        {
            // 检查是否存在README.md或其他md文件
            var mdFilePath = FindMdFilePath(scriptProject);

            // 设置抽屉位置和大小
            DrawerVm.DrawerPosition = DrawerPosition.Right;

            if (!string.IsNullOrEmpty(mdFilePath))
            {
                DrawerVm.DrawerWidth = 450;
                // 注册抽屉关闭前事件
                DrawerVm.SetDrawerClosingAction(args =>
                {
                    if (_mdWebpagePanel != null)
                    {
                        _mdWebpagePanel.Visibility = Visibility.Hidden;
                    }
                });
                DrawerVm.setDrawerOpenedAction(async () =>
                {
                    SelectedScriptProject = null;
                    if (_mdWebpagePanel != null)
                    {
                        // 等待导航完成或超时
                        try
                        {
                            await WaitForNavigationCompletedWithTimeout();
                            _mdWebpagePanel.Visibility = Visibility.Visible;
                            _mdWebpagePanel.WebView.Focus();
                            Debug.WriteLine("Navigation completed successfully");
                            // 导航成功完成后执行其他操作
                        }
                        catch (TimeoutException)
                        {
                            Toast.Error("Markdown内容加载超时");
                        }
                    }
                });
            }
            else
            {
                DrawerVm.SetDrawerClosingAction(_ => { });
                DrawerVm.setDrawerOpenedAction(() =>
                {
                    SelectedScriptProject = null;
                });
                DrawerVm.DrawerWidth = 300;
            }

            // 创建要在抽屉中显示的内容
            var content = CreateScriptDetailContent(scriptProject, mdFilePath);

            // 打开抽屉
            DrawerVm.OpenDrawer(content);
        }
    }

    private async Task WaitForNavigationCompletedWithTimeout()
    {
        var completedTask = await Task.WhenAny(
            _navigationCompletionSource!.Task,
            Task.Delay(NavigationTimeoutMs)
        );

        if (completedTask != _navigationCompletionSource.Task)
        {
            throw new TimeoutException("Navigation did not complete within the timeout period");
        }
    }

    private object CreateScriptDetailContent(ScriptProject scriptProject, string? mdFilePath)
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
        panel.Children.Add(new TextBlock
        {
            Text = scriptProject.Manifest.Name,
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 10)
        });


        // 如果找到md文件，使用WebpagePanel显示
        if (!string.IsNullOrEmpty(mdFilePath))
        {
            // 使用Grid作为容器来实现填充效果
            var grid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 15)
            };
            
            _mdWebpagePanel = new WebpagePanel
            {
                Margin = new Thickness(0),
                Visibility = Visibility.Hidden,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            
            _navigationCompletionSource = new TaskCompletionSource<bool>();
            _mdWebpagePanel.OnNavigationCompletedAction = (_) =>
            {
                // 导航完成时设置任务结果
                _navigationCompletionSource.TrySetResult(true);
            };
            _mdWebpagePanel.NavigateToMd(File.ReadAllText(mdFilePath));
            
            grid.Children.Add(_mdWebpagePanel);
            panel.Children.Add(grid);
            
            // 设置Grid高度以占满剩余空间
            panel.SizeChanged += (sender, args) =>
            {
                // 计算其他元素使用的高度
                double otherElementsHeight = 0;
                foreach (var child in panel.Children)
                {
                    if (child != grid)
                    {
                        var frameworkElement = child as FrameworkElement;
                        if (frameworkElement != null)
                        {
                            otherElementsHeight += frameworkElement.ActualHeight + frameworkElement.Margin.Top + frameworkElement.Margin.Bottom;
                        }
                    }
                }
                
                // 设置Grid高度为剩余空间
                grid.Height = Math.Max(400, panel.ActualHeight - otherElementsHeight - 15); // 设置最小高度为400
            };
        }
        else
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"版本: {scriptProject.Manifest.Version}",
                Margin = new Thickness(0, 5, 0, 5)
            });

            panel.Children.Add(new TextBlock
            {
                Text = scriptProject.Manifest.Description,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 15)
            });
        }

        // 添加操作按钮
        // var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal };
        //
        // var runButton = new Button
        // {
        //     Content = "执行脚本",
        //     Margin = new Thickness(0, 0, 10, 0)
        // };
        // runButton.Click += async (s, e) => await OnStartRun(script);
        // buttonPanel.Children.Add(runButton);
        //
        // var openFolderButton = new Button { Content = "打开目录" };
        // openFolderButton.Click += (s, e) => OnOpenScriptProjectFolder(script);
        // buttonPanel.Children.Add(openFolderButton);

        // panel.Children.Add(buttonPanel);


        return border;
    }

    private static string? FindMdFilePath(ScriptProject script)
    {
        string[] possibleMdFiles = { "README.md", "readme.md" };
        string mdFilePath = null;

        foreach (var mdFile in possibleMdFiles)
        {
            string fullPath = Path.Combine(script.ProjectPath, mdFile);
            if (File.Exists(fullPath))
            {
                mdFilePath = fullPath;
                break;
            }
        }

        return mdFilePath;
    }
}
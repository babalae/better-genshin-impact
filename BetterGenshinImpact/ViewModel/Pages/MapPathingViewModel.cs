using System;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.ViewModel.Message;
using CommunityToolkit.Mvvm.Messaging;
using Wpf.Ui.Violeta.Controls;
using BetterGenshinImpact.View.Pages.View;
using BetterGenshinImpact.ViewModel.Pages.View;
using Wpf.Ui.Violeta.Win32;
using BetterGenshinImpact.View.Controls.Drawer;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class MapPathingViewModel : ViewModel
{
    private readonly ILogger<MapPathingViewModel> _logger = App.GetLogger<MapPathingViewModel>();
    public static readonly string PathJsonPath = Global.Absolute(@"User\AutoPathing");

    [ObservableProperty]
    private ObservableCollection<FileTreeNode<PathingTask>> _treeList = [];

    [ObservableProperty]
    private FileTreeNode<PathingTask>? _selectNode;

    private MapPathingDevWindow? _mapPathingDevWindow;
    private readonly IScriptService _scriptService;

    public AllConfig Config { get; set; }
    
    // 添加抽屉ViewModel
    public DrawerViewModel DrawerVm { get; } = new DrawerViewModel();

    /// <inheritdoc/>
    public MapPathingViewModel(IScriptService scriptService, IConfigService configService)
    {
        _scriptService = scriptService;
        Config = configService.Get();
        WeakReferenceMessenger.Default.Register<RefreshDataMessage>(this, (r, m) => InitScriptListViewData());

        IconManager.CacheExcludeExtensions = [".ico"];
    }

    private void InitScriptListViewData()
    {
        TreeList.Clear();
        var root = FileTreeNodeHelper.LoadDirectory<PathingTask>(PathJsonPath);
        // 循环写入 root.Children
        foreach (var item in root.Children)
        {
            SetIconForNodeAndChildren(item);
            TreeList.Add(item);
        }
    }

    private void SetIconForNodeAndChildren(FileTreeNode<PathingTask> node)
    {
        if (!string.IsNullOrEmpty(node.FilePath) && File.Exists(Path.Combine(node.FilePath, "icon.ico")))
        {
            node.IconFilePath = Path.Combine(node.FilePath, "icon.ico");
        }
        else
        {
            node.IconFilePath = node.FilePath;
        }

        foreach (var child in node.Children)
        {
            SetIconForNodeAndChildren(child);
        }
    }

    public override void OnNavigatedTo()
    {
        InitScriptListViewData();
    }

    [RelayCommand]
    public void OnOpenScriptsFolder()
    {
        if (!Directory.Exists(PathJsonPath))
        {
            Directory.CreateDirectory(PathJsonPath);
        }

        Process.Start("explorer.exe", PathJsonPath);
    }

    [RelayCommand]
    public void OnOpenScriptProjectFolder(ScriptProject? item)
    {
        if (item == null)
        {
            return;
        }

        Process.Start("explorer.exe", item.ProjectPath);
    }

    [RelayCommand]
    public async Task OnStart()
    {
        var item = SelectNode;
        if (item == null)
        {
            return;
        }

        if (item.IsDirectory)
        {
            Toast.Warning("执行多个地图追踪任务的时候，请使用调度器功能");
            return;
        }

        if (string.IsNullOrEmpty(item.FilePath))
        {
            return;
        }

        var fileInfo = new FileInfo(item.FilePath);
        var project = ScriptGroupProject.BuildPathingProject(fileInfo.Name, fileInfo.DirectoryName!);
        await _scriptService.RunMulti([project]);
    }
    
    [RelayCommand]
    public void OnOpenDevTools()
    {
        if (_mapPathingDevWindow == null || !_mapPathingDevWindow.IsVisible)
        {
            _mapPathingDevWindow = new MapPathingDevWindow();
            _mapPathingDevWindow.Closed += (s, e) => _mapPathingDevWindow = null;
            _mapPathingDevWindow.Show();
        }
        else
        {
            _mapPathingDevWindow.Activate();
        }
    }

    [RelayCommand]
    public async void OnOpenSettings()
    {
        // var uiMessageBox = new Wpf.Ui.Controls.MessageBox
        // {
        //     Content = view,
        //     Title = "地图追踪配置",
        //     CloseButtonText = "关闭"
        // };
        //
        // await uiMessageBox.ShowDialogAsync();

        var vm = App.GetService<PathingConfigViewModel>();
        var view = new PathingConfigView(vm);
        view?.ShowDialog();
    }

    [RelayCommand]
    public void OnGoToPathingUrl()
    {
        Process.Start(new ProcessStartInfo("https://bettergi.com/feats/autos/pathing.html") { UseShellExecute = true });
    }

    [RelayCommand]
    public void OnRefresh()
    {
        InitScriptListViewData();
    }

    [RelayCommand]
    public void OnOpenLocalScriptRepo()
    {
        Config.ScriptConfig.ScriptRepoHintDotVisible = false;
        ScriptRepoUpdater.Instance.OpenScriptRepoWindow();
    }

    [RelayCommand]
    public void OnOpenPathingDetail()
    {
        var item = SelectNode;
        if (item == null)
        {
            return;
        }

        // 设置抽屉位置和大小
        DrawerVm.DrawerPosition = DrawerPosition.Right;
        DrawerVm.DrawerWidth = 350;

        // 创建要在抽屉中显示的内容
        var content = CreatePathingDetailContent(item);

        // 打开抽屉
        DrawerVm.OpenDrawer(content);
    }

    private object CreatePathingDetailContent(FileTreeNode<PathingTask> node)
    {
        // 创建显示路径任务详情的控件
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)),
            Padding = new Thickness(20)
        };
        
        var panel = new StackPanel();
        border.Child = panel;

        // 添加标题
        panel.Children.Add(new TextBlock
        {
            Text = node.FileName,
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 10)
        });

        // 如果是文件而不是目录，显示更多详情
        if (!node.IsDirectory && !string.IsNullOrEmpty(node.FilePath))
        {
            try
            {
                var fileInfo = new FileInfo(node.FilePath);
                
                panel.Children.Add(new TextBlock
                {
                    Text = $"路径: {node.FilePath}",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 5, 0, 5)
                });
                
                panel.Children.Add(new TextBlock
                {
                    Text = $"大小: {fileInfo.Length / 1024} KB",
                    Margin = new Thickness(0, 5, 0, 5)
                });
                
                panel.Children.Add(new TextBlock
                {
                    Text = $"修改时间: {fileInfo.LastWriteTime}",
                    Margin = new Thickness(0, 5, 0, 15)
                });

                // 添加操作按钮
                var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal };

                var runButton = new Button
                {
                    Content = "执行任务",
                    Margin = new Thickness(0, 0, 10, 0)
                };
                runButton.Click += async (s, e) => await OnStart();
                buttonPanel.Children.Add(runButton);

                panel.Children.Add(buttonPanel);
            }
            catch (Exception ex)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"读取文件信息时出错: {ex.Message}",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 5, 0, 5)
                });
            }
        }
        else
        {
            // 显示目录信息
            panel.Children.Add(new TextBlock
            {
                Text = "这是一个目录，包含多个地图追踪任务。",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 15)
            });

            // 添加子项信息
            if (node.Children.Count > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"包含 {node.Children.Count} 个子项",
                    Margin = new Thickness(0, 5, 0, 5)
                });
            }
        }

        return border;
    }
}

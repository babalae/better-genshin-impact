using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Controls.Drawer;
using BetterGenshinImpact.View.Controls.Webview;
using BetterGenshinImpact.View.Pages.View;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Message;
using BetterGenshinImpact.ViewModel.Pages.View;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Wpf.Ui.Violeta.Controls;
using Wpf.Ui.Violeta.Win32;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class MapPathingViewModel : ViewModel
{
    private readonly ILogger<MapPathingViewModel> _logger = App.GetLogger<MapPathingViewModel>();
    public static readonly string PathJsonPath = Global.Absolute(@"User\AutoPathing");

    [ObservableProperty] private ObservableCollection<FileTreeNode<PathingTask>> _treeList = [];

    [ObservableProperty] private FileTreeNode<PathingTask>? _selectNode;

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

        // 如果是目录，检查是否存在README.md
        string? mdFilePath = null;
        if (item.IsDirectory && !string.IsNullOrEmpty(item.FilePath))
        {
            mdFilePath = FindMdFilePath(item.FilePath);
        }

        // 设置抽屉位置和大小
        DrawerVm.DrawerPosition = DrawerPosition.Right;

        if (!string.IsNullOrEmpty(mdFilePath))
        {
            DrawerVm.DrawerWidth = 450;
        }
        else
        {
            DrawerVm.DrawerWidth = 350;
        }

        // 统一的抽屉事件处理
        DrawerVm.SetDrawerClosingAction(_ => { });
        DrawerVm.setDrawerOpenedAction(() => { SelectNode = null; });

        // 创建要在抽屉中显示的内容
        var content = CreatePathingDetailContent(item, mdFilePath);

        // 打开抽屉
        if (content != null)
        {
            DrawerVm.OpenDrawer(content);
        }
    }

    private string? FindMdFilePath(string dirPath)
    {
        string[] possibleMdFiles = { "README.md", "readme.md" };

        foreach (var mdFile in possibleMdFiles)
        {
            string fullPath = Path.Combine(dirPath, mdFile);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return null;
    }

    private object? CreatePathingDetailContent(FileTreeNode<PathingTask> node, string? mdFilePath = null)
    {
        // 创建显示路径任务详情的控件
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)),
            Padding = new Thickness(20)
        };

        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 标题行
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 内容行，占满剩余空间

        border.Child = mainGrid;

        // 添加标题
        var titleTextBlock = new TextBlock
        {
            Text = node.FileName,
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetRow(titleTextBlock, 0);
        mainGrid.Children.Add(titleTextBlock);

        // 如果找到md文件，使用RichTextBox显示
        if (!string.IsNullOrEmpty(mdFilePath))
        {
            string markdown = File.ReadAllText(mdFilePath);
            var flowDoc = MarkdownToFlowDocumentConverter.ConvertToFlowDocument(markdown);
            var richTextBox = new RichTextBox
            {
                IsReadOnly = true,
                IsDocumentEnabled = true,
                BorderThickness = new Thickness(0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Document = flowDoc,
                Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 10, 0, 0)
            };

            Grid.SetRow(richTextBox, 1);
            mainGrid.Children.Add(richTextBox);
        }
        else if (!node.IsDirectory && !string.IsNullOrEmpty(node.FilePath))
        {
            // 如果是文件而不是目录，显示更多详情
            try
            {
                if (string.IsNullOrEmpty(node.Value?.Info.Description))
                {
                    return null;
                }

                var descriptionTextBlock = new TextBlock
                {
                    Text = $"{node.Value?.Info.Description}",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 10, 0, 0),
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                Grid.SetRow(descriptionTextBlock, 1);
                mainGrid.Children.Add(descriptionTextBlock);
            }
            catch (Exception ex)
            {
                var errorTextBlock = new TextBlock
                {
                    Text = $"读取文件信息时出错: {ex.Message}",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 10, 0, 0),
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Foreground = Brushes.Orange
                };
                Grid.SetRow(errorTextBlock, 1);
                mainGrid.Children.Add(errorTextBlock);
            }
        }
        else
        {
            // 显示目录信息 - 使用StackPanel包装多个TextBlock
            var contentPanel = new StackPanel
            {
                Margin = new Thickness(0, 10, 0, 0),
                VerticalAlignment = VerticalAlignment.Top
            };

            contentPanel.Children.Add(new TextBlock
            {
                Text = "这是一个目录，包含多个地图追踪任务。",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 15)
            });

            // 添加子项信息
            if (node.Children.Count > 0)
            {
                contentPanel.Children.Add(new TextBlock
                {
                    Text = $"包含 {node.Children.Count} 个子项",
                    Margin = new Thickness(0, 0, 0, 5)
                });
            }

            Grid.SetRow(contentPanel, 1);
            mainGrid.Children.Add(contentPanel);
        }

        return border;
    }
}
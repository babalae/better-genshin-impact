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
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.ViewModel.Message;
using CommunityToolkit.Mvvm.Messaging;
using Wpf.Ui.Controls;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class MapPathingViewModel : ObservableObject, INavigationAware, IViewModel
{
    private readonly ILogger<MapPathingViewModel> _logger = App.GetLogger<MapPathingViewModel>();
    public static readonly string PathJsonPath = Global.Absolute(@"User\AutoPathing");

    [ObservableProperty]
    private ObservableCollection<FileTreeNode<PathingTask>> _treeList = [];

    private MapViewer? _mapViewer;
    private readonly IScriptService _scriptService;

    public AllConfig Config { get; set; }

    /// <inheritdoc/>
    public MapPathingViewModel(IScriptService scriptService, IConfigService configService)
    {
        _scriptService = scriptService;
        Config = configService.Get();
        WeakReferenceMessenger.Default.Register<RefreshDataMessage>(this, (r, m) => InitScriptListViewData());
    }

    private void InitScriptListViewData()
    {
        TreeList.Clear();
        var root = FileTreeNodeHelper.LoadDirectory<PathingTask>(PathJsonPath);
        // 循环写入 root.Children
        foreach (var item in root.Children)
        {
            // 补充图标
            if (!string.IsNullOrEmpty(item.FilePath) && File.Exists(Path.Combine(item.FilePath, "icon.ico")))
            {
                item.IconFilePath = Path.Combine(item.FilePath, "icon.ico");
            }
            else
            {
                item.IconFilePath = item.FilePath;
            }

            TreeList.Add(item);
        }
    }

    public void OnNavigatedTo()
    {
        InitScriptListViewData();
    }

    public void OnNavigatedFrom()
    {
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
    public async void OnStart(FileTreeNode<PathingTask>? item)
    {
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
    public void OnOpenMapViewer()
    {
        if (_mapViewer == null || !_mapViewer.IsVisible)
        {
            _mapViewer = new MapViewer();
            _mapViewer.Closed += (s, e) => _mapViewer = null;
            _mapViewer.Show();
        }
        else
        {
            _mapViewer.Activate();
        }
    }

    [RelayCommand]
    public void OnOpenMapEditor()
    {
        PathRecorder.Instance.OpenEditorInWebView();
    }

    [RelayCommand]
    public void OnGoToPathingUrl()
    {
        Process.Start(new ProcessStartInfo("https://bgi.huiyadan.com/autos/pathing.html") { UseShellExecute = true });
    }

    [RelayCommand]
    public void OnRefresh(object? item)
    {
        InitScriptListViewData();
    }

    [RelayCommand]
    public void OnOpenLocalScriptRepo()
    {
        Config.ScriptConfig.ScriptRepoHintDotVisible = false;
        ScriptRepoUpdater.Instance.OpenLocalRepoInWebView();
    }
}

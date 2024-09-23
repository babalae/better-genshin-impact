using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class MapPathingViewModel(IScriptService scriptService) : ObservableObject, INavigationAware, IViewModel
{
    private readonly ILogger<MapPathingViewModel> _logger = App.GetLogger<MapPathingViewModel>();
    public static readonly string PathJsonPath = Global.Absolute(@"User\AutoPathing");

    [ObservableProperty]
    private TreeCollection<PathingTaskTreeObject> _pathItems = Load();

    [ObservableProperty]
    private FileTreeModel<PathingTask> _fileItems = new(PathJsonPath);

    private MapViewer? _mapViewer;

    private void InitScriptListViewData()
    {
        _pathItems.Clear();

        _pathItems.Root.Name = PathJsonPath;
        _pathItems.Root.IsFolder = true;
        LoadFolderRecursive(PathJsonPath, _pathItems.Root);
    }

    private static TreeCollection<PathingTaskTreeObject> Load()
    {
        TreeCollection<PathingTaskTreeObject> tree = [];
        tree.Root.Name = PathJsonPath;
        tree.Root.IsFolder = true;
        LoadFolderRecursive(PathJsonPath, tree.Root);
        return tree;
    }

    private static void LoadFolderRecursive(string folder, PathingTaskTreeObject parent)
    {
        var directoryInfo = new DirectoryInfo(folder);
        foreach (var dir in directoryInfo.GetDirectories())
        {
            var dirNode = new PathingTaskTreeObject { Name = dir.Name, IsFolder = true };
            parent.Children.Add(dirNode);
            LoadFolderRecursive(dir.FullName, dirNode);
        }

        foreach (var file in directoryInfo.GetFiles())
        {
            var fileNode = new PathingTaskTreeObject
            {
                Name = file.Name,
                IsFolder = false,
                Task = PathingTask.BuildFromFilePath(file.FullName)
            };
            parent.Children.Add(fileNode);
        }
    }

    public void OnNavigatedTo()
    {
        // InitScriptListViewData();
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
    public async void OnStart(PathingTask? item)
    {
        if (item == null)
        {
            return;
        }

        var fileInfo = new FileInfo(item.FullPath);
        var project = ScriptGroupProject.BuildPathingProject(fileInfo.Name, fileInfo.DirectoryName!);
        await scriptService.RunMulti([project]);
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
        _fileItems = new(PathJsonPath);
    }
}

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Script.Project;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.ViewModel.Windows.GearTask;

public partial class JsScriptSelectionViewModel : ViewModel
{
    private readonly ILogger<JsScriptSelectionViewModel> _logger = App.GetLogger<JsScriptSelectionViewModel>();

    [ObservableProperty]
    private ObservableCollection<JsScriptInfo> _jsScripts = [];

    [ObservableProperty]
    private ObservableCollection<JsScriptInfo> _filteredJsScripts = [];

    [ObservableProperty]
    private JsScriptInfo? _selectedScript;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _readmeContent = string.Empty;

    [ObservableProperty]
    private string _mainJsContent = string.Empty;

    [ObservableProperty]
    private int _selectedTabIndex = 0;

    public JsScriptSelectionViewModel()
    {
        LoadJsScripts();
    }

    private void LoadJsScripts()
    {
        try
        {
            var jsPath = Path.Combine(ScriptRepoUpdater.CenterRepoPath, "repo", "js");
            if (!Directory.Exists(jsPath))
            {
                _logger.LogWarning($"JS脚本目录不存在: {jsPath}");
                return;
            }

            var scriptDirectories = Directory.GetDirectories(jsPath);
            var scripts = new ObservableCollection<JsScriptInfo>();

            foreach (var scriptDir in scriptDirectories)
            {
                try
                {
                    var manifestPath = Path.Combine(scriptDir, "manifest.json");
                    if (File.Exists(manifestPath))
                    {
                        var manifestContent = File.ReadAllText(manifestPath);
                        var manifest = Manifest.FromJson(manifestContent);
                        
                        var scriptInfo = new JsScriptInfo
                        {
                            FolderName = Path.GetFileName(scriptDir),
                            FolderPath = scriptDir,
                            Manifest = manifest
                        };
                        
                        scripts.Add(scriptInfo);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"加载JS脚本失败: {scriptDir}");
                }
            }

            JsScripts = scripts;
            ApplyFilter();
            SelectFirstScript();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载JS脚本列表失败");
        }
    }

    partial void OnSelectedScriptChanged(JsScriptInfo? value)
    {
        if (value != null)
        {
            // 清空之前的内容
            ReadmeContent = string.Empty;
            MainJsContent = string.Empty;
            
            // 根据当前选中的标签页加载内容
            LoadCurrentTabContent();
        }
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (SelectedScript != null)
        {
            LoadCurrentTabContent();
        }
    }

    private async void LoadCurrentTabContent()
    {
        if (SelectedScript == null) return;
        
        switch (SelectedTabIndex)
        {
            case 0: // README.md
                if (string.IsNullOrEmpty(ReadmeContent))
                {
                    await Task.Run(LoadReadmeContent);
                }
                break;
            case 1: // main.js
                if (string.IsNullOrEmpty(MainJsContent))
                {
                    await Task.Run(LoadMainJsContent);
                }
                break;
        }
    }

    private void LoadReadmeContent()
    {
        if (SelectedScript == null) return;
        
        try
        {
            var readmePath = Path.Combine(SelectedScript.FolderPath, "README.md");
            if (File.Exists(readmePath))
            {
                ReadmeContent = File.ReadAllText(readmePath);
            }
            else
            {
                ReadmeContent = "README.md 文件不存在";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载README.md失败");
            ReadmeContent = $"加载README.md失败: {ex.Message}";
        }
    }

    private void LoadMainJsContent()
    {
        if (SelectedScript == null) return;
        
        try
        {
            var mainJsPath = Path.Combine(SelectedScript.FolderPath, SelectedScript.Manifest.Main);
            if (File.Exists(mainJsPath))
            {
                MainJsContent = File.ReadAllText(mainJsPath);
            }
            else
            {
                MainJsContent = "main.js 文件不存在";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载main.js失败");
            MainJsContent = $"加载main.js失败: {ex.Message}";
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
        SelectFirstScript();
    }

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            FilteredJsScripts = new ObservableCollection<JsScriptInfo>(JsScripts);
        }
        else
        {
            var filtered = JsScripts.Where(script => 
                script.FolderName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                script.Manifest.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                script.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            ).ToList();
            FilteredJsScripts = new ObservableCollection<JsScriptInfo>(filtered);
        }
    }

    private void SelectFirstScript()
    {
        if (FilteredJsScripts.Count > 0)
        {
            SelectedScript = FilteredJsScripts[0];
        }
        else
        {
            SelectedScript = null;
        }
    }

    [RelayCommand]
    private void RefreshScripts()
    {
        LoadJsScripts();
    }
}

public class JsScriptInfo
{
    public string FolderName { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public Manifest Manifest { get; set; } = new();
    
    public string DisplayName => $"{FolderName} - {Manifest.Name}";
    public string Description => Manifest.ShortDescription;
}
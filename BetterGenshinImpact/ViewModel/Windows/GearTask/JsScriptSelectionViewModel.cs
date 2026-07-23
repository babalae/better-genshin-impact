using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

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
            string? repoJsonContent = ScriptRepoUpdater.Instance.ReadFileFromCenterRepo("repo.json");
            if (string.IsNullOrWhiteSpace(repoJsonContent))
            {
                var repoJsonPath = Directory.GetFiles(ScriptRepoUpdater.CenterRepoPath, "repo.json", SearchOption.AllDirectories).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(repoJsonPath) && File.Exists(repoJsonPath))
                {
                    repoJsonContent = File.ReadAllText(repoJsonPath);
                }
            }

            if (string.IsNullOrWhiteSpace(repoJsonContent))
            {
                _logger.LogWarning("repo/repo.json 不存在或内容为空");
                return;
            }

            var repoJson = JObject.Parse(repoJsonContent);
            if (repoJson["indexes"] is not JArray indexes)
            {
                _logger.LogWarning("repo/repo.json 缺少 indexes 节点");
                return;
            }

            var jsNode = indexes
                .OfType<JObject>()
                .FirstOrDefault(x => string.Equals(x["name"]?.ToString(), "js", StringComparison.OrdinalIgnoreCase));

            if (jsNode?["children"] is not JArray jsChildren)
            {
                _logger.LogWarning("repo/repo.json 缺少 js.children 节点");
                return;
            }

            var scripts = new ObservableCollection<JsScriptInfo>();

            foreach (var child in jsChildren.OfType<JObject>())
            {
                try
                {
                    var folderName = child["name"]?.ToString();
                    if (string.IsNullOrWhiteSpace(folderName))
                    {
                        continue;
                    }

                    var rawDescription = child["description"]?.ToString() ?? string.Empty;
                    var (name, shortDescription) = SplitRepoDescription(rawDescription, folderName);

                    var scriptInfo = new JsScriptInfo
                    {
                        FolderName = folderName,
                        RepoRelDir = $"js/{folderName}",
                        Name = name,
                        ShortDescription = shortDescription,
                        Version = child["version"]?.ToString() ?? string.Empty,
                        Author = child["author"]?.ToString() ?? string.Empty,
                        RawDescription = rawDescription,
                        LastUpdated = child["lastUpdated"]?.ToString() ?? string.Empty,
                        Tags = child["tags"] is JArray tags
                            ? tags.Values<string>().Where(t => !string.IsNullOrWhiteSpace(t)).ToArray()
                            : [],
                        Authors = child["authors"] is JArray authors
                            ? authors
                                .OfType<JObject>()
                                .Select(a => a["name"]?.ToString())
                                .Where(a => !string.IsNullOrWhiteSpace(a))
                                .Cast<string>()
                                .ToArray()
                            : [],
                    };

                    scripts.Add(scriptInfo);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "加载JS脚本失败");
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
            var relPath = $"{SelectedScript.RepoRelDir}/README.md";
            string? content = ScriptRepoUpdater.Instance.ReadFileFromCenterRepo(relPath);
            ReadmeContent = content ?? "README.md 文件不存在";
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
            var relPath = $"{SelectedScript.RepoRelDir}/main.js";
            string? content = ScriptRepoUpdater.Instance.ReadFileFromCenterRepo(relPath);
            MainJsContent = content ?? "main.js 文件不存在";
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
                script.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
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
    
    private static (string Name, string ShortDescription) SplitRepoDescription(string rawDescription, string fallbackName)
    {
        if (string.IsNullOrWhiteSpace(rawDescription))
        {
            return (fallbackName, string.Empty);
        }

        const string separator = "~|~";
        var idx = rawDescription.IndexOf(separator, StringComparison.Ordinal);
        if (idx < 0)
        {
            return (fallbackName, rawDescription.Trim());
        }

        var name = rawDescription[..idx].Trim();
        var desc = rawDescription[(idx + separator.Length)..].Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            name = fallbackName;
        }

        return (name, desc);
    }
}

public class JsScriptInfo
{
    public string FolderName { get; set; } = string.Empty;
    public string RepoRelDir { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ShortDescription { get; set; } = string.Empty;
    public string RawDescription { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string LastUpdated { get; set; } = string.Empty;
    public string[] Tags { get; set; } = [];
    public string[] Authors { get; set; } = [];
    
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? FolderName : $"{Name}（{FolderName}）";
    public string Description => ShortDescription;
}

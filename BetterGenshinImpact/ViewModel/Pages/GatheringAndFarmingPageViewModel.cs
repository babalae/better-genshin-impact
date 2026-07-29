using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using Wpf.Ui;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class GatheringAndFarmingPageViewModel : ViewModel
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> MaterialIconUrls = new(LoadMaterialIconUrls);
    private readonly ILogger<GatheringAndFarmingPageViewModel> _logger = App.GetLogger<GatheringAndFarmingPageViewModel>();
    private readonly IScriptService _scriptService;
    private readonly INavigationService _navigationService;
    private readonly List<PathingTaskIndexEntry> _pathingTaskIndex = [];
    private bool _isInitialized;

    [ObservableProperty] private ObservableCollection<GatherCharacterCard> _characters = [];
    [ObservableProperty] private ObservableCollection<GatherTrackSection> _foodAndSpecialtySections = [];
    [ObservableProperty] private ObservableCollection<GatherTrackItem> _dropItems = [];
    [ObservableProperty] private ObservableCollection<GatherTrackItem> _oreItems = [];
    [ObservableProperty] private ObservableCollection<GatherTrackItem> _filteredFoodAndSpecialtyItems = [];
    [ObservableProperty] private ObservableCollection<GatherTrackItem> _filteredDropItems = [];
    [ObservableProperty] private ObservableCollection<GatherTrackItem> _filteredOreItems = [];
    [ObservableProperty] private string _foodAndSpecialtyFilterText = string.Empty;
    [ObservableProperty] private string _dropItemFilterText = string.Empty;
    [ObservableProperty] private string _oreItemFilterText = string.Empty;
    [ObservableProperty] private int _availablePathingTaskCount;
    [ObservableProperty] private string _taskIndexSummary = "尚未扫描路线";

    public int TrackEntryCount =>
        Characters.Sum(x => x.Materials.Count)
        + FoodAndSpecialtySections.Sum(x => x.Items.Count)
        + DropItems.Count
        + OreItems.Count;

    public GatheringAndFarmingPageViewModel(IScriptService scriptService, INavigationService navigationService)
    {
        _scriptService = scriptService;
        _navigationService = navigationService;
    }

    public override void OnNavigatedTo()
    {
        if (_isInitialized)
        {
            return;
        }

        BuildDesignData();
        _ = LoadImagesAsync();
        _ = RefreshPathingIndexAsync();
        _isInitialized = true;
    }

    [RelayCommand]
    private void OpenMapPathing()
    {
        _navigationService.Navigate(typeof(MapPathingPage));
    }

    [RelayCommand]
    private void OpenScriptRepo()
    {
        ScriptRepoUpdater.Instance.OpenScriptRepoWindow();
    }

    [RelayCommand]
    private async Task RefreshPathingIndexAsync()
    {
        try
        {
            var pathingRoot = MapPathingViewModel.PathJsonPath;
            if (!Directory.Exists(pathingRoot))
            {
                _pathingTaskIndex.Clear();
                AvailablePathingTaskCount = 0;
                TaskIndexSummary = "未找到本地地图追踪目录，编译后首次运行会自动生成。";
                return;
            }

            var files = await Task.Run(() => Directory
                .EnumerateFiles(pathingRoot, "*.json", SearchOption.AllDirectories)
                .Select(path =>
                {
                    var relativePath = Path.GetRelativePath(pathingRoot, path);
                    return new PathingTaskIndexEntry(
                        path,
                        relativePath.Replace('/', '\\'),
                        Path.GetFileNameWithoutExtension(path));
                })
                .OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList());

            _pathingTaskIndex.Clear();
            _pathingTaskIndex.AddRange(files);
            AvailablePathingTaskCount = files.Count;
            TaskIndexSummary = files.Count == 0
                ? "已扫描完成，但本地还没有可执行的地图追踪任务。"
                : $"已完成扫描，可直接匹配 {files.Count} 条地图追踪任务。";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "刷新采集与锄地路线索引失败");
            TaskIndexSummary = $"路线索引刷新失败：{ex.Message}";
            Toast.Error($"路线索引刷新失败：{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task TrackMaterialAsync(GatherTrackItem? item)
    {
        if (item == null)
        {
            return;
        }

        if (_pathingTaskIndex.Count == 0)
        {
            await RefreshPathingIndexAsync();
        }

        var matchedTask = FindBestPathingTask(item.SearchKeywords);
        if (matchedTask == null)
        {
            await ThemedMessageBox.WarningAsync(
                $"未找到与“{item.Name}”匹配的地图追踪任务。\n\n你可以先在“脚本仓库”或“地图追踪”中准备对应路线，然后再从这里一键执行。",
                "未找到路线");
            return;
        }

        var result = await ThemedMessageBox.QuestionAsync(
            $"是否执行“{item.Name}”对应的地图追踪任务？\n\n匹配到的路线：{matchedTask.RelativePath}",
            "执行地图追踪",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxResult.No);

        if (result != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var project = BuildPathingProject(matchedTask.FullPath);
            await _scriptService.RunMulti([project]);
            Toast.Success($"已开始执行路线：{Path.GetFileNameWithoutExtension(matchedTask.FullPath)}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行素材路线失败: {MaterialName}", item.Name);
            await ThemedMessageBox.ErrorAsync($"执行“{item.Name}”失败：{ex.Message}", "执行失败");
        }
    }

    private PathingTaskIndexEntry? FindBestPathingTask(IEnumerable<string> keywords)
    {
        var keywordList = keywords
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (keywordList.Count == 0)
        {
            return null;
        }

        return _pathingTaskIndex
            .Select(entry => new
            {
                Entry = entry,
                Score = keywordList.Max(keyword => ScorePathingTask(entry, keyword))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Entry.RelativePath.Length)
            .ThenBy(x => x.Entry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Entry)
            .FirstOrDefault();
    }

    private static int ScorePathingTask(PathingTaskIndexEntry entry, string keyword)
    {
        if (entry.FileName.Equals(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (entry.RelativePath.EndsWith(keyword + ".json", StringComparison.OrdinalIgnoreCase))
        {
            return 90;
        }

        if (entry.FileName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return 75;
        }

        if (entry.RelativePath.Contains(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return 60;
        }

        return 0;
    }

    private static ScriptGroupProject BuildPathingProject(string fullPath)
    {
        var folder = Path.GetDirectoryName(fullPath) ?? MapPathingViewModel.PathJsonPath;
        var relativeFolder = Path.GetRelativePath(MapPathingViewModel.PathJsonPath, folder);
        if (relativeFolder == ".")
        {
            relativeFolder = string.Empty;
        }

        return ScriptGroupProject.BuildPathingProject(Path.GetFileName(fullPath), relativeFolder);
    }

    private void BuildDesignData()
    {
        Characters =
        [
            new GatherCharacterCard(
                "纳西妲",
                "须弥 · 草系主C/辅助",
                "纳",
                "https://enka.network/ui/UI_AvatarIcon_Nahida.png",
                CreatePalette("#84C26E", "#1E3523"),
                [
                    CreateTrack("月莲", "水边与林地特产路线", "月", "#84C26E", "#1E3523"),
                    CreateTrack("蕈兽", "浮游菌群常规掉落路线", "蕈", "#6BB9A0", "#1B342F", "蕈兽", "浮游菌"),
                    CreateTrack("树莓", "顺路补货的常用食材路线", "莓", "#D36B7F", "#3A1D25")
                ]),
            new GatherCharacterCard(
                "芙宁娜",
                "枫丹 · 水系辅助",
                "芙",
                "https://enka.network/ui/UI_AvatarIcon_Furina.png",
                CreatePalette("#7EB7F3", "#1D2E45"),
                [
                    CreateTrack("湖光铃兰", "枫丹湖区特产路线", "铃", "#7EB7F3", "#1D2E45"),
                    CreateTrack("原海异种", "海边通刷掉落路线", "海", "#5AA4D6", "#183247", "原海异种", "异海凝珠"),
                    CreateTrack("萃凝晶", "枫丹矿石补货路线", "晶", "#82C9FF", "#20354A")
                ]),
            new GatherCharacterCard(
                "雷电将军",
                "稻妻 · 充能爆发核心",
                "雷",
                "https://enka.network/ui/UI_AvatarIcon_Shougun.png",
                CreatePalette("#A887F7", "#2B2144"),
                [
                    CreateTrack("天云草实", "清籁岛高密度特产路线", "云", "#A887F7", "#2B2144"),
                    CreateTrack("野伏众", "稻妻常刷掉落路线", "伏", "#E09A5E", "#442817", "野伏众", "刀镡"),
                    CreateTrack("紫晶块", "稻妻锻造矿石路线", "紫", "#B498FF", "#322650")
                ]),
            new GatherCharacterCard(
                "钟离",
                "璃月 · 护盾辅助",
                "钟",
                "https://enka.network/ui/UI_AvatarIcon_Zhongli.png",
                CreatePalette("#D3A65C", "#43301A"),
                [
                    CreateTrack("石珀", "璃月山体特产路线", "珀", "#D3A65C", "#43301A"),
                    CreateTrack("史莱姆", "通用掉落补货路线", "史", "#73C7C0", "#1C3737", "史莱姆", "史莱姆凝液"),
                    CreateTrack("白铁块", "璃月常用矿石路线", "铁", "#C7D0DA", "#2B3440")
                ]),
            new GatherCharacterCard(
                "那维莱特",
                "枫丹 · 水系站场",
                "那",
                "https://enka.network/ui/UI_AvatarIcon_Neuvillette.png",
                CreatePalette("#68C1E0", "#183743"),
                [
                    CreateTrack("湖光铃兰", "角色专属特产路线", "铃", "#68C1E0", "#183743"),
                    CreateTrack("原海异种", "海边通刷掉落路线", "海", "#68C1E0", "#183743", "原海异种", "异海凝珠"),
                    CreateTrack("水晶块", "精锻魔矿主力矿线", "晶", "#8FD2F2", "#213A49")
                ]),
            new GatherCharacterCard(
                "娜维娅",
                "枫丹 · 岩系输出",
                "娜",
                "https://enka.network/ui/UI_AvatarIcon_Navia.png",
                CreatePalette("#E0B765", "#45371A"),
                [
                    CreateTrack("苍晶螺", "枫丹海岸特产路线", "螺", "#73C0D4", "#203743"),
                    CreateTrack("发条机关", "枫丹机械系掉落路线", "机", "#D6A86A", "#45301E"),
                    CreateTrack("白铁块", "锻造补货矿石路线", "铁", "#C7D0DA", "#2B3440")
                ])
        ];

        FoodAndSpecialtySections =
        [
            new GatherTrackSection(
                "地方特产",
                "更适合按地区分批补货，适合角色突破前集中准备。",
                [
                    CreateTrack("月莲", "须弥水边高密度路线", "月", "#84C26E", "#1E3523"),
                    CreateTrack("清心", "璃月高山特产路线", "清", "#7CC6E6", "#183543"),
                    CreateTrack("苍晶螺", "枫丹海岸特产路线", "螺", "#73C0D4", "#203743"),
                    CreateTrack("湖光铃兰", "枫丹湖区采集路线", "铃", "#7EB7F3", "#1D2E45"),
                    CreateTrack("石珀", "璃月矿壁采集路线", "珀", "#D3A65C", "#43301A"),
                    CreateTrack("天云草实", "清籁岛采集路线", "云", "#A887F7", "#2B2144")
                ]),
            new GatherTrackSection(
                "食材",
                "适合烹饪、周本前补货或顺路囤积。",
                [
                    CreateTrack("甜甜花", "通用食材顺路收集", "甜", "#E38B98", "#452128"),
                    CreateTrack("松茸", "林地食材补货路线", "茸", "#D6A86A", "#433118"),
                    CreateTrack("树莓", "前期高频食材路线", "莓", "#D36B7F", "#3A1D25"),
                    CreateTrack("莲蓬", "璃月水域食材路线", "莲", "#7DC0B7", "#1C3533"),
                    CreateTrack("日落果", "蒙德野外食材路线", "果", "#E49B67", "#472818"),
                    CreateTrack("鱼肉", "沿河补货路线", "鱼", "#6AB6E6", "#1C3042")
                ])
        ];

        DropItems =
        [
            CreateTrack("丘丘人射手", "弓手掉落集中清线", "丘", "#C98A5B", "#3A2518", "丘丘人射手", "丘丘人"),
            CreateTrack("史莱姆", "元素凝液快速补货", "史", "#73C7C0", "#1C3737", "史莱姆", "史莱姆凝液"),
            CreateTrack("蕈兽", "真菌孢子常用路线", "蕈", "#6BB9A0", "#1B342F", "蕈兽", "浮游菌"),
            CreateTrack("发条机关", "枫丹机械掉落路线", "机", "#D6A86A", "#45301E"),
            CreateTrack("遗迹守卫", "机关核心补货路线", "遗", "#8E9EB5", "#26313F"),
            CreateTrack("原海异种", "海边材料通刷路线", "海", "#5AA4D6", "#183247", "原海异种", "异海凝珠")
        ];

        OreItems =
        [
            CreateTrack("白铁块", "日常锻造基础矿线", "铁", "#C7D0DA", "#2B3440"),
            CreateTrack("水晶块", "精锻魔矿主力矿线", "晶", "#8FD2F2", "#213A49"),
            CreateTrack("紫晶块", "稻妻矿石补货路线", "紫", "#B498FF", "#322650"),
            CreateTrack("星银矿石", "雪山专属矿石路线", "银", "#9FC6DC", "#213545"),
            CreateTrack("萃凝晶", "枫丹矿石补货路线", "凝", "#82C9FF", "#20354A"),
            CreateTrack("铁块", "早期锻造补货路线", "块", "#B4BDC8", "#29323D")
        ];

        ApplyFoodAndSpecialtyFilter();
        ApplyDropItemFilter();
        ApplyOreItemFilter();
        OnPropertyChanged(nameof(TrackEntryCount));
    }

    partial void OnFoodAndSpecialtyFilterTextChanged(string value)
    {
        ApplyFoodAndSpecialtyFilter();
    }

    partial void OnDropItemFilterTextChanged(string value)
    {
        ApplyDropItemFilter();
    }

    partial void OnOreItemFilterTextChanged(string value)
    {
        ApplyOreItemFilter();
    }

    private void ApplyFoodAndSpecialtyFilter()
    {
        var filteredItems = FoodAndSpecialtySections.SelectMany(section =>
            MatchesFilter(section.Title, FoodAndSpecialtyFilterText)
                ? section.Items
                : section.Items.Where(item => MatchesFilter(item, FoodAndSpecialtyFilterText)));
        FilteredFoodAndSpecialtyItems = new ObservableCollection<GatherTrackItem>(filteredItems);
    }

    private void ApplyDropItemFilter()
    {
        FilteredDropItems = new ObservableCollection<GatherTrackItem>(
            DropItems.Where(item => MatchesFilter(item, DropItemFilterText)));
    }

    private void ApplyOreItemFilter()
    {
        FilteredOreItems = new ObservableCollection<GatherTrackItem>(
            OreItems.Where(item => MatchesFilter(item, OreItemFilterText)));
    }

    private static bool MatchesFilter(GatherTrackItem item, string filterText)
    {
        return MatchesFilter(item.Name, filterText)
               || MatchesFilter(item.Description, filterText)
               || item.SearchKeywords.Any(keyword => MatchesFilter(keyword, filterText));
    }

    private static bool MatchesFilter(string value, string filterText)
    {
        return string.IsNullOrWhiteSpace(filterText)
               || value.Contains(filterText.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private async Task LoadImagesAsync()
    {
        var materialItems = Characters.SelectMany(character => character.Materials)
            .Concat(FoodAndSpecialtySections.SelectMany(section => section.Items))
            .Concat(DropItems)
            .Concat(OreItems);
        var imageTasks = Characters.Select(LoadAvatarImageAsync)
            .Concat(materialItems.Select(LoadMaterialImageAsync));

        await Task.WhenAll(imageTasks);
    }

    private static async Task LoadAvatarImageAsync(GatherCharacterCard character)
    {
        character.AvatarImage = await MapIconImageCache.GetAsync(character.AvatarImageUrl, default);
    }

    private static async Task LoadMaterialImageAsync(GatherTrackItem material)
    {
        material.IconImage = await MapIconImageCache.GetAsync(material.IconUrl, default);
    }

    private static GatherTrackItem CreateTrack(
        string name,
        string description,
        string shortLabel,
        string accentHex,
        string surfaceHex,
        params string[] keywords)
    {
        var palette = CreatePalette(accentHex, surfaceHex);
        return new GatherTrackItem(
            name,
            description,
            shortLabel,
            palette.AccentBrush,
            palette.SurfaceBrush,
            MaterialIconUrls.Value.GetValueOrDefault(name, string.Empty),
            keywords.Length == 0 ? [name] : keywords);
    }

    private static IReadOnlyDictionary<string, string> LoadMaterialIconUrls()
    {
        var json = ResourceHelper.GetString("pack://application:,,,/Resources/Json/icons.json");
        var icons = JsonConvert.DeserializeObject<List<GatherIconResource>>(json) ?? [];
        return icons.ToDictionary(x => x.Name, x => x.Link, StringComparer.Ordinal);
    }

    private static GatherPalette CreatePalette(string accentHex, string surfaceHex)
    {
        return new GatherPalette(CreateBrush(accentHex), CreateBrush(surfaceHex));
    }

    private static Brush CreateBrush(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex)!;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private sealed record PathingTaskIndexEntry(string FullPath, string RelativePath, string FileName);

    private sealed class GatherIconResource
    {
        public string Name { get; set; } = string.Empty;
        public string Link { get; set; } = string.Empty;
    }
}

public sealed class GatherPalette
{
    public Brush AccentBrush { get; }
    public Brush SurfaceBrush { get; }

    public GatherPalette(Brush accentBrush, Brush surfaceBrush)
    {
        AccentBrush = accentBrush;
        SurfaceBrush = surfaceBrush;
    }
}

public sealed partial class GatherCharacterCard : ObservableObject
{
    public string Name { get; }
    public string Subtitle { get; }
    public string AvatarText { get; }
    public string AvatarImageUrl { get; }
    public Brush AccentBrush { get; }
    public Brush AccentSurfaceBrush { get; }
    public ObservableCollection<GatherTrackItem> Materials { get; }

    [ObservableProperty] private ImageSource? _avatarImage;

    public GatherCharacterCard(
        string name,
        string subtitle,
        string avatarText,
        string avatarImageUrl,
        GatherPalette palette,
        IEnumerable<GatherTrackItem> materials)
    {
        Name = name;
        Subtitle = subtitle;
        AvatarText = avatarText;
        AvatarImageUrl = avatarImageUrl;
        AccentBrush = palette.AccentBrush;
        AccentSurfaceBrush = palette.SurfaceBrush;
        Materials = new ObservableCollection<GatherTrackItem>(materials);
    }
}

public sealed class GatherTrackSection
{
    public string Title { get; }
    public string Description { get; }
    public ObservableCollection<GatherTrackItem> Items { get; }

    public GatherTrackSection(string title, string description, IEnumerable<GatherTrackItem> items)
    {
        Title = title;
        Description = description;
        Items = new ObservableCollection<GatherTrackItem>(items);
    }
}

public sealed partial class GatherTrackItem : ObservableObject
{
    public string Name { get; }
    public string Description { get; }
    public string ShortLabel { get; }
    public Brush AccentBrush { get; }
    public Brush AccentSurfaceBrush { get; }
    public string IconUrl { get; }
    public IReadOnlyList<string> SearchKeywords { get; }

    [ObservableProperty] private ImageSource? _iconImage;

    public GatherTrackItem(
        string name,
        string description,
        string shortLabel,
        Brush accentBrush,
        Brush accentSurfaceBrush,
        string iconUrl,
        IReadOnlyList<string> searchKeywords)
    {
        Name = name;
        Description = description;
        ShortLabel = shortLabel;
        AccentBrush = accentBrush;
        AccentSurfaceBrush = accentSurfaceBrush;
        IconUrl = iconUrl;
        SearchKeywords = searchKeywords;
    }
}

using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.I18n;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BetterGenshinImpact.ViewModel.Windows;

/// <summary>
/// 一键宏配置窗口：按角色编辑 User/avatar_macro.json 中的宏脚本和宏编号
/// </summary>
public partial class AvatarMacroConfigViewModel : ViewModel
{
    /// <summary>
    /// 请求关闭窗口，参数为对话框结果
    /// </summary>
    public event Action<bool?>? CloseRequested;

    /// <summary>
    /// 一键宏配置文件路径
    /// </summary>
    private readonly string _jsonPath;

    /// <summary>
    /// 全部角色宏，保存时按此顺序写回
    /// </summary>
    private readonly List<AvatarMacroItemViewModel> _allAvatars;

    /// <summary>
    /// 按搜索文本过滤后的角色列表
    /// </summary>
    public ObservableCollection<AvatarMacroItemViewModel> FilteredAvatars { get; } = [];

    /// <summary>
    /// 宏编号下拉选项，0 表示使用默认战斗宏编号
    /// </summary>
    public List<KeyValuePair<int, string>> MacroPriorityOptions { get; }

    /// <summary>
    /// 角色搜索文本
    /// </summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>
    /// 当前编辑的角色
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedAvatar))]
    private AvatarMacroItemViewModel? _selectedAvatar;

    /// <summary>
    /// 当前显示的宏页签索引（0~4 对应宏 1~5）
    /// </summary>
    [ObservableProperty]
    private int _selectedMacroIndex;

    /// <summary>
    /// 是否已选中角色，未选中时隐藏编辑区
    /// </summary>
    public bool HasSelectedAvatar => SelectedAvatar != null;

    /// <summary>
    /// 读取配置文件并创建编辑数据，配置文件格式错误时抛出异常
    /// </summary>
    public AvatarMacroConfigViewModel()
    {
        _jsonPath = OneKeyFightTask.GetAvatarMacroJsonPath();
        _allAvatars = LoadAvatars(_jsonPath);

        var t = I18nService.Instance;
        var defaultPriority = ConfigService.Config!.MacroConfig.CombatMacroPriority;
        MacroPriorityOptions =
        [
            new(0, string.Format(t.Translate("使用默认宏编号（当前为 {0}）"), defaultPriority)),
            .. Enumerable.Range(1, 5).Select(i => new KeyValuePair<int, string>(i, string.Format(t.Translate("宏 {0}"), i)))
        ];

        ApplyFilter();
        SelectedAvatar = FilteredAvatars.FirstOrDefault(a => a.IsConfigured) ?? FilteredAvatars.FirstOrDefault();
    }

    /// <summary>
    /// 读取用户配置，并补充默认配置中新增的角色（例如版本更新后的新角色）
    /// </summary>
    /// <param name="jsonPath">用户配置文件路径</param>
    private static List<AvatarMacroItemViewModel> LoadAvatars(string jsonPath)
    {
        var macros = ReadMacros(jsonPath);
        var names = new HashSet<string>(macros.Select(m => m.Name));

        // 用户配置只在首次使用时从默认配置复制，之后新增的角色需要从默认配置补充
        var defaultPath = Global.Absolute("User/avatar_macro_default.json");
        if (File.Exists(defaultPath))
        {
            macros.AddRange(ReadMacros(defaultPath).Where(m => names.Add(m.Name)));
        }

        return macros.Select(m => new AvatarMacroItemViewModel(m)).ToList();
    }

    /// <summary>
    /// 反序列化角色宏列表；模型由 System.Text.Json 序列化，这里保持一致
    /// </summary>
    /// <param name="path">配置文件路径</param>
    private static List<AvatarMacro> ReadMacros(string path)
    {
        var json = File.ReadAllText(path);
        var macros = JsonSerializer.Deserialize<List<AvatarMacro>>(json, ConfigService.JsonOptions) ?? [];
        return macros.Where(m => !string.IsNullOrWhiteSpace(m.Name)).ToList();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedAvatarChanged(AvatarMacroItemViewModel? value)
    {
        if (value == null)
        {
            return;
        }

        // 切换角色时直接定位到该角色实际会执行的宏
        var priority = value.MacroPriority > 0 ? value.MacroPriority : ConfigService.Config!.MacroConfig.CombatMacroPriority;
        SelectedMacroIndex = priority is >= 1 and <= 5 ? priority - 1 : 0;
    }

    /// <summary>
    /// 按搜索文本刷新角色列表，并尽量保留当前选中项
    /// </summary>
    private void ApplyFilter()
    {
        var selected = SelectedAvatar;
        var keyword = SearchText.Trim();

        FilteredAvatars.Clear();
        foreach (var avatar in _allAvatars.Where(a => keyword.Length == 0 || a.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            FilteredAvatars.Add(avatar);
        }

        // Clear 会让列表把选中项置空，这里恢复
        SelectedAvatar = selected != null && FilteredAvatars.Contains(selected) ? selected : FilteredAvatars.FirstOrDefault();
    }

    /// <summary>
    /// 校验全部宏脚本后写回配置文件；一键宏下次触发时会根据文件修改时间自动重新加载
    /// </summary>
    [RelayCommand]
    private void Save()
    {
        // 1. 逐个解析非空脚本，收集格式错误
        var errors = new List<string>();
        foreach (var avatar in _allAvatars)
        {
            for (var i = 1; i <= 5; i++)
            {
                var script = avatar.GetScriptContent(i).Trim();
                if (script.Length == 0)
                {
                    continue;
                }

                try
                {
                    CombatScriptParser.ParseLineCommands(script, avatar.Name);
                }
                catch (Exception e)
                {
                    errors.Add($"{avatar.Name} - {string.Format(I18nService.Instance.Translate("宏 {0}"), i)}：{e.Message}");
                }
            }
        }

        if (errors.Count > 0)
        {
            ThemedMessageBox.Error(I18nService.Instance.Translate("以下宏脚本格式有误，请修改后再保存：") + Environment.NewLine + string.Join(Environment.NewLine, errors));
            return;
        }

        // 2. 写回配置文件
        try
        {
            var macros = _allAvatars.Select(a => a.ToModel()).ToList();
            File.WriteAllText(_jsonPath, JsonSerializer.Serialize(macros, ConfigService.JsonOptions));
            CloseRequested?.Invoke(true);
        }
        catch (Exception e)
        {
            ThemedMessageBox.Error(I18nService.Instance.Translate("保存一键宏配置失败：") + e.Message);
        }
    }

    /// <summary>
    /// 放弃修改并关闭窗口
    /// 暂不提示未保存的修改
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(false);
    }

    /// <summary>
    /// 打开一键宏脚本说明文档
    /// </summary>
    [RelayCommand]
    private void GoToOneKeyMacroUrl()
    {
        Process.Start(new ProcessStartInfo("https://www.bettergi.com/feats/macro/onem.html") { UseShellExecute = true });
    }
}

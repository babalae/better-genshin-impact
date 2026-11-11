using BetterGenshinImpact.GameTask.AutoFight.Model;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Windows;

/// <summary>
/// 一键宏配置窗口中的单个角色宏，编辑时使用，保存时转换回 <see cref="AvatarMacro"/>
/// </summary>
public partial class AvatarMacroItemViewModel : ObservableObject
{
    /// <summary>
    /// 角色名称，与出战角色识别结果匹配，不允许在界面中修改
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 宏 1 脚本
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfigured))]
    private string _scriptContent1;

    /// <summary>
    /// 宏 2 脚本
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfigured))]
    private string _scriptContent2;

    /// <summary>
    /// 宏 3 脚本
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfigured))]
    private string _scriptContent3;

    /// <summary>
    /// 宏 4 脚本
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfigured))]
    private string _scriptContent4;

    /// <summary>
    /// 宏 5 脚本
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfigured))]
    private string _scriptContent5;

    /// <summary>
    /// 角色使用的宏编号（1~5），0 表示使用默认战斗宏编号
    /// </summary>
    [ObservableProperty]
    private int _macroPriority;

    /// <summary>
    /// 是否至少配置了一个宏，用于在角色列表中标记
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ScriptContent1)
        || !string.IsNullOrWhiteSpace(ScriptContent2)
        || !string.IsNullOrWhiteSpace(ScriptContent3)
        || !string.IsNullOrWhiteSpace(ScriptContent4)
        || !string.IsNullOrWhiteSpace(ScriptContent5);

    /// <summary>
    /// 从配置文件中的角色宏创建编辑项
    /// </summary>
    /// <param name="macro">配置文件中的角色宏</param>
    public AvatarMacroItemViewModel(AvatarMacro macro)
    {
        // JSON 中显式写 null 时按空字符串处理
        Name = macro.Name ?? string.Empty;
        _scriptContent1 = macro.ScriptContent1 ?? string.Empty;
        _scriptContent2 = macro.ScriptContent2 ?? string.Empty;
        _scriptContent3 = macro.ScriptContent3 ?? string.Empty;
        _scriptContent4 = macro.ScriptContent4 ?? string.Empty;
        _scriptContent5 = macro.ScriptContent5 ?? string.Empty;
        // 非法编号按运行时逻辑视为使用默认编号
        _macroPriority = macro.IsValidMacroPriority() ? macro.MacroPriority : 0;
    }

    /// <summary>
    /// 按编号获取宏脚本
    /// </summary>
    /// <param name="index">宏编号（1~5）</param>
    public string GetScriptContent(int index)
    {
        return index switch
        {
            1 => ScriptContent1,
            2 => ScriptContent2,
            3 => ScriptContent3,
            4 => ScriptContent4,
            5 => ScriptContent5,
            _ => string.Empty
        };
    }

    /// <summary>
    /// 转换为用于保存的角色宏，脚本去除首尾空白
    /// </summary>
    public AvatarMacro ToModel()
    {
        return new AvatarMacro
        {
            Name = Name,
            ScriptContent1 = ScriptContent1.Trim(),
            ScriptContent2 = ScriptContent2.Trim(),
            ScriptContent3 = ScriptContent3.Trim(),
            ScriptContent4 = ScriptContent4.Trim(),
            ScriptContent5 = ScriptContent5.Trim(),
            MacroPriority = MacroPriority
        };
    }
}

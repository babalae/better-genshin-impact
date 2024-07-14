using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Model;

/// <summary>
/// 脚本执行组
/// </summary>
public partial class ScriptGroup : ObservableObject
{
    /// <summary>
    /// 组名称
    /// </summary>
    [ObservableProperty]
    private string _groupName = string.Empty;
}

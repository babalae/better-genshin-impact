using System.Windows.Input;
using BetterGenshinImpact.Pulonia.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 一个可选为“实例当前账户”的账户：账户页顶部、标题栏账户徽标、主实例的子实例菜单和新建云原神实例窗口共用。
/// </summary>
public sealed partial class PuloniaAccountChoiceViewModel : ObservableObject
{
    /// <summary>账户 ID。</summary>
    public string Id { get; }

    /// <summary>账户名称。</summary>
    public string Name { get; }

    /// <summary>下拉框与菜单中的显示文字：名称加 UID 尾号。</summary>
    public string DisplayText { get; }

    /// <summary>是否是所属实例的当前账户，菜单据此显示勾选。</summary>
    [ObservableProperty] private bool _isCurrent;

    /// <summary>选择此账户的命令；只用于显示的场合（下拉框）为 null。</summary>
    public ICommand? SelectCommand { get; }

    /// <summary>
    /// 由账户资料建立选项。
    /// </summary>
    /// <param name="account">已保存的账户资料</param>
    /// <param name="isCurrent">是否是所属实例的当前账户</param>
    /// <param name="selectCommand">选择此账户的命令，参数为本选项</param>
    public PuloniaAccountChoiceViewModel(PuloniaGenshinAccountProfile account, bool isCurrent = false, ICommand? selectCommand = null)
    {
        Id = account.Id;
        Name = account.Name;
        DisplayText = Describe(account);
        _isCurrent = isCurrent;
        SelectCommand = selectCommand;
    }

    /// <summary>
    /// 账户的显示文字：“乙（UID 尾号 1234）”；没有 UID 时只显示名称。
    /// </summary>
    /// <param name="account">账户资料</param>
    public static string Describe(PuloniaGenshinAccountProfile account)
        => account.Uid is { Length: > 0 } uid ? $"{account.Name}（UID 尾号 {UidTail(uid)}）" : account.Name;

    /// <summary>UID 尾号（最后 4 位）。</summary>
    /// <param name="uid">完整 UID</param>
    public static string UidTail(string uid) => uid.Length <= 4 ? uid : uid[^4..];
}

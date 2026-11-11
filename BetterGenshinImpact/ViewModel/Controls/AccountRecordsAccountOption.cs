using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Controls;

/// <summary>狛荷屋 UID 下拉框的一项：Pulonia 账户目录中填写了 UID 的账户，显示为“名称（UID）”，选中值是 UID。</summary>
public sealed partial class AccountRecordsAccountOption : ObservableObject
{
    /// <summary>账户 ID；账户的 UID 修改后据此继续选中同一账户。</summary>
    public string AccountId { get; }

    /// <summary>完整 UID，也是狛荷屋数据目录名。</summary>
    public string Uid { get; }

    /// <summary>账户名称；在账户页改名后原位更新，不重建选项。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    private string _name;

    /// <summary>建立一个账户选项。</summary>
    /// <param name="accountId">账户 ID</param>
    /// <param name="name">账户名称</param>
    /// <param name="uid">完整 UID</param>
    public AccountRecordsAccountOption(string accountId, string name, string uid)
    {
        AccountId = accountId;
        Uid = uid;
        _name = name;
    }

    /// <summary>下拉框中显示的文字。</summary>
    public string DisplayText => $"{Name}（{Uid}）";

    /// <summary>无模板展示时同样显示“名称（UID）”。</summary>
    public override string ToString() => DisplayText;
}

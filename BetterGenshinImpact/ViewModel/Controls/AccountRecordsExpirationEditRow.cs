using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Controls;

/// <summary>库存编辑弹窗中的一个到期批次，输入文本在保存时统一校验。</summary>
public partial class AccountRecordsExpirationEditRow : ObservableObject
{
    /// <summary>该批正整数数量。</summary>
    [ObservableProperty] private string _quantityText = "";
    /// <summary>显式带时区的到期时间。</summary>
    [ObservableProperty] private string _expiresAtText = "";
}

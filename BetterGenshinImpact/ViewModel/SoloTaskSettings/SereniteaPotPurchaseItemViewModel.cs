using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.SoloTaskSettings;

/// <summary>洞天百宝商品的勾选草稿，仅供当前尘歌壶任务使用。</summary>
public partial class SereniteaPotPurchaseItemViewModel : ObservableObject
{
    /// <summary>商品名称，与任务识别及购买参数保持一致。</summary>
    public string Name { get; }

    /// <summary>是否购买此商品。</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>从商品名称及当前购买配置建立勾选项。</summary>
    /// <param name="name">商品名称。</param>
    /// <param name="isSelected">当前是否已配置购买。</param>
    public SereniteaPotPurchaseItemViewModel(string name, bool isSelected)
    {
        Name = name;
        _isSelected = isSelected;
    }
}

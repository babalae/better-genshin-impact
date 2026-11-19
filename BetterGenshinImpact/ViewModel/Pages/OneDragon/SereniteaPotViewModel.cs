using CommunityToolkit.Mvvm.ComponentModel;
using BetterGenshinImpact.Service.I18n;

namespace BetterGenshinImpact.ViewModel.Pages.OneDragon;

public partial class SereniteaPotViewModel : OneDragonBaseViewModel
{
    public override string Title { get; } = I18nService.Instance.Translate("领取尘歌壶奖励");
}

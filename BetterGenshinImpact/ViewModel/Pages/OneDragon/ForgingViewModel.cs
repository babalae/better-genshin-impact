using BetterGenshinImpact.Service.I18n;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages.OneDragon;

public partial class ForgingViewModel : OneDragonBaseViewModel
{
    public override string Title { get; } = I18nService.Instance.Translate("合成浓缩树脂");
}

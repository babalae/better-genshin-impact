using CommunityToolkit.Mvvm.ComponentModel;
using BetterGenshinImpact.Service.Interface;

namespace BetterGenshinImpact.ViewModel.Pages.OneDragon;

public partial class TcgViewModel : OneDragonBaseViewModel
{
    public override string Title { get; } = App.GetService<ILocalizationService>().GetString("oneDragon.tcg.title");
}

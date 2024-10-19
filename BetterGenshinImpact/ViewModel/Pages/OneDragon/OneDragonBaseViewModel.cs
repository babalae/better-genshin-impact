using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages.OneDragon;

public abstract partial class OneDragonBaseViewModel : ObservableObject, IViewModel
{
    public abstract string Title { get; }
}

using System;
using BetterGenshinImpact.Service.Interface;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages.OneDragon;

public partial class CraftViewModel : OneDragonBaseViewModel, IDisposable
{
    private readonly ILocalizationService _localizationService;

    public CraftViewModel()
    {
        _localizationService = App.GetService<ILocalizationService>();
        _localizationService.LanguageChanged += OnLanguageChanged;
    }

    public override string Title => _localizationService.GetString("oneDragon.craft.title");

    private void OnLanguageChanged(object? sender, LanguageChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Title));
    }

    public void Dispose()
    {
        _localizationService.LanguageChanged -= OnLanguageChanged;
    }
}

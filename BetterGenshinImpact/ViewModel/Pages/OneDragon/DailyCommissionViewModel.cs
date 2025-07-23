using System;
using BetterGenshinImpact.Service.Interface;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages.OneDragon;

public partial class DailyCommissionViewModel : OneDragonBaseViewModel, IDisposable
{
    private readonly ILocalizationService _localizationService;

    public DailyCommissionViewModel()
    {
        _localizationService = App.GetService<ILocalizationService>();
        _localizationService.LanguageChanged += OnLanguageChanged;
    }

    public override string Title => _localizationService.GetString("oneDragon.dailyCommission.title");

    private void OnLanguageChanged(object? sender, LanguageChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Title));
    }

    public void Dispose()
    {
        _localizationService.LanguageChanged -= OnLanguageChanged;
    }
}

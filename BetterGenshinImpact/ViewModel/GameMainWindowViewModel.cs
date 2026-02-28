using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.ViewModel;

public partial class GameMainWindowViewModel : ViewModel
{
    private readonly ILogger<GameMainWindowViewModel> _logger = App.GetLogger<GameMainWindowViewModel>();

    public GameMainWindowViewModel()
    {
        _logger.LogError("GameMainWindowViewModel constructor called");
    }
}
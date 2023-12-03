using CommunityToolkit.Mvvm.ComponentModel;
using MicaSetup.Controls;
using MicaSetup.Design.Controls;
using System.Linq;

namespace MicaSetup.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    [ObservableProperty]
    private string route = null!;

    public ShellViewModel()
    {
        Routing.RegisterRoute();
        Route = ShellPageSetting.PageDict.FirstOrDefault().Key;
    }
}

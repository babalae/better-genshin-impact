using BetterGenshinImpact.ViewModel;

#if DEBUG

using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.ClearScript;
using Microsoft.ClearScript.V8;
using Vanara.PInvoke;
using Wpf.Ui.Controls;
using System.Diagnostics;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class ScriptControlViewModel : ObservableObject, INavigationAware, IViewModel
{
    public ScriptControlViewModel()
    {
        using IScriptEngine engine = new V8ScriptEngine();
        engine.AddHostObject("lib", new HostTypeCollection("mscorlib", "System.Core"));
        engine.AddHostObject("win32", new HostTypeCollection("Vanara.PInvoke.User32"));
        object test = engine.Evaluate("win32.Vanara.PInvoke.User32.GetActiveWindow()");
        Debug.WriteLine((int)User32.GetActiveWindow());
        Debug.WriteLine((int)(HWND)test);
    }

    public void OnNavigatedFrom()
    {
    }

    public void OnNavigatedTo()
    {
    }
}

#else

public partial class ScriptControlViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject, IViewModel { }

#endif

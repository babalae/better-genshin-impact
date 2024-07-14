using BetterGenshinImpact.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.ClearScript;
using Microsoft.ClearScript.V8;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Vanara.PInvoke;
using Wpf.Ui.Controls;
using static System.Net.Mime.MediaTypeNames;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class ScriptControlViewModel : ObservableObject, INavigationAware, IViewModel
{
    [ObservableProperty]
    private ObservableCollection<ScriptGroup> _scriptGroups = [];

    public ScriptControlViewModel()
    {
        // const V8ScriptEngineFlags flags = V8ScriptEngineFlags.UseCaseInsensitiveMemberBinding;
        // using IScriptEngine engine = new V8ScriptEngine(flags);
        // engine.AddHostObject("lib", new HostTypeCollection("mscorlib", "System.Core"));
        // engine.AddHostObject("win32", new HostTypeCollection("Vanara.PInvoke.User32"));
        // object test = engine.Evaluate("win32.Vanara.PInvoke.User32.getActiveWindow()");
        // Debug.WriteLine((int)User32.GetActiveWindow());
        // Debug.WriteLine((int)(HWND)test);

        //using var runtime = new V8Runtime();
        //using var engine1 = runtime.CreateScriptEngine();
        //engine1.Compile("foo = { bar: 123 }");
        //Debug.WriteLine(engine1.Script.foo);

        // _scriptGroups 加入测试数据
        for (int i = 0; i < 3; i++)
        {
            _scriptGroups.Add(new ScriptGroup { GroupName = $"测试组{i}" });
        }
    }

    public void OnNavigatedFrom()
    {
    }

    public void OnNavigatedTo()
    {
    }
}

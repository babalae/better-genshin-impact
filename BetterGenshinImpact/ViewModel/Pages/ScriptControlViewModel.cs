using BetterGenshinImpact.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.ClearScript;
using Microsoft.ClearScript.V8;
using System.Collections.ObjectModel;
using System.Diagnostics;
using BetterGenshinImpact.Script.Dependence;
using Vanara.PInvoke;
using Wpf.Ui.Controls;
using static System.Net.Mime.MediaTypeNames;
using Microsoft.ClearScript.JavaScript;
using System.Threading.Tasks;
using System;
using BetterGenshinImpact.Script;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class ScriptControlViewModel : ObservableObject, INavigationAware, IViewModel
{
    [ObservableProperty]
    private ObservableCollection<ScriptGroup> _scriptGroups = [];

    public ScriptControlViewModel()
    {
        //using var runtime = new V8Runtime();
        //using var engine1 = runtime.CreateScriptEngine();
        //engine1.Compile("foo = { bar: 123 }");
        //Debug.WriteLine(engine1.Script.foo);

        // _scriptGroups 加入测试数据
        for (int i = 0; i < 3; i++)
        {
            _scriptGroups.Add(new ScriptGroup { GroupName = $"测试组{i}" });
        }
        RunMulti();
    }

    public void OnNavigatedFrom()
    {
    }

    public void OnNavigatedTo()
    {
    }

    public void RunMulti()
    {
        IScriptEngine engine = new V8ScriptEngine(V8ScriptEngineFlags.UseCaseInsensitiveMemberBinding | V8ScriptEngineFlags.EnableTaskPromiseConversion);
        EngineExtend.InitHost(engine);

        // 执行脚本
        engine.Execute(@"
(async function() {
    log.info('等待 {m} s', 3);
    await sleep(3000);
    log.info('test {name}', '你好');
})();
");
    }
}

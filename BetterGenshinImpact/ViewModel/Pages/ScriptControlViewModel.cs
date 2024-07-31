using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.ClearScript;
using Microsoft.ClearScript.V8;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using Wpf.Ui.Controls;
using BetterGenshinImpact.GameTask.Model.Enum;
using System;

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

        new TaskRunner(DispatcherTimerOperationEnum.UseSelfCaptureImage).FireAndForget(async () =>
        {
            await (Task)engine.Evaluate(@"
            (async function() {
                log.info('等待 {m} s', 1);
                await sleep(1000);
                log.info('测试 {name}', 'TP方法');
                await genshin.tp(3452.310059,2290.465088);
                log.warn('TP完成');
                //await sleep(1000);
                //await runKeyMouseScript('操作1.json');
            })();
            ");
        });
    }
}

using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.Service;
using Serilog.Core;

namespace BetterGenshinImpact.Core.Script.WebView;

/// <summary>
/// 给 WebView 提供的桥接类
/// 用于调用 C# 方法
/// </summary>
[ClassInterface(ClassInterfaceType.AutoDual)]
[ComVisible(true)]
public class MapEditorWebBridge
{
    public void ChangeMapName(string mapName)
    {
        TaskContext.Instance().Config.DevConfig.RecordMapName = mapName;
    }

    public async Task RunPathing(string json)
    {
        await ScriptService.StartGameTask();
        SystemControl.ActivateWindow();
        await new TaskRunner().RunThreadAsync(async () =>
        {
            var task = PathingTask.BuildFromJson(json);
            var pathExecutor = new PathExecutor(CancellationContext.Instance.Cts.Token);
            await pathExecutor.Pathing(task);
        });
    }
}

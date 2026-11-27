using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.Service;
using Microsoft.Extensions.Logging;

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
        using var cancellationScope = CancellationContext.Instance.TryBeginScope();
        if (cancellationScope == null)
        {
            App.GetLogger<MapEditorWebBridge>().LogWarning("当前存在正在执行的任务，请勿重复启动");
            return;
        }

        await ScriptService.StartGameTask();
        if (cancellationScope.Token.IsCancellationRequested)
        {
            return;
        }

        SystemControl.ActivateWindow();
        await new TaskRunner().RunThreadAsync(async () =>
        {
            var task = PathingTask.BuildFromJson(json);
            var pathExecutor = new PathExecutor(CancellationContext.Instance.Cts.Token);
            pathExecutor.PartyConfig = new PathingPartyConfig { AutoFightEnabled = false };
            await pathExecutor.Pathing(task);
        });
    }
}

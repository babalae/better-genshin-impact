using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
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
    /// <summary>
    /// 配置根。编辑器窗口的生命周期独立于运行环境，桥接对象只持有配置；试跑时再从 TaskRunner 拿运行环境
    /// </summary>
    private readonly AllConfig _config;

    /// <summary>
    /// 创建地图编辑器的桥接对象
    /// </summary>
    /// <param name="config">配置根</param>
    public MapEditorWebBridge(AllConfig config)
    {
        _config = config;
    }

    public void ChangeMapName(string mapName)
    {
        _config.DevConfig.RecordMapName = mapName;
    }

    public async Task RunPathing(string json)
    {
        // 试跑寻路：由 TaskRunner 绑定运行环境后交给本次寻路
        await new TaskRunner().RunThreadAsync(async (runtime, ct) =>
        {
            runtime.Window.Activate();
            var task = PathingTask.BuildFromJson(json);
            var pathExecutor = new PathExecutor(runtime, _config, ct);
            pathExecutor.PartyConfig = new PathingPartyConfig { AutoFightEnabled = false };
            // 编辑器试跑只等待本次寻路结果，保留原有预览行为。
            _ = await pathExecutor.Pathing(task);
        });
    }
}

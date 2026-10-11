using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.Runtime;

/// <summary>
/// 截图器 Start 参数的唯一构建处，各 Provider 共用
/// </summary>
public static class GameCaptureSettings
{
    public static Dictionary<string, object> From(AllConfig config)
    {
        return new Dictionary<string, object>
        {
            // BitBlt 必须传这个键，否则 Start 直接返回，截图器不会启动
            { "autoFixWin11BitBlt", OsVersionHelper.IsWindows11_OrGreater && config.AutoFixWin11BitBlt },
            // WGC 限流：0 = 不启用；>0 = DWM 最小推帧间隔（毫秒）
            { "MinUpdateIntervalMs", config.WgcMinUpdateIntervalMs },
            // WGC V2 开关：CPU 颜色转换回退（默认 GPU 打包）
            { "UseCpuConvert", config.WgcV2UseCpuConvert },
        };
    }
}

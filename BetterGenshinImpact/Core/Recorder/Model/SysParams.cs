using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.Core.Recorder.Model;

public class SysParams
{
    /// <summary>
    /// 提高指针精确度 是否启用
    /// </summary>
    public bool EnhancePointerPrecision { get; set; }

    public SysParams()
    {
        var (mouseThreshold1, mouseThreshold2, mouseThreshold3) = EnvironmentUtil.GetMouse();
        if (mouseThreshold3 > 0)
        {
            EnhancePointerPrecision = true;
        }
        else
        {
            EnhancePointerPrecision = false;
        }
    }
}
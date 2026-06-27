using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.Model;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoWood.Assets;

public class AutoWoodAssets : Singleton<AutoWoodAssets>
{
    private readonly ISystemInfo systemInfo;

    // 木头数量
    public Rect WoodCountUpperRect;

    private double AssetScale => systemInfo.AssetScale;

    private AutoWoodAssets()
    {
        systemInfo = TaskContext.Instance().SystemInfo;

        WoodCountUpperRect = new Rect((int)(100 * AssetScale), (int)(450 * AssetScale), (int)(300 * AssetScale), (int)(250 * AssetScale));
    }
}

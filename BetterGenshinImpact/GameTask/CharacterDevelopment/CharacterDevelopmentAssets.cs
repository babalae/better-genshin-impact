using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model.Assets;

namespace BetterGenshinImpact.GameTask.CharacterDevelopment;

/// <summary>
/// 按截图分辨率提供角色养成 Task 的模板识别对象。
/// </summary>
/// <remarks>菜单和筛选模板均只在画面左下区域匹配，避免角色立绘和文字造成误命中。</remarks>
internal sealed class CharacterDevelopmentAssets
{
    private const string TaskName = "CharacterDevelopment";
    private static readonly CaptureAssetsCache<CharacterDevelopmentAssets> Cache = new(static size => new CharacterDevelopmentAssets(size));

    public RecognitionObject MenuRo { get; }
    public RecognitionObject FilterRo { get; }

    private CharacterDevelopmentAssets(CaptureSize captureSize)
    {
        MenuRo = RecognitionAssets.Get(TaskName, "Menu", captureSize.Width, captureSize.Height);
        FilterRo = RecognitionAssets.Get(TaskName, "Filter", captureSize.Width, captureSize.Height);
    }

    public static CharacterDevelopmentAssets Get(int captureWidth, int captureHeight)
    {
        return Cache.Get(captureWidth, captureHeight);
    }
}

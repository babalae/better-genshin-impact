namespace BetterGenshinImpact.Helpers.Extensions;

public static class BooleanExtension
{
    public static string ToChinese(this bool enabled)
    {
        return enabled ? "开启" : "关闭";
    }
}
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.Model.Gear.Parameter;

public class JavascriptGearTaskParams : BaseGearTaskParams
{
    public string FolderName { get; set; } = string.Empty;

    /// <summary>
    /// js脚本参数
    /// </summary>
    public dynamic? Context { get; set; } = null;
    
    public PathingPartyConfig? PathingPartyConfig { get; set; }
}
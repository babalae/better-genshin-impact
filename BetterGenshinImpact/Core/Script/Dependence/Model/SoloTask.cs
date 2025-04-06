using Microsoft.ClearScript;

namespace BetterGenshinImpact.Core.Script.Dependence.Model;

/// <summary>
/// 独立任务
/// </summary>
public class SoloTask
{
    /// <summary>
    /// 独立任务名称
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 独立任务配置
    /// </summary>
    public object? Config { get; set; }

    public SoloTask(string name)
    {
        Name = name;
    }

    public SoloTask(string name, ScriptObject config)
    {
        Name = name;
        Config = config;
    }
    
    public string ConfigLcb { get; set; }//LCB保留文字传入接口

    public SoloTask(string name, string configLcb)//LCB保留文字传入接口
    {
        Name = name;
        ConfigLcb = configLcb;
    }
    
}

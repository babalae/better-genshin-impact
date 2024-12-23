using System;

namespace BetterGenshinImpact.Core.Recorder.Model;

[Serializable]
public class KeyMouseScriptInfo
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string? Author { get; set; }

    public string? Version { get; set; }

    /// <summary>
    /// 制作时 BetterGI 的版本，用于兼容性检查
    /// </summary>
    public string? BgiVersion { get; set; }

    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public double RecordDpi { get; set; } = 1.0;
    
    public string StartTime { get; set; } = string.Empty;
    
    public string StartTimeUnixTimestamp { get; set; } = string.Empty;
    
    public SysParams? SysParams { get; set; }
}

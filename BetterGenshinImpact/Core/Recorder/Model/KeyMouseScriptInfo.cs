using System;

namespace BetterGenshinImpact.Core.Recorder.Model;

[Serializable]
public class KeyMouseScriptInfo
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public double RecordDpi { get; set; } = 1.0;
}

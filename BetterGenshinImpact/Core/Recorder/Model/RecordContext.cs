using BetterGenshinImpact.Model;

namespace BetterGenshinImpact.Core.Recorder.Model;

public class RecordContext : Singleton<RecordContext>
{
    
    public SysParams SysParams { get; set; } = new();
    
}
using System.IO;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recorder;
using BetterGenshinImpact.Core.Script;

namespace BetterGenshinImpact.Model.Gear.Tasks;

public class KeyMouseGearTask : BaseGearTask
{
    public KeyMouseGearTask(string path)
    {
        FilePath = path;
    }
    
    public override async Task Run()
    {
        // 加载并执行
        var json = await File.ReadAllTextAsync(FilePath);
        await KeyMouseMacroPlayer.PlayMacro(json, CancellationContext.Instance.Cts.Token, false);
    }
}
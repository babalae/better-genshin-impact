using System.IO;
using System.Threading;
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
    
    public override async Task Run(CancellationToken ct)
    {
        // 加载并执行
        var json = await File.ReadAllTextAsync(FilePath, ct);
        await KeyMouseMacroPlayer.PlayMacro(json, ct, false);
    }
}
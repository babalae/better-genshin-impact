using System.Threading.Tasks;

namespace BetterGenshinImpact.Model.Gear;

/// <summary>
/// 直接使用C#反射来执行任务的GearTask
/// </summary>
public class CSharpReflectionGearTask : BaseGearTask
{
    public override Task Run(params object[] configs)
    {
        throw new System.NotImplementedException();
    }
}
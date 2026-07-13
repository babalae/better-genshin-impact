using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Model;

namespace BetterGenshinImpact.GameTask.Shell;

public class ShellTaskParam : BaseTaskParam<ShellTask>
{
    private ShellTaskParam(string command, int configTimeoutSeconds, bool configNoWindow, bool configOutput, bool configDisable) : base(null, null)
    {
        Command = command;
        TimeoutSeconds = configTimeoutSeconds;
        NoWindow = configNoWindow;
        Output = configOutput;
        Disable = configDisable;
    }

    public readonly bool Disable;
    public readonly string Command;
    public readonly int TimeoutSeconds;
    public readonly bool NoWindow;
    public readonly bool Output;

    public static ShellTaskParam BuildFromConfig(string command, ShellConfig config)
    {
        return new ShellTaskParam(command, config.Timeout, config.NoWindow, config.Output, config.Disable);
    }
}

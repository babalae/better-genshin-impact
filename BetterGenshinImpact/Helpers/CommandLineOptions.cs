using System;
using System.Linq;

namespace BetterGenshinImpact.Helpers;

/// <summary>
/// 命令行参数统一解析，启动时解析一次，各处查询解析结果。
/// </summary>
public class CommandLineOptions
{
    public const string ChildSessionArgument = "--childSession";

    private static CommandLineOptions? _instance;

    public static CommandLineOptions Instance => _instance ??= Parse(Environment.GetCommandLineArgs());

    public CommandLineAction Action { get; }

    /// <summary>
    /// 当前 BetterGI 是否运行在 Child Session 内。
    /// </summary>
    public bool IsChildSession { get; }

    /// <summary>
    /// startOneDragon 时可选的配置名称（第 3 个参数）
    /// </summary>
    public string? OneDragonConfigName { get; }

    /// <summary>
    /// --startGroups / --TaskProgress 时传入的组名列表（第 3 个参数起）
    /// </summary>
    public string[] GroupNames { get; } = [];

    /// <summary>
    /// 是否有命令行任务参数（startOneDragon / --startGroups / --TaskProgress / start）
    /// </summary>
    public bool HasTaskArgs => Action != CommandLineAction.None;

    /// <summary>
    /// 是否是需要 StartGameTask 自行处理游戏启动的命令
    /// （一条龙、配置组、任务进度由各自流程中的 StartGameTask 启动游戏）
    /// </summary>
    public bool ShouldDeferGameStart => Action is CommandLineAction.StartOneDragon
        or CommandLineAction.StartGroups
        or CommandLineAction.TaskProgress;

    private CommandLineOptions(
        CommandLineAction action,
        string? oneDragonConfigName = null,
        string[]? groupNames = null,
        bool isChildSession = false)
    {
        Action = action;
        OneDragonConfigName = oneDragonConfigName;
        GroupNames = groupNames ?? [];
        IsChildSession = isChildSession;
    }

    internal static CommandLineOptions Parse(string[] args)
    {
        var launchArgs = args.Skip(1).Select(x => x.Trim()).ToArray();
        var isChildSession = launchArgs.Any(x =>
            x.Equals(ChildSessionArgument, StringComparison.OrdinalIgnoreCase));
        var commandArgs = launchArgs.Where(x =>
            !x.Equals(ChildSessionArgument, StringComparison.OrdinalIgnoreCase)).ToArray();

        if (commandArgs.Length == 0)
            return new CommandLineOptions(CommandLineAction.None, isChildSession: isChildSession);

        var arg1 = commandArgs[0];
        var extra = commandArgs.Skip(1).ToArray();

        if (arg1.Contains("startOneDragon", StringComparison.OrdinalIgnoreCase))
        {
            return new CommandLineOptions(CommandLineAction.StartOneDragon,
                oneDragonConfigName: extra.Length > 0 ? extra[0] : null,
                isChildSession: isChildSession);
        }

        if (arg1.Equals("--startGroups", StringComparison.OrdinalIgnoreCase))
        {
            return new CommandLineOptions(
                CommandLineAction.StartGroups,
                groupNames: extra,
                isChildSession: isChildSession);
        }

        if (arg1.Equals("--TaskProgress", StringComparison.OrdinalIgnoreCase))
        {
            return new CommandLineOptions(
                CommandLineAction.TaskProgress,
                groupNames: extra,
                isChildSession: isChildSession);
        }

        if (arg1.Contains("start", StringComparison.OrdinalIgnoreCase))
        {
            return new CommandLineOptions(CommandLineAction.Start, isChildSession: isChildSession);
        }

        return new CommandLineOptions(CommandLineAction.None, isChildSession: isChildSession);
    }
}

public enum CommandLineAction
{
    /// <summary>双击启动，无命令行参数</summary>
    None,

    /// <summary>纯 "start" — 仅启动截图器</summary>
    Start,

    /// <summary>startOneDragon — 启动一条龙</summary>
    StartOneDragon,

    /// <summary>--startGroups — 启动调度组</summary>
    StartGroups,

    /// <summary>--TaskProgress — 启动任务进度</summary>
    TaskProgress,
}

using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Service.Instance;

namespace BetterGenshinImpact.Helpers;

/// <summary>
/// 命令行参数统一解析，启动时解析一次，各处查询解析结果。
/// </summary>
public class CommandLineOptions
{
    public const string InstanceArgument = "--instance";
    public const string InstanceIdArgument = "--instance-id";
    public const string ParentInstanceArgument = "--parent-instance";
    public const string ParentPipeArgument = "--parent-pipe";

    private static CommandLineOptions? _instance;

    public static CommandLineOptions Instance => _instance ??= Parse(Environment.GetCommandLineArgs());

    public CommandLineAction Action { get; }

    /// <summary>
    /// 当前 BetterGI 的实例类型。
    /// </summary>
    public BetterGiInstanceType InstanceType { get; }

    public bool IsPrimaryInstance => InstanceType == BetterGiInstanceType.Primary;

    /// <summary>
    /// 启动方为当前进程预分配的实例 ID。
    /// </summary>
    public string? RequestedInstanceId { get; }

    /// <summary>
    /// 父实例 ID。
    /// </summary>
    public string? ParentInstanceId { get; }

    /// <summary>
    /// 父实例公开的命名管道名称。
    /// </summary>
    public string? ParentPipeName { get; }

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
        BetterGiInstanceType instanceType = BetterGiInstanceType.Primary,
        string? requestedInstanceId = null,
        string? parentInstanceId = null,
        string? parentPipeName = null)
    {
        Action = action;
        OneDragonConfigName = oneDragonConfigName;
        GroupNames = groupNames ?? [];
        InstanceType = instanceType;
        RequestedInstanceId = requestedInstanceId;
        ParentInstanceId = parentInstanceId;
        ParentPipeName = parentPipeName;
    }

    internal static CommandLineOptions Parse(string[] args)
    {
        var launchArgs = args.Skip(1).Select(x => x.Trim()).ToArray();
        var instanceType = BetterGiInstanceType.Primary;
        string? requestedInstanceId = null;
        string? parentInstanceId = null;
        string? parentPipeName = null;
        var commandArgs = new List<string>();

        for (var index = 0; index < launchArgs.Length; index++)
        {
            var argument = launchArgs[index];
            if (argument.Equals(InstanceArgument, StringComparison.OrdinalIgnoreCase))
            {
                if (TryReadNext(launchArgs, ref index, out var instanceTypeValue))
                {
                    instanceType = instanceTypeValue.ToLowerInvariant() switch
                    {
                        "primary" => BetterGiInstanceType.Primary,
                        "childsession" => BetterGiInstanceType.ChildSession,
                        "webview" => BetterGiInstanceType.WebView,
                        _ => BetterGiInstanceType.Primary
                    };
                }
                continue;
            }

            if (argument.Equals(InstanceIdArgument, StringComparison.OrdinalIgnoreCase))
            {
                if (TryReadNext(launchArgs, ref index, out var instanceIdValue)
                    && InstanceIds.TryNormalize(instanceIdValue, out var parsedInstanceId))
                {
                    requestedInstanceId = parsedInstanceId;
                }
                continue;
            }

            if (argument.Equals(ParentInstanceArgument, StringComparison.OrdinalIgnoreCase))
            {
                if (TryReadNext(launchArgs, ref index, out var parentInstanceValue)
                    && InstanceIds.TryNormalize(parentInstanceValue, out var parsedParentInstanceId))
                {
                    parentInstanceId = parsedParentInstanceId;
                }
                continue;
            }

            if (argument.Equals(ParentPipeArgument, StringComparison.OrdinalIgnoreCase))
            {
                if (TryReadNext(launchArgs, ref index, out var parentPipeValue))
                {
                    parentPipeName = parentPipeValue;
                }
                continue;
            }

            commandArgs.Add(argument);
        }

        if (commandArgs.Count == 0)
        {
            return Create(CommandLineAction.None);
        }

        var arg1 = commandArgs[0];
        var extra = commandArgs.Skip(1).ToArray();

        if (arg1.Contains("startOneDragon", StringComparison.OrdinalIgnoreCase))
        {
            return Create(
                CommandLineAction.StartOneDragon,
                oneDragonConfigName: extra.Length > 0 ? extra[0] : null);
        }

        if (arg1.Equals("--startGroups", StringComparison.OrdinalIgnoreCase))
        {
            return Create(
                CommandLineAction.StartGroups,
                groupNames: extra);
        }

        if (arg1.Equals("--TaskProgress", StringComparison.OrdinalIgnoreCase))
        {
            return Create(
                CommandLineAction.TaskProgress,
                groupNames: extra);
        }

        if (arg1.Contains("start", StringComparison.OrdinalIgnoreCase))
        {
            return Create(CommandLineAction.Start);
        }

        return Create(CommandLineAction.None);

        CommandLineOptions Create(
            CommandLineAction action,
            string? oneDragonConfigName = null,
            string[]? groupNames = null)
        {
            return new CommandLineOptions(
                action,
                oneDragonConfigName,
                groupNames,
                instanceType,
                requestedInstanceId,
                parentInstanceId,
                parentPipeName);
        }
    }

    private static bool TryReadNext(string[] args, ref int index, out string value)
    {
        if (index + 1 >= args.Length)
        {
            value = string.Empty;
            return false;
        }

        value = args[++index];
        return true;
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

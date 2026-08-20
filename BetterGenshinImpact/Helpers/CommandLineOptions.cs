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
    public const string RestartFromProcessIdArgument = "--restart-from-pid";
    public const string McpArgument = "--mcp";
    public const string McpPortArgument = "--mcp-port";
    public const int DefaultMcpPort = 5042;

    private static CommandLineOptions? _instance;

    public static CommandLineOptions Instance => _instance ??= Parse(Environment.GetCommandLineArgs());

    public CommandLineAction Action { get; }

    /// <summary>
    /// 当前 BetterGI 的实例类型。
    /// </summary>
    public BetterGiInstanceType InstanceType { get; }

    /// <summary>
    /// 是否通过命令行明确指定了只能作为客户端运行的实例类型。
    /// </summary>
    public bool HasExplicitInstanceType { get; }

    /// <summary>
    /// 应用重启时被替换的旧进程 ID。
    /// </summary>
    public int? RestartFromProcessId { get; }

    /// <summary>
    /// 是否启动本机 MCP Streamable HTTP 服务。
    /// </summary>
    public bool McpEnabled { get; }

    /// <summary>
    /// MCP 服务监听端口，仅绑定到 127.0.0.1。
    /// </summary>
    public int McpPort { get; }

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
        bool hasExplicitInstanceType = false,
        int? restartFromProcessId = null,
        bool mcpEnabled = false,
        int mcpPort = DefaultMcpPort)
    {
        Action = action;
        OneDragonConfigName = oneDragonConfigName;
        GroupNames = groupNames ?? [];
        InstanceType = instanceType;
        HasExplicitInstanceType = hasExplicitInstanceType;
        RestartFromProcessId = restartFromProcessId;
        // 主实例始终提供仅回环可见的 MCP，供外部客户端和内置 Agent 共用。
        McpEnabled = mcpEnabled || instanceType == BetterGiInstanceType.Primary;
        McpPort = mcpPort;
    }

    internal static CommandLineOptions Parse(string[] args)
    {
        var launchArgs = args.Skip(1).Select(x => x.Trim()).ToArray();
        var instanceType = BetterGiInstanceType.Primary;
        var hasExplicitInstanceType = false;
        int? restartFromProcessId = null;
        var mcpEnabled = false;
        var mcpPort = DefaultMcpPort;
        var commandArgs = new List<string>();

        for (var index = 0; index < launchArgs.Length; index++)
        {
            var argument = launchArgs[index];
            if (argument.Equals(InstanceArgument, StringComparison.OrdinalIgnoreCase))
            {
                if (TryReadNext(launchArgs, ref index, out var instanceTypeValue))
                {
                    var parsedType = instanceTypeValue.ToLowerInvariant() switch
                    {
                        "childsession" => BetterGiInstanceType.ChildSession,
                        "webview" => BetterGiInstanceType.WebView,
                        _ => (BetterGiInstanceType?)null
                    };
                    if (parsedType is not null)
                    {
                        instanceType = parsedType.Value;
                        hasExplicitInstanceType = true;
                    }
                }
                continue;
            }

            if (argument.Equals(RestartFromProcessIdArgument, StringComparison.OrdinalIgnoreCase))
            {
                if (TryReadNext(launchArgs, ref index, out var processIdValue)
                    && int.TryParse(processIdValue, out var parsedProcessId)
                    && parsedProcessId > 0)
                {
                    restartFromProcessId = parsedProcessId;
                }
                continue;
            }

            if (argument.Equals(McpArgument, StringComparison.OrdinalIgnoreCase))
            {
                mcpEnabled = true;
                continue;
            }

            if (argument.Equals(McpPortArgument, StringComparison.OrdinalIgnoreCase))
            {
                if (TryReadNext(launchArgs, ref index, out var portValue)
                    && int.TryParse(portValue, out var parsedPort)
                    && parsedPort is > 0 and <= 65535)
                {
                    mcpPort = parsedPort;
                    mcpEnabled = true;
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
                hasExplicitInstanceType,
                restartFromProcessId,
                mcpEnabled,
                mcpPort);
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

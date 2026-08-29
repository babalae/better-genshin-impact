using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace BetterGenshinImpact.Service.Mcp;

[McpServerToolType]
public sealed class McpRepositoryTools(McpApplicationServices application)
{
    [McpServerTool(Name = "bgi_get_script_repository", ReadOnly = true, Idempotent = true),
     Description("获取当前脚本仓库渠道、URL、本地路径、更新时间和更新状态。")]
    public static object GetScriptRepository()
    {
        var config = TaskContext.Instance().Config.ScriptConfig;
        return new
        {
            channel = string.IsNullOrWhiteSpace(config.SelectedChannelName) ? "CNB" : config.SelectedChannelName,
            url = ScriptRepoUpdater.ResolveRepoUrl(config),
            localPath = ScriptRepoUpdater.CenterRepoPath,
            exists = Directory.Exists(ScriptRepoUpdater.CenterRepoPath),
            config.LastUpdateScriptRepoTime,
            config.ScriptRepoHintDotVisible,
            isUpdating = ScriptRepoUpdater.Instance.IsAutoUpdating,
            availableChannels = ScriptRepoUpdater.RepoChannels,
        };
    }

    [McpServerTool(Name = "bgi_set_script_repository_channel", Destructive = true),
     Description("修改脚本仓库渠道。支持 CNB、GitCode、GitHub 或“自定义”，并立即保存设置。")]
    public async Task<object> SetScriptRepositoryChannel(
        [Description("渠道名：CNB、GitCode、GitHub 或 自定义。")]
        string channel,
        [Description("channel 为“自定义”时必填的 http/https Git 仓库 URL。")]
        string? customUrl = null,
        [Description("必须明确设为 true。")] bool confirm = false,
        CancellationToken cancellationToken = default)
    {
        if (!confirm)
        {
            throw new InvalidOperationException("修改仓库渠道需要将 confirm 设为 true。");
        }

        var normalizedChannel = ScriptRepoUpdater.RepoChannels.Keys
            .FirstOrDefault(x => x.Equals(channel, StringComparison.OrdinalIgnoreCase));
        if (normalizedChannel is null && channel.Equals("自定义", StringComparison.OrdinalIgnoreCase))
        {
            normalizedChannel = "自定义";
            if (!Uri.TryCreate(customUrl, UriKind.Absolute, out var customUri)
                || customUri.Scheme is not ("http" or "https"))
            {
                throw new ArgumentException("自定义渠道必须提供有效的 http/https URL。", nameof(customUrl));
            }

            customUrl = customUri.ToString().TrimEnd('/');
        }

        if (normalizedChannel is null)
        {
            throw new ArgumentException($"未知渠道：{channel}", nameof(channel));
        }

        var configService = application.Services.GetRequiredService<IConfigService>();
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var config = configService.Get().ScriptConfig;
            config.SelectedChannelName = normalizedChannel;
            if (normalizedChannel == "自定义")
            {
                config.CustomRepoUrl = customUrl!;
            }

            configService.Save();
        }).Task;
        McpRepositoryIndex.Invalidate();
        return new
        {
            channel = normalizedChannel,
            url = ScriptRepoUpdater.ResolveRepoUrl(configService.Get().ScriptConfig),
            saved = true,
        };
    }

    [McpServerTool(Name = "bgi_update_script_repository", Destructive = true, OpenWorld = true),
     Description("从当前渠道或指定 Git URL 拉取/更新 BetterGI 脚本仓库。显式 repositoryUrl 会同时成为当前“自定义”渠道，后续搜索立即使用该仓库的新索引。")]
    public async Task<object> UpdateScriptRepository(
        [Description("可选仓库 URL；省略时使用当前设置中的脚本仓库渠道。仅允许 http/https URL。")]
        string? repositoryUrl = null,
        [Description("必须明确设为 true，因为更新会写入 Repos 目录。")]
        bool confirm = false,
        CancellationToken cancellationToken = default)
    {
        if (!confirm)
        {
            throw new InvalidOperationException("更新仓库需要将 confirm 设为 true。");
        }

        var config = TaskContext.Instance().Config.ScriptConfig;
        var url = string.IsNullOrWhiteSpace(repositoryUrl)
            ? ScriptRepoUpdater.ResolveRepoUrl(config)
            : repositoryUrl.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("仓库 URL 必须是有效的 http/https 地址。", nameof(repositoryUrl));
        }

        var configService = application.Services.GetRequiredService<IConfigService>();
        if (!string.IsNullOrWhiteSpace(repositoryUrl))
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                config.SelectedChannelName = "自定义";
                config.CustomRepoUrl = uri.ToString().TrimEnd('/');
                configService.Save();
            }).Task;
            McpRepositoryIndex.Invalidate();
        }

        var (repoPath, updated) = await ScriptRepoUpdater.Instance
            .UpdateCenterRepoByGit(uri.ToString().TrimEnd('/'), null)
            .WaitAsync(cancellationToken);
        McpRepositoryIndex.Invalidate();
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            config.LastUpdateScriptRepoTime = DateTime.Now;
            config.ScriptRepoHintDotVisible = updated;
            configService.Save();
        }).Task;
        return new { repoPath, updated, url = uri.ToString() };
    }

    [McpServerTool(Name = "bgi_list_script_subscriptions", ReadOnly = true, Idempotent = true),
     Description("列出当前脚本仓库的订阅路径。")]
    public static object ListScriptSubscriptions() => new
    {
        repository = ScriptRepoUpdater.CenterRepoPath,
        subscriptions = ScriptRepoUpdater.GetSubscribedPathsForCurrentRepo(),
    };

    [McpServerTool(Name = "bgi_set_script_subscriptions", Destructive = true),
     Description("完整替换当前仓库的订阅路径清单；不会安装或删除用户脚本文件。")]
    public static async Task<object> SetScriptSubscriptions(
        [Description("订阅路径列表；每项必须以 pathing、js、combat 或 tcg 开头。")]
        string[] paths,
        [Description("必须明确设为 true。")] bool confirm,
        CancellationToken cancellationToken = default)
    {
        if (!confirm)
        {
            throw new InvalidOperationException("替换订阅清单需要将 confirm 设为 true。");
        }

        var result = await ScriptRepoUpdater.Instance
            .SetSubscribedPathsForCurrentRepoAsync(paths)
            .WaitAsync(cancellationToken);
        return new { subscriptions = result, installedFilesChanged = false };
    }

    [McpServerTool(Name = "bgi_subscribe_scripts", Destructive = true, OpenWorld = true),
     Description("新增脚本订阅，并可立即从本地脚本仓库安装/覆盖对应脚本。")]
    public static async Task<object> SubscribeScripts(
        [Description("新增订阅路径；每项必须以 pathing、js、combat 或 tcg 开头。")]
        string[] paths,
        [Description("true 时立即安装并覆盖相应用户脚本；false 时只修改订阅清单。")]
        bool install = true,
        [Description("必须明确设为 true。")] bool confirm = false,
        CancellationToken cancellationToken = default)
    {
        if (!confirm)
        {
            throw new InvalidOperationException("新增订阅需要将 confirm 设为 true。");
        }

        if (install)
        {
            var pathJson = JsonSerializer.Serialize(paths, ConfigService.JsonOptions);
            await Application.Current.Dispatcher.InvokeAsync(() =>
                    ScriptRepoUpdater.Instance.ImportScriptFromPathJson(pathJson)).Task.Unwrap()
                .WaitAsync(cancellationToken);
        }
        else
        {
            var merged = ScriptRepoUpdater.GetSubscribedPathsForCurrentRepo().Concat(paths);
            await ScriptRepoUpdater.Instance.SetSubscribedPathsForCurrentRepoAsync(merged).WaitAsync(cancellationToken);
        }

        return new
        {
            subscriptions = ScriptRepoUpdater.GetSubscribedPathsForCurrentRepo(),
            installedFilesChanged = install,
        };
    }

    [McpServerTool(Name = "bgi_unsubscribe_scripts", Destructive = true), Description("移除脚本订阅。不会删除已经安装到 User 目录的脚本。")]
    public static async Task<object> UnsubscribeScripts(
        [Description("要移除的订阅路径。")] string[] paths,
        [Description("必须明确设为 true。")] bool confirm,
        CancellationToken cancellationToken = default)
    {
        if (!confirm)
        {
            throw new InvalidOperationException("取消订阅需要将 confirm 设为 true。");
        }

        var result = await ScriptRepoUpdater.Instance
            .RemoveSubscribedPathsForCurrentRepoAsync(paths)
            .WaitAsync(cancellationToken);
        return new { subscriptions = result, installedFilesChanged = false };
    }

    [McpServerTool(Name = "bgi_update_subscribed_scripts", Destructive = true, OpenWorld = true),
     Description("更新脚本仓库并重新安装全部已订阅脚本，可能覆盖 User 目录中的对应脚本。")]
    public static async Task<object> UpdateSubscribedScripts(
        [Description("必须明确设为 true。")] bool confirm,
        CancellationToken cancellationToken = default)
    {
        if (!confirm)
        {
            throw new InvalidOperationException("更新订阅脚本需要将 confirm 设为 true。");
        }

        var before = ScriptRepoUpdater.GetSubscribedPathsForCurrentRepo();
        await ScriptRepoUpdater.Instance.ManualUpdateSubscribedScripts().WaitAsync(cancellationToken);
        McpRepositoryIndex.Invalidate();
        return new
        {
            requested = true,
            subscriptions = before,
            repository = ScriptRepoUpdater.CenterRepoPath,
        };
    }
}
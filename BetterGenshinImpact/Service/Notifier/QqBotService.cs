using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service.Notifier;

/// <summary>
/// 内置 QQ Bot 控制端。每个用户在 BGI 中配置自己的 AppID/AppSecret，
/// BGI 直接连接 QQ Gateway，不需要额外的本地或云端代理。
/// </summary>
public sealed class QqBotService : IHostedService, IDisposable
{
    private const string TokenUrl = "https://bots.qq.com/app/getAppAccessToken";
    private const string GatewayUrl = "https://api.sgroup.qq.com/gateway";
    private const string ApiBaseUrl = "https://api.sgroup.qq.com";
    private const string BgiPanelRemark = "BetterGI QQ 控制面板";
    private const string BgiGroupPanelRemark = "BetterGI QQ 群控制面板";
    private const string C2cMessageEvent = "C2C_MESSAGE_CREATE";
    private const string GroupMessageEvent = "GROUP_AT_MESSAGE_CREATE";
    private const string GroupMessageEventFallback = "GROUP_MESSAGE_CREATE";
    private const int GroupAndC2cIntent = 1 << 25;

    private static readonly ILogger Logger = App.GetLogger<QqBotService>();
    private static readonly IReadOnlyDictionary<string, (string Property, string Label)> TaskCommands =
        new Dictionary<string, (string Property, string Label)>(StringComparer.OrdinalIgnoreCase)
        {
            ["一条龙"] = ("SOneDragonFlowCommand", "一条龙"),
            ["自动七圣召唤"] = ("SwitchAutoGeniusInvokationCommand", "自动七圣召唤"),
            ["自动伐木"] = ("SwitchAutoWoodCommand", "自动伐木"),
            ["自动战斗"] = ("SwitchAutoFightCommand", "自动战斗"),
            ["自动秘境"] = ("SwitchAutoDomainCommand", "自动秘境"),
            ["自动首领"] = ("SwitchAutoBossCommand", "自动首领"),
            ["自动征讨领域"] = ("SwitchAutoStygianOnslaughtCommand", "自动征讨领域"),
            ["自动音游"] = ("SwitchAutoMusicGameCommand", "自动音游"),
            ["自动剧本"] = ("SwitchAutoAlbumCommand", "自动剧本"),
            ["自动烹饪"] = ("SwitchAutoCookCommand", "自动烹饪"),
            ["自动连招构建"] = ("SwitchAutoComboCommand", "自动连招构建"),
            ["自动连招测试"] = ("SwitchAutoComboRunCommand", "自动连招测试"),
            ["自动钓鱼"] = ("SwitchAutoFishingCommand", "自动钓鱼"),
            ["自动地脉"] = ("SwitchAutoLeyLineOutcropCommand", "自动地脉"),
            ["圣遗物分解"] = ("SwitchArtifactSalvageCommand", "圣遗物分解"),
            ["获取格子图标"] = ("SwitchGetGridIconsCommand", "获取格子图标"),
            ["数量识别对比"] = ("RunInventoryCountComparisonCommand", "数量识别对比"),
            ["自动兑换码"] = ("SwitchAutoRedeemCodeCommand", "自动兑换码")
        };

    // 和实时触发设置页使用同一份配置，属性变更会刷新触发器并保存配置。
    private static readonly IReadOnlyDictionary<string, (Func<AllConfig, bool> Get, Action<AllConfig, bool> Set)> TriggerCommands =
        new Dictionary<string, (Func<AllConfig, bool> Get, Action<AllConfig, bool> Set)>(StringComparer.Ordinal)
        {
            ["自动拾取"] = (c => c.AutoPickConfig.Enabled, (c, enabled) => c.AutoPickConfig.Enabled = enabled),
            ["自动剧情"] = (c => c.AutoSkipConfig.Enabled, (c, enabled) => c.AutoSkipConfig.Enabled = enabled),
            ["半自动钓鱼"] = (c => c.AutoFishingConfig.Enabled, (c, enabled) => c.AutoFishingConfig.Enabled = enabled),
            ["自动吃药"] = (c => c.AutoEatConfig.Enabled, (c, enabled) => c.AutoEatConfig.Enabled = enabled),
            ["快速传送"] = (c => c.QuickTeleportConfig.Enabled, (c, enabled) => c.QuickTeleportConfig.Enabled = enabled),
            ["地图遮罩"] = (c => c.MapMaskConfig.Enabled, (c, enabled) => c.MapMaskConfig.Enabled = enabled),
            ["冷却提示"] = (c => c.SkillCdConfig.Enabled, (c, enabled) => c.SkillCdConfig.Enabled = enabled)
        };

    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private readonly SemaphoreSlim _menuSyncLock = new(1, 1);
    private readonly object _lifecycleLock = new();
    private readonly object _activeTaskLock = new();

    private CancellationTokenSource? _serviceCts;
    private Task? _runTask;
    private NotificationConfig? _config;
    private string? _accessToken;
    private DateTime _accessTokenExpiresAt = DateTime.MinValue;
    private DateTime _lastMenuSyncAt = DateTime.MinValue;
    private DateTime _lastCustomMenuAttemptAt = DateTime.MinValue;
    private string? _lastCustomMenuJson;
    // 由 _menuSyncLock 保护，重连时继续保留已选择的一条龙菜单页。
    private string[] _oneDragonMenuNames = [];
    private int _oneDragonMenuPage;
    private string[] _schedulerMenuNames = [];
    private int _schedulerMenuPage;
    private string? _activeTaskKind;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _config = TaskContext.Instance().Config.NotificationConfig;
        _config.PropertyChanged += OnConfigChanged;
        RestartIfEnabled();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        StopConnection();
        var task = _runTask;
        if (task != null)
            await task.WaitAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (_config != null)
            _config.PropertyChanged -= OnConfigChanged;
        StopConnection();
        _httpClient.Dispose();
        _sendLock.Dispose();
        _runLock.Dispose();
        _tokenLock.Dispose();
        _menuSyncLock.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnConfigChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NotificationConfig.QqBotControlEnabled)
            or nameof(NotificationConfig.QqAppId)
            or nameof(NotificationConfig.QqClientSecret)
            or nameof(NotificationConfig.QqOpenId)
            or nameof(NotificationConfig.QqGroupOpenId))
        {
            _accessToken = null;
            _accessTokenExpiresAt = DateTime.MinValue;
            if (e.PropertyName == nameof(NotificationConfig.QqAppId))
                _lastCustomMenuJson = null;
            RestartIfEnabled();
        }
    }

    private void RestartIfEnabled()
    {
        StopConnection();
        _lastMenuSyncAt = DateTime.MinValue;
        if (_config?.QqBotControlEnabled != true)
            return;

        if (string.IsNullOrWhiteSpace(_config.QqAppId) || string.IsNullOrWhiteSpace(_config.QqClientSecret))
        {
            Logger.LogWarning("QQ Bot 控制已启用，但 AppID 或 AppSecret 为空");
            return;
        }

        lock (_lifecycleLock)
        {
            _serviceCts = new CancellationTokenSource();
            _runTask = Task.Run(() => RunAsync(_serviceCts.Token));
        }
    }

    private void StopConnection()
    {
        lock (_lifecycleLock)
        {
            _serviceCts?.Cancel();
            _serviceCts?.Dispose();
            _serviceCts = null;
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunConnectionAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (System.Exception ex)
            {
                Logger.LogWarning(ex, "QQ Bot 连接断开，将在 5 秒后重连");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task RunConnectionAsync(CancellationToken ct)
    {
        var config = _config ?? throw new InvalidOperationException("QQ Bot 配置未初始化");
        var accessToken = await GetAccessTokenAsync(config.QqAppId, config.QqClientSecret, ct);
        var gateway = await GetGatewayUrlAsync(accessToken, ct);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(gateway), ct);
        await SyncRemoteMenusIfNeededAsync(accessToken, ct);
        using var hello = await ReceiveJsonAsync(socket, ct);
        var heartbeatInterval = hello.RootElement.GetProperty("d").GetProperty("heartbeat_interval").GetInt32();

        await SendJsonAsync(socket, new
        {
            op = 2,
            d = new
            {
                token = $"QQBot {accessToken}",
                intents = GroupAndC2cIntent,
                shard = new[] { 0, 1 }
            }
        }, ct);

        long sequence = 0;
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeat = RunHeartbeatAsync(socket, heartbeatInterval, () => Interlocked.Read(ref sequence), heartbeatCts.Token);

        try
        {
            Logger.LogInformation("QQ Bot 控制连接已建立");
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                using var message = await ReceiveJsonAsync(socket, ct);
                var root = message.RootElement;
                if (root.TryGetProperty("s", out var sequenceElement) && sequenceElement.TryGetInt64(out var currentSequence))
                    Interlocked.Exchange(ref sequence, currentSequence);

                var op = root.TryGetProperty("op", out var opElement) ? opElement.GetInt32() : -1;
                if (op == 9)
                    throw new InvalidOperationException("QQ Bot 鉴权失败，请检查 AppID、AppSecret 和事件权限");
                if (op != 0 || !root.TryGetProperty("t", out var typeElement) || !root.TryGetProperty("d", out var data))
                    continue;

                var eventType = typeElement.GetString();
                if (eventType != C2cMessageEvent && eventType != GroupMessageEvent && eventType != GroupMessageEventFallback)
                    continue;

                await HandleMessageAsync(eventType!, data, ct);
            }
        }
        finally
        {
            heartbeatCts.Cancel();
            try { await heartbeat; } catch (OperationCanceledException) { }
        }
    }

    private async Task HandleMessageAsync(string eventType, JsonElement data, CancellationToken ct)
    {
        var config = _config;
        if (config == null)
            return;

        var isC2c = eventType == C2cMessageEvent;
        var actorOpenId = isC2c ? GetString(data, "author", "user_openid") : null;
        var groupOpenId = isC2c ? null : GetString(data, "group_openid");
        var content = GetString(data, "content")?.Trim();
        if (string.IsNullOrWhiteSpace(content))
            return;

        if (isC2c)
        {
            if (string.IsNullOrWhiteSpace(config.QqOpenId) || !string.Equals(actorOpenId, config.QqOpenId, StringComparison.Ordinal))
                return;
        }
        else if (string.IsNullOrWhiteSpace(config.QqGroupOpenId)
                 || !string.Equals(groupOpenId, config.QqGroupOpenId, StringComparison.Ordinal))
        {
            return;
        }

        var command = NormalizeCommand(content);
        if (command == null)
            return;

        var eventId = GetString(data, "event_id") ?? GetString(data, "id");
        var messageId = GetString(data, "id");
        var response = await ExecuteCommandAsync(command, ct);
        object? keyboard = null;
        if (isC2c)
        {
            try
            {
                keyboard = await BuildReplyKeyboardAsync(command, ct);
            }
            catch (System.Exception ex)
            {
                Logger.LogDebug(ex, "QQ Bot 列表回复快捷按钮生成失败，将仅发送文本");
            }
        }
        await SendReplyAsync(isC2c, actorOpenId, groupOpenId, eventId, messageId, response, ct, keyboard);
    }

    /// <summary>
    /// 使用当前 Bot 的凭据自动安装 BGI 的 QQ 指令面板和单聊快捷菜单。
    /// 这两个配置属于 Bot 本身，因此用户不需要手动打开 QQ 开放平台逐项填写。
    /// </summary>
    private async Task SyncRemoteMenusIfNeededAsync(string accessToken, CancellationToken ct)
    {
        if (DateTime.UtcNow - _lastMenuSyncAt < TimeSpan.FromMinutes(10))
            return;

        await _menuSyncLock.WaitAsync(ct);
        try
        {
            if (DateTime.UtcNow - _lastMenuSyncAt < TimeSpan.FromMinutes(10))
                return;

            try
            {
                string[] oneDragonNames = [];
                string[] schedulerNames = [];
                await RunOnUiAsync(() =>
                {
                    var oneDragon = App.GetService<OneDragonFlowViewModel>()
                        ?? throw new InvalidOperationException("无法获取一条龙服务");
                    var scheduler = App.GetService<ScriptControlViewModel>()
                        ?? throw new InvalidOperationException("无法获取配置组服务");
                    oneDragon.OnNavigatedTo();
                    scheduler.OnNavigatedTo();
                    oneDragonNames = oneDragon.ConfigList.Select(x => x.Name)
                        .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    schedulerNames = scheduler.ScriptGroups.Select(x => x.Name)
                        .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    return Task.CompletedTask;
                });
                await UpdateCustomMenuAsync(accessToken, ct, oneDragonNames, _oneDragonMenuPage,
                    schedulerNames, _schedulerMenuPage);
                await UpdateCommandPanelAsync(accessToken, ct);
                _lastMenuSyncAt = DateTime.UtcNow;
                Logger.LogInformation("QQ Bot BGI 快捷菜单和指令面板已同步");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (System.Exception ex)
            {
                // 菜单同步失败不应影响消息收发，下一次重连或配置变更时会再次尝试。
                Logger.LogWarning(ex, "QQ Bot 菜单同步失败，消息控制仍可正常使用");
            }
        }
        finally
        {
            _menuSyncLock.Release();
        }
    }

    private async Task UpdateCustomMenuAsync(string accessToken, CancellationToken ct,
        string[]? oneDragonNames = null, int page = 0, string[]? schedulerNames = null, int schedulerPage = 0)
    {
        var names = oneDragonNames ?? _oneDragonMenuNames;
        var selectedPage = oneDragonNames == null ? _oneDragonMenuPage : page;
        var schedulers = schedulerNames ?? _schedulerMenuNames;
        var selectedSchedulerPage = schedulerNames == null ? _schedulerMenuPage : schedulerPage;
        var oneDragonItems = BuildPagedMenuItems(names, selectedPage, "执行一条龙", "一条龙菜单", "一条龙列表");
        var schedulerItems = BuildPagedMenuItems(schedulers, selectedSchedulerPage, "配置组", "调度器菜单", "配置组 列表");
        var items = new List<object>
        {
            new { type = "menu", name = "一条龙", sub_menu_items = oneDragonItems },
            new { type = "menu", name = "调度器", sub_menu_items = schedulerItems },
            new
            {
                type = "menu",
                name = "独立任务",
                sub_menu_items = new object[]
                {
                    new { type = "send_message", name = "自动战斗", send_message = "自动战斗" },
                    new { type = "send_message", name = "自动秘境", send_message = "自动秘境" },
                    new { type = "send_message", name = "自动首领", send_message = "自动首领" },
                    new { type = "send_message", name = "自动伐木", send_message = "自动伐木" },
                    new { type = "send_message", name = "更多任务", send_message = "功能" }
                }
            },
            new
            {
                type = "menu",
                name = "实时触发",
                sub_menu_items = new object[]
                {
                    new { type = "send_message", name = "开启拾取", send_message = "开启自动拾取" },
                    new { type = "send_message", name = "关闭拾取", send_message = "关闭自动拾取" },
                    new { type = "send_message", name = "开启剧情", send_message = "开启自动剧情" },
                    new { type = "send_message", name = "关闭剧情", send_message = "关闭自动剧情" },
                    new { type = "send_message", name = "更多开关", send_message = "实时触发列表" }
                }
            },
            new
            {
                type = "menu",
                name = "更多",
                sub_menu_items = new object[]
                {
                    new { type = "send_message", name = "帮助", send_message = "帮助" },
                    new { type = "send_message", name = "运行状态", send_message = "状态" },
                    new { type = "send_message", name = "停止任务", send_message = "停止" },
                    new { type = "send_message", name = "开启拾取", send_message = "开启自动拾取" },
                    new { type = "send_message", name = "关闭拾取", send_message = "关闭自动拾取" }
                }
            }
        };
        var payload = new
        {
            menu = new { items }
        };
        var serialized = JsonSerializer.Serialize(payload);
        if (!string.Equals(_lastCustomMenuJson, serialized, StringComparison.Ordinal))
        {
            // QQ 限制菜单修改为每分钟 5 次；同一份菜单不重复提交。
            var delay = _lastCustomMenuAttemptAt.AddSeconds(13) - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, ct);
            _lastCustomMenuAttemptAt = DateTime.UtcNow;
            var result = await SendApiAsync(HttpMethod.Put, "/v2/menu", accessToken, payload, ct);
            using var json = JsonDocument.Parse(result);
            if (!json.RootElement.TryGetProperty("version", out var version) || !version.TryGetInt64(out _))
                throw new InvalidOperationException("QQ 未返回菜单版本号，无法确认菜单更新成功");
            _lastCustomMenuJson = serialized;
        }
        _oneDragonMenuNames = names;
        _oneDragonMenuPage = selectedPage;
        _schedulerMenuNames = schedulers;
        _schedulerMenuPage = selectedSchedulerPage;
    }

    private static List<object> BuildPagedMenuItems(string[] names, int page, string commandPrefix,
        string pageCommand, string emptyCommand)
    {
        var pageSize = GetMenuPageSize(names.Length);
        var pageCount = (names.Length + pageSize - 1) / pageSize;
        page = names.Length == 0 ? 0 : Math.Clamp(page, 0, pageCount - 1);
        var commands = new List<object>();
        commands.Add(new { type = "send_message", name = "刷新列表", send_message = emptyCommand });
        for (var i = page * pageSize; i < Math.Min(names.Length, (page + 1) * pageSize); i++)
        {
            commands.Add(new
            {
                type = "send_message",
                name = ShortenMenuName($"{i + 1}.{names[i]}", 14),
                send_message = $"{commandPrefix} {names[i]}"
            });
        }
        if (pageCount > 1)
        {
            commands.Add(new
            {
                type = "send_message",
                name = page == pageCount - 1 ? "返回首页" : "下一页",
                send_message = $"{pageCommand} {(page + 1) % pageCount + 1}"
            });
        }
        return commands;
    }

    private static int GetMenuPageSize(int count) => count > 4 ? 3 : 4;

    private static string ShortenMenuName(string name, int maxWidth)
    {
        var label = new StringBuilder();
        var width = 0;
        foreach (var rune in name.EnumerateRunes())
        {
            var runeWidth = rune.IsAscii ? 1 : 2;
            if (width + runeWidth > maxWidth)
                break;
            label.Append(rune.ToString());
            width += runeWidth;
        }
        return label.ToString();
    }

    private async Task UpdateCommandPanelAsync(string accessToken, CancellationToken ct)
    {
        var panel = new
        {
            items = new object[]
            {
                new { type = "command", name = "帮助", desc = "查看全部 BGI 指令", only_admin = false },
                new { type = "command", name = "一条龙列表", desc = "查看一条龙配置", only_admin = false },
                new { type = "command", name = "配置组列表", desc = "查看调度器配置", only_admin = false },
                new { type = "command", name = "自动战斗", desc = "执行自动战斗", only_admin = false },
                new { type = "command", name = "自动秘境", desc = "执行自动秘境", only_admin = false },
                new { type = "command", name = "自动首领", desc = "执行自动首领", only_admin = false },
                new { type = "command", name = "自动伐木", desc = "执行自动伐木", only_admin = false },
                new { type = "command", name = "自动烹饪", desc = "执行自动烹饪", only_admin = false },
                new { type = "command", name = "实时触发列表", desc = "查看实时开关状态", only_admin = false },
                new { type = "command", name = "开启自动拾取", desc = "开启自动拾取", only_admin = false },
                new { type = "command", name = "关闭自动拾取", desc = "关闭自动拾取", only_admin = false },
                new { type = "command", name = "开启自动剧情", desc = "开启自动剧情", only_admin = false },
                new { type = "command", name = "关闭自动剧情", desc = "关闭自动剧情", only_admin = false },
                new { type = "command", name = "开启自动吃药", desc = "开启自动吃药", only_admin = false },
                new { type = "command", name = "关闭自动吃药", desc = "关闭自动吃药", only_admin = false },
                new { type = "command", name = "开启快速传送", desc = "开启快速传送", only_admin = false },
                new { type = "command", name = "关闭快速传送", desc = "关闭快速传送", only_admin = false },
                new { type = "command", name = "状态", desc = "查看当前运行状态", only_admin = false },
                new { type = "command", name = "停止", desc = "停止当前任务", only_admin = false },
                new { type = "command", name = "功能", desc = "查看独立任务列表", only_admin = false }
            },
            remark = BgiPanelRemark
        };

        await SyncCommandPanelAsync(accessToken, "c2c", "all", null, BgiPanelRemark, panel, ct);

        // 群面板只能绑定到已获取到的群 OpenID，避免把入口展示给无法控制 BGI 的群。
        var groupOpenId = _config?.QqGroupOpenId;
        if (!string.IsNullOrWhiteSpace(groupOpenId))
        {
            var groupPanel = new
            {
                items = panel.items,
                remark = BgiGroupPanelRemark
            };
            await SyncCommandPanelAsync(accessToken, "group", "specific", groupOpenId,
                BgiGroupPanelRemark, groupPanel, ct);
        }
    }

    private async Task SyncCommandPanelAsync(string accessToken, string scope, string targetType,
        string? groupOpenId, string panelRemark, object panel, CancellationToken ct)
    {

        string? panelId = null;
        var listJson = await SendApiAsync(HttpMethod.Get, $"/v2/panels?scope={scope}&limit=50", accessToken, null, ct);
        using (var list = JsonDocument.Parse(listJson))
        {
            if (list.RootElement.TryGetProperty("records", out var records)
                && records.ValueKind == JsonValueKind.Array)
            {
                foreach (var record in records.EnumerateArray())
                {
                    var recordRemark = GetString(record, "remark") ?? GetString(record, "panel", "remark");
                    if (string.Equals(recordRemark, panelRemark, StringComparison.Ordinal))
                    {
                        panelId = GetString(record, "panel_id") ?? GetString(record, "id");
                        break;
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(panelId))
        {
            object createPayload = scope == "group"
                ? new
                {
                    scope,
                    target_type = targetType,
                    group_openids = new[] { groupOpenId },
                    panel
                }
                : new
                {
                    scope,
                    target_type = targetType,
                    panel
                };
            await SendApiAsync(HttpMethod.Post, "/v2/panels", accessToken, createPayload, ct);
        }
        else
        {
            await SendApiAsync(HttpMethod.Put, $"/v2/panels/{Uri.EscapeDataString(panelId)}", accessToken,
                new { panel }, ct);
        }
    }

    private async Task<string> SendApiAsync(HttpMethod method, string path, string accessToken, object? payload,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, ApiBaseUrl + path);
        request.Headers.Add("Authorization", $"QQBot {accessToken}");
        if (payload != null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"QQ API {method} {path} 失败：{(int)response.StatusCode} {responseText}");
        return responseText;
    }


    private static string? NormalizeCommand(string content)
    {
        var direct = Regex.Replace(content.Trim(), @"^<@!?[^>]+>\s*", string.Empty);
        return string.IsNullOrWhiteSpace(direct) ? null : $"命令 {direct}";
    }

    private async Task<string> ExecuteCommandAsync(string command, CancellationToken ct)
    {
        var parts = SplitCommand(command);
        if (parts.Count == 0 || parts.Count == 1 || parts[1] is "帮助" or "指令列表")
            return GetHelpText();

        var action = parts[1].ToLowerInvariant();
        if (action.StartsWith("开启", StringComparison.Ordinal) || action.StartsWith("关闭", StringComparison.Ordinal))
        {
            var triggerName = action.Length > 2 ? action[2..] : string.Join(' ', parts.Skip(2));
            return await SetTriggerEnabledAsync(triggerName, action.StartsWith("开启", StringComparison.Ordinal));
        }
        if (action.StartsWith("执行", StringComparison.Ordinal) && action.Length > 2
            && action[2..] != "一条龙" && TaskCommands.ContainsKey(action[2..]))
            return await ExecuteTaskCommandAsync([parts[0], "任务", action[2..]], ct);
        if (action == "地图追踪列表")
            return await ExecutePathCommandAsync([parts[0], "地图追踪", "列表"], ct);
        if (action == "执行地图追踪")
            return await ExecutePathCommandAsync([parts[0], "地图追踪", .. parts.Skip(2)], ct);
        if (action == "停止地图追踪")
            return StopTask("地图追踪");
        if (action == "一条龙列表")
            return await ExecuteOneDragonCommandAsync([parts[0], "一条龙", "列表"], ct);
        if (action == "一条龙菜单")
            return await ExecuteOneDragonCommandAsync([parts[0], "一条龙", "菜单", .. parts.Skip(2)], ct);
        if (action == "执行一条龙")
            return await ExecuteOneDragonCommandAsync([parts[0], "一条龙", .. parts.Skip(2)], ct);
        if (action == "停止一条龙")
            return StopTask("一条龙");
        if (action == "一条龙" && parts[0] is not "停止" and not "执行")
            return await ExecuteOneDragonCommandAsync(parts, ct);
        if (TaskCommands.ContainsKey(action))
            return await ExecuteTaskCommandAsync([parts[0], "任务", action], ct);

        switch (action)
        {
            case "状态":
                return TaskControl.TaskSemaphore.CurrentCount == 0 ? "当前正在执行任务" : "当前空闲";
            case "功能":
                return GetFeatureText();
            case "实时触发列表":
                return await GetTriggerStatusAsync();
            case "停止":
                if (parts.Count >= 3 && parts[2] == "地图追踪")
                    return StopTask("地图追踪");
                if (parts.Count >= 3 && parts[2] == "一条龙")
                    return StopTask("一条龙");
                CancellationContext.Instance.Cancel();
                return "已发送停止当前任务的指令";
            case "启动":
            case "执行":
                if (parts.Count >= 3 && parts[2] == "地图追踪")
                    return await ExecutePathCommandAsync([parts[0], "地图追踪", .. parts.Skip(3)], ct);
                if (parts.Count >= 3 && parts[2] == "一条龙")
                    return await ExecuteOneDragonCommandAsync([parts[0], "一条龙", .. parts.Skip(3)], ct);
                if (parts.Count == 3 && TaskCommands.ContainsKey(parts[2]))
                    return await ExecuteTaskCommandAsync([parts[0], "任务", parts[2]], ct);
                return "请发送 执行自动战斗 等独立任务指令，或 执行一条龙 配置名称；发送 帮助 查看全部指令";
            case "配置组":
                return await ExecuteScriptGroupCommandAsync(parts, ct);
            case "配置组列表":
            case "调度器":
                return await ExecuteScriptGroupCommandAsync([parts[0], "配置组", "列表"], ct);
            case "调度器菜单":
                return await ExecuteScriptGroupCommandAsync([parts[0], "配置组", "菜单", .. parts.Skip(2)], ct);
            case "脚本":
                return await ExecuteJsCommandAsync(parts, ct);
            case "地图追踪":
                return await ExecutePathCommandAsync(parts, ct);
            case "任务":
                return await ExecuteTaskCommandAsync(parts, ct);
            default:
                return "未知指令，请发送 帮助 或 功能";
        }
    }

    private static string GetHelpText() =>
        "帮助 / 指令列表：列出全部指令\n" +
        "状态：查看当前任务状态\n" +
        "功能：列出独立任务\n" +
        "停止：停止当前任务\n" +
        "地图追踪列表：列出地图追踪\n" +
        "执行地图追踪 名称：执行地图追踪\n" +
        "停止地图追踪：停止地图追踪\n" +
        "一条龙列表：列出一条龙配置\n" +
        "一条龙菜单 页码：切换私聊快捷菜单中的一条龙配置页\n" +
        "执行一条龙 名称：执行指定一条龙配置\n" +
        "停止一条龙：停止一条龙\n" +
        "调度器 / 配置组列表：列出调度器配置\n" +
        "配置组 列表：列出配置组\n" +
        "配置组 名称：执行配置组\n" +
        "脚本 列表：列出脚本\n" +
        "脚本 名称：执行脚本\n" +
        "独立任务（任选一种写法）：\n" +
        string.Join("\n", TaskCommands.Keys.Where(name => name != "一条龙").Select(name => $"{name} / 执行{name}")) +
        "\n实时触发列表：查看各功能开关状态\n" +
        string.Join("\n", TriggerCommands.Keys.Select(name => $"开启{name} / 关闭{name}"));

    private static string GetFeatureText() =>
        "独立任务：" + string.Join("、", TaskCommands.Keys.Where(name => name != "一条龙")) + "\n" +
        "使用：直接发送功能名，或发送 执行功能名，例如 自动战斗 / 执行自动战斗\n" +
        "实时触发：" + string.Join("、", TriggerCommands.Keys) + "\n" +
        "使用：开启功能名 / 关闭功能名，例如 开启自动拾取 / 关闭自动拾取\n" +
        "可调用资源：配置组、脚本、地图追踪\n" +
        "示例：自动秘境；配置组 日常；脚本 自动采集";

    private async Task<string> SetTriggerEnabledAsync(string name, bool enabled)
    {
        if (!TriggerCommands.TryGetValue(name, out var command))
            return $"未知实时触发功能“{name}”，请发送 帮助 查看支持的开关指令";

        var response = string.Empty;
        await RunOnUiAsync(() =>
        {
            var context = TaskContext.Instance();
            var alreadySet = command.Get(context.Config) == enabled;
            command.Set(context.Config, enabled);
            var state = enabled ? "开启" : "关闭";
            response = alreadySet ? $"{name}已处于{state}状态" : $"已{state}{name}";
            if (enabled && !context.IsInitialized)
                response += "；请先在 BGI 首页启动实时触发后生效";
            return Task.CompletedTask;
        });
        return response;
    }

    private async Task<string> GetTriggerStatusAsync()
    {
        var response = string.Empty;
        await RunOnUiAsync(() =>
        {
            var context = TaskContext.Instance();
            response = string.Join("\n", TriggerCommands.Select(entry =>
                $"{entry.Key}：{(entry.Value.Get(context.Config) ? "开启" : "关闭")}"));
            if (!context.IsInitialized)
                response += "\n实时触发尚未启动，请在 BGI 首页启动";
            return Task.CompletedTask;
        });
        return response;
    }

    private static List<string> SplitCommand(string command)
    {
        return command.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private async Task<string> ExecuteOneDragonCommandAsync(IReadOnlyList<string> parts, CancellationToken ct)
    {
        OneDragonFlowViewModel? viewModel = null;
        string[] names = [];
        await RunOnUiAsync(() =>
        {
            viewModel = App.GetService<OneDragonFlowViewModel>();
            if (viewModel == null)
                throw new InvalidOperationException("无法获取一条龙服务");
            viewModel.OnNavigatedTo();
            names = viewModel.ConfigList.Select(x => x.Name)
                .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return Task.CompletedTask;
        });

        if (parts.Count < 3 || parts[2] is "列表" or "菜单")
        {
            var pageSize = GetMenuPageSize(names.Length);
            var pageCount = Math.Max(1, (names.Length + pageSize - 1) / pageSize);
            var page = 1;
            var paging = parts.Count >= 3 && parts[2] == "菜单";
            if (paging && (parts.Count != 4 || !int.TryParse(parts[3], out page) || page < 1 || page > pageCount))
                return $"请发送 一条龙菜单 页码，页码范围为 1 至 {pageCount}";

            var listText = names.Length == 0 ? "当前没有一条龙配置" :
                $"共 {names.Length} 项：\n" + string.Join("\n", names);
            await _menuSyncLock.WaitAsync(ct);
            try
            {
                var botConfig = _config ?? throw new InvalidOperationException("QQ Bot 配置未初始化");
                var accessToken = await GetAccessTokenAsync(botConfig.QqAppId, botConfig.QqClientSecret, ct);
                await UpdateCustomMenuAsync(accessToken, ct, names, page - 1);
                Logger.LogInformation("QQ Bot 一条龙快捷菜单已更新，共 {Count} 项，第 {Page}/{Pages} 页",
                    names.Length, page, pageCount);
                if (names.Length == 0)
                    return listText + "\n“一条龙”菜单保留刷新列表入口";
                var status = $"已提交“一条龙”菜单（第 {page}/{pageCount} 页）；QQ 底部菜单可能需要重新进入聊天才显示。可直接使用本次回复的配置按钮，或发送 执行一条龙 配置名称";
                var visibleNames = names.Skip((page - 1) * pageSize).Take(pageSize);
                return paging ? status + "\n" + string.Join("\n", visibleNames) : listText + "\n" + status;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (System.Exception ex)
            {
                Logger.LogWarning(ex, "QQ Bot 一条龙快捷菜单更新失败");
                return listText + "\n快捷菜单更新失败，请稍后重新发送 一条龙列表；仍可发送 执行一条龙 配置名称";
            }
            finally
            {
                _menuSyncLock.Release();
            }
        }

        var name = string.Join(' ', parts.Skip(2));
        var config = viewModel.ConfigList.FirstOrDefault(x =>
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (config == null)
            return $"找不到一条龙配置“{name}”，请先发送 一条龙列表";

        return await StartTaskAsync(
            () => RunOneDragonAsync(config.Name),
            $"一条龙 {config.Name}", "一条龙已经在执行中", ct, "一条龙");
    }

    private async Task<string> ExecuteScriptGroupCommandAsync(IReadOnlyList<string> parts, CancellationToken ct)
    {
        ScriptControlViewModel? viewModel = null;
        await RunOnUiAsync(() =>
        {
            viewModel = App.GetService<ScriptControlViewModel>();
            if (viewModel == null)
                throw new InvalidOperationException("无法获取配置组服务");
            viewModel.OnNavigatedTo();
            return Task.CompletedTask;
        });

        if (parts.Count < 3 || parts[2] is "列表" or "菜单")
        {
            var names = viewModel.ScriptGroups.Select(x => x.Name)
                .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var pageSize = GetMenuPageSize(names.Length);
            var pageCount = Math.Max(1, (names.Length + pageSize - 1) / pageSize);
            var page = 1;
            var paging = parts.Count >= 3 && parts[2] == "菜单";
            if (paging && (parts.Count != 4 || !int.TryParse(parts[3], out page) || page < 1 || page > pageCount))
                return $"请发送 调度器菜单 页码，页码范围为 1 至 {pageCount}";

            var listText = names.Length == 0 ? "当前没有配置组" :
                $"共 {names.Length} 项：\n" + string.Join("\n", names);
            await _menuSyncLock.WaitAsync(ct);
            try
            {
                var botConfig = _config ?? throw new InvalidOperationException("QQ Bot 配置未初始化");
                var accessToken = await GetAccessTokenAsync(botConfig.QqAppId, botConfig.QqClientSecret, ct);
                await UpdateCustomMenuAsync(accessToken, ct, schedulerNames: names, schedulerPage: page - 1);
                var status = $"已提交“调度器”菜单（第 {page}/{pageCount} 页）；QQ 底部菜单可能需要重新进入聊天才显示。可直接使用本次回复的配置按钮，或发送 配置组 配置名称";
                var visibleNames = names.Skip((page - 1) * pageSize).Take(pageSize);
                return paging ? status + "\n" + string.Join("\n", visibleNames) : listText + "\n" + status;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (System.Exception ex)
            {
                Logger.LogWarning(ex, "QQ Bot 调度器快捷菜单更新失败");
                return listText + "\n快捷菜单更新失败，请稍后重新发送 配置组 列表";
            }
            finally
            {
                _menuSyncLock.Release();
            }
        }

        var name = string.Join(' ', parts.Skip(2));
        var group = viewModel.ScriptGroups.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (group == null)
            return $"找不到配置组“{name}”，请先发送 配置组 列表";

        return await StartTaskAsync(
            () => RunOnUiAsync(() => viewModel.OnStartMultiScriptGroupWithNamesAsync(group.Name)),
            $"配置组 {group.Name}", "已有任务在执行中", ct);
    }

    private async Task<string> ExecuteJsCommandAsync(IReadOnlyList<string> parts, CancellationToken ct)
    {
        JsListViewModel? viewModel = null;
        await RunOnUiAsync(() =>
        {
            viewModel = App.GetService<JsListViewModel>();
            if (viewModel == null)
                throw new InvalidOperationException("无法获取 JS 脚本服务");
            viewModel.OnNavigatedTo();
            return Task.CompletedTask;
        });

        if (parts.Count < 3 || parts[2] == "列表")
            return FormatNames(viewModel.ScriptItems.Select(x => x.Manifest.Name), "当前没有脚本");

        var name = string.Join(' ', parts.Skip(2));
        var item = viewModel.ScriptItems.FirstOrDefault(x =>
            string.Equals(x.Manifest.Name, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(x.FolderName, name, StringComparison.OrdinalIgnoreCase));
        if (item == null)
            return $"找不到脚本“{name}”，请先发送 脚本 列表";

        return await StartTaskAsync(
            () => RunOnUiAsync(() => viewModel.OnStartRun(item)),
            $"脚本 {item.Manifest.Name}", "已有任务在执行中", ct);
    }

    private async Task<string> ExecutePathCommandAsync(IReadOnlyList<string> parts, CancellationToken ct)
    {
        MapPathingViewModel? viewModel = null;
        await RunOnUiAsync(() =>
        {
            viewModel = App.GetService<MapPathingViewModel>();
            if (viewModel == null)
                throw new InvalidOperationException("无法获取地图追踪服务");
            viewModel.OnNavigatedTo();
            return Task.CompletedTask;
        });

        var nodes = FlattenPathNodes(viewModel.TreeList).Where(x => !x.IsDirectory).ToList();
        if (parts.Count < 3 || parts[2] == "列表")
            return FormatNames(nodes.Select(x => x.Name ?? x.FileName ?? string.Empty), "当前没有地图追踪任务");

        var name = string.Join(' ', parts.Skip(2));
        var item = nodes.FirstOrDefault(x =>
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(x.FileName, name, StringComparison.OrdinalIgnoreCase));
        if (item == null)
            return $"找不到地图追踪“{name}”，请先发送 地图追踪列表";

        return await StartTaskAsync(
            () => RunOnUiAsync(() => viewModel.OnStart(item)),
            $"地图追踪 {item.Name ?? item.FileName}", "已有任务在执行中", ct, "地图追踪");
    }

    private async Task<string> ExecuteTaskCommandAsync(IReadOnlyList<string> parts, CancellationToken ct)
    {
        if (parts.Count < 3 || parts[2] == "列表")
            return GetFeatureText();

        var alias = parts[2].ToLowerInvariant();
        if (!TaskCommands.TryGetValue(alias, out var command))
            return $"未知独立任务“{parts[2]}”，请发送 功能";

        return await StartTaskAsync(
            () => RunTaskSettingsCommandAsync(command.Property),
            command.Label, "已有任务在执行中", ct);
    }

    private async Task RunTaskSettingsCommandAsync(string propertyName)
    {
        await RunOnUiAsync(async () =>
        {
            var viewModel = App.GetService<TaskSettingsPageViewModel>();
            var property = viewModel?.GetType().GetProperty(propertyName);
            var command = property?.GetValue(viewModel) as ICommand;
            if (command == null)
                throw new InvalidOperationException($"找不到任务命令 {propertyName}");

            command.Execute(null);
            var executionTask = command.GetType().GetProperty("ExecutionTask")?.GetValue(command) as Task;
            if (executionTask != null)
                await executionTask;
        });
    }

    private async Task<string> StartTaskAsync(Func<Task> action, string label, string busyText, CancellationToken ct,
        string? taskKind = null)
    {
        if (!await _runLock.WaitAsync(0, ct))
            return busyText;
        lock (_activeTaskLock)
        {
            _activeTaskKind = taskKind ?? label;
        }
        _ = Task.Run(async () =>
        {
            try { await action(); }
            catch (System.Exception ex) { Logger.LogError(ex, "QQ Bot 执行 {Label} 失败", label); }
            finally
            {
                lock (_activeTaskLock)
                {
                    _activeTaskKind = null;
                }
                _runLock.Release();
            }
        });
        return $"已启动{label}";
    }

    private string StopTask(string taskKind)
    {
        lock (_activeTaskLock)
        {
            if (!string.Equals(_activeTaskKind, taskKind, StringComparison.Ordinal))
                return $"当前没有正在执行的{taskKind}任务";
        }

        CancellationContext.Instance.Cancel();
        return $"已发送停止{taskKind}的指令";
    }

    private async Task RunOnUiAsync(Func<Task> action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
            throw new InvalidOperationException("BGI UI 调度器不可用");
        await dispatcher.InvokeAsync(action).Task.Unwrap();
    }

    private static IEnumerable<FileTreeNode<PathingTask>> FlattenPathNodes(IEnumerable<FileTreeNode<PathingTask>> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in FlattenPathNodes(node.Children))
                yield return child;
        }
    }

    private static string FormatNames(IEnumerable<string> names, string emptyText)
    {
        var values = names.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(30).ToList();
        return values.Count == 0 ? emptyText : string.Join("\n", values.Prepend($"共 {values.Count} 项："));
    }

    private async Task RunOneDragonAsync(string? configName = null)
    {
        try
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
                return;

            await dispatcher.InvokeAsync(async () =>
            {
                var viewModel = App.GetService<OneDragonFlowViewModel>();
                if (viewModel == null)
                    throw new InvalidOperationException("无法获取一条龙页面服务");
                viewModel.OnNavigatedTo();
                if (!string.IsNullOrWhiteSpace(configName))
                {
                    var config = viewModel.ConfigList.FirstOrDefault(x =>
                        string.Equals(x.Name, configName, StringComparison.OrdinalIgnoreCase));
                    if (config == null)
                        throw new InvalidOperationException($"找不到一条龙配置 {configName}");
                    viewModel.SelectedConfig = config;
                    viewModel.SetSomeSelectedConfig(config);
                }
                await viewModel.OnOneKeyExecute();
            }).Task.Unwrap();
        }
        catch (System.Exception ex)
        {
            Logger.LogError(ex, "QQ Bot 启动一条龙失败");
        }
    }

    private async Task<object?> BuildReplyKeyboardAsync(string command, CancellationToken ct)
    {
        var parts = SplitCommand(command);
        if (parts.Count < 2 || parts[1] is not ("一条龙列表" or "一条龙菜单" or "配置组列表" or "调度器" or "调度器菜单"))
            return null;

        var isScheduler = parts[1] is "配置组列表" or "调度器" or "调度器菜单";
        string[] names = [];
        await RunOnUiAsync(() =>
        {
            if (isScheduler)
            {
                var viewModel = App.GetService<ScriptControlViewModel>()
                    ?? throw new InvalidOperationException("无法获取配置组服务");
                viewModel.OnNavigatedTo();
                names = viewModel.ScriptGroups.Select(x => x.Name)
                    .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            }
            else
            {
                var viewModel = App.GetService<OneDragonFlowViewModel>()
                    ?? throw new InvalidOperationException("无法获取一条龙服务");
                viewModel.OnNavigatedTo();
                names = viewModel.ConfigList.Select(x => x.Name)
                    .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            }
            return Task.CompletedTask;
        });

        var pageSize = GetMenuPageSize(names.Length);
        var pageCount = Math.Max(1, (names.Length + pageSize - 1) / pageSize);
        var page = 1;
        if (parts[1] is "一条龙菜单" or "调度器菜单")
            page = parts.Count == 3 && int.TryParse(parts[2], out var parsedPage) ? parsedPage : 1;
        var start = Math.Clamp(page - 1, 0, pageCount - 1) * pageSize;
        var buttons = new List<object>
        {
            new
            {
                id = "bgi_refresh_lists",
                render_data = new { label = "刷新列表", visited_label = "刷新列表", style = 0 },
                action = new
                {
                    type = 2,
                    permission = new { type = 2 },
                    data = isScheduler ? "配置组列表" : "一条龙列表",
                    enter = true
                }
            }
        };
        foreach (var name in names.Skip(start).Take(pageSize))
        {
            var commandText = isScheduler ? $"配置组 {name}" : $"执行一条龙 {name}";
            buttons.Add(new
            {
                id = $"bgi_{(isScheduler ? "scheduler" : "one_dragon")}_{buttons.Count}",
                render_data = new
                {
                    label = ShortenMenuName(name, 10),
                    visited_label = "已选择",
                    style = 0
                },
                action = new
                {
                    type = 2,
                    permission = new { type = 2 },
                    data = commandText,
                    enter = true
                }
            });
        }

        if (pageCount > 1)
        {
            var nextPage = page == pageCount ? 1 : page + 1;
            buttons.Add(new
            {
                id = $"bgi_{(isScheduler ? "scheduler" : "one_dragon")}_next",
                render_data = new { label = "下一页", visited_label = "下一页", style = 0 },
                action = new
                {
                    type = 2,
                    permission = new { type = 2 },
                    data = isScheduler ? $"调度器菜单 {nextPage}" : $"一条龙菜单 {nextPage}",
                    enter = true
                }
            });
        }

        return new { content = new { rows = new[] { new { buttons } } } };
    }

    private async Task SendReplyAsync(bool isC2c, string? userOpenId, string? groupOpenId,
        string? eventId, string? messageId, string text, CancellationToken ct, object? keyboard = null)
    {
        if (string.IsNullOrWhiteSpace(eventId) && string.IsNullOrWhiteSpace(messageId))
            return;

        var target = isC2c
            ? $"https://api.sgroup.qq.com/v2/users/{userOpenId}/messages"
            : $"https://api.sgroup.qq.com/v2/groups/{groupOpenId}/messages";
        var token = await GetAccessTokenAsync(_config!.QqAppId, _config.QqClientSecret, ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, target);
        request.Headers.Add("Authorization", $"QQBot {token}");
        var payload = new Dictionary<string, object?>
        {
            ["content"] = text,
            ["msg_type"] = 0,
            ["event_id"] = eventId,
            ["msg_id"] = messageId,
            ["msg_seq"] = 1
        };
        if (keyboard != null)
            payload["keyboard"] = keyboard;
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await _httpClient.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
            return;
        if (keyboard == null)
        {
            var responseText = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"QQ 回复失败：{(int)response.StatusCode} {responseText}");
        }

        Logger.LogWarning("QQ Bot 回复携带快捷按钮失败，将回退为纯文本：{StatusCode}", response.StatusCode);
        payload.Remove("keyboard");
        using var fallback = new HttpRequestMessage(HttpMethod.Post, target);
        fallback.Headers.Add("Authorization", $"QQBot {token}");
        fallback.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var fallbackResponse = await _httpClient.SendAsync(fallback, ct);
        fallbackResponse.EnsureSuccessStatusCode();
    }

    private async Task<string> GetAccessTokenAsync(string appId, string clientSecret, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_accessToken) && DateTime.UtcNow < _accessTokenExpiresAt)
            return _accessToken;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (!string.IsNullOrWhiteSpace(_accessToken) && DateTime.UtcNow < _accessTokenExpiresAt)
                return _accessToken;

            using var content = new StringContent(JsonSerializer.Serialize(new { appId, clientSecret }), Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(TokenUrl, content, ct);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            _accessToken = json.RootElement.GetProperty("access_token").GetString();
            var expiresIn = json.RootElement.TryGetProperty("expires_in", out var expiresElement)
                && int.TryParse(expiresElement.GetString(), out var seconds)
                ? seconds
                : 300;
            _accessTokenExpiresAt = DateTime.UtcNow.AddSeconds(Math.Max(30, expiresIn - 60));
            return _accessToken ?? throw new InvalidOperationException("QQ API 未返回 access_token");
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<string> GetGatewayUrlAsync(string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, GatewayUrl);
        request.Headers.Add("Authorization", $"QQBot {accessToken}");
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.GetProperty("url").GetString()!;
    }

    private async Task RunHeartbeatAsync(ClientWebSocket socket, int interval, Func<long> sequence, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(interval, ct);
            await SendJsonAsync(socket, new { op = 1, d = sequence() == 0 ? (long?)null : sequence() }, ct);
        }
    }

    private async Task<JsonDocument> ReceiveJsonAsync(ClientWebSocket socket, CancellationToken ct)
    {
        using var stream = new System.IO.MemoryStream();
        var buffer = new byte[8192];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new WebSocketException("QQ Gateway 已关闭连接");
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return JsonDocument.Parse(stream.ToArray());
    }

    private async Task SendJsonAsync(ClientWebSocket socket, object payload, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        await _sendLock.WaitAsync(ct);
        try { await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct); }
        finally { _sendLock.Release(); }
    }

    private static string? GetString(JsonElement element, params string[] path)
    {
        foreach (var segment in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(segment, out element))
                return null;
        }
        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }
}

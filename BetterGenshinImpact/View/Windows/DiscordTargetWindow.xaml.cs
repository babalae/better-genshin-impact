using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BetterGenshinImpact.Helpers.Http;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.Service.I18n;
using BetterGenshinImpact.Service.Notifier;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

/// <summary>
/// Discord 推送目标树的节点类型
/// </summary>
public enum DiscordTargetNodeKind
{
    Guild,
    ChannelGroup,
    MemberGroup,
    Category,
    Channel,
    Member,
    Info,
    Placeholder,
}

/// <summary>
/// 目标树节点。群组节点（服务器 / 分组 / 分类）的 Target 为 null，只有叶子节点可以加入清单。
/// </summary>
public sealed class DiscordTargetNode
{
    public string Title { get; init; } = string.Empty;

    public DiscordTargetNodeKind Kind { get; init; }

    public ObservableCollection<DiscordTargetNode> Children { get; } = [];

    public DiscordBotTarget? Target { get; init; }

    public string GuildId { get; init; } = string.Empty;

    public string GuildName { get; init; } = string.Empty;

    /// <summary>
    /// 子节点是否已加载过，用于 TreeView 惰性加载
    /// </summary>
    public bool ChildrenLoaded { get; set; }
}

/// <summary>
/// Discord 推送目标挑选对话框。
/// 左侧按「服务器 → 频道 / 成员」两级分类展示，展开时才向 Discord 拉取数据；
/// 右侧是可增删的目标清单，按下确定后通过 <see cref="Result"/> 返回。
/// </summary>
public partial class DiscordTargetWindow : FluentWindow
{
    private readonly DiscordBotApiClient _apiClient;

    public ObservableCollection<DiscordTargetNode> RootNodes { get; } = [];

    public ObservableCollection<DiscordBotTarget> EditingTargets { get; } = [];

    /// <summary>
    /// 按下确定后的目标清单；取消时为空
    /// </summary>
    public List<DiscordBotTarget> Result { get; private set; } = [];

    public DiscordTargetWindow(string botToken, IEnumerable<DiscordBotTarget>? targets)
    {
        InitializeComponent();

        _apiClient = new DiscordBotApiClient(HttpClientFactory.GetCommonSendClient(), botToken);

        foreach (var target in targets ?? [])
        {
            EditingTargets.Add(target.Clone());
        }

        TargetTree.ItemsSource = RootNodes;
        DataContext = this;

        Loaded += OnLoaded;
        SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(this);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await LoadGuildsAsync();
    }

    private void SetStatus(string message)
    {
        StatusText.Text = message;
    }

    private static DiscordTargetNode CreatePlaceholder()
    {
        return new DiscordTargetNode { Title = I18nService.Instance.Translate("加载中…"), Kind = DiscordTargetNodeKind.Placeholder };
    }

    /// <summary>
    /// 加载机器人所在的服务器列表。频道与成员在展开时才拉取，避免一次打太多 API。
    /// </summary>
    private async Task LoadGuildsAsync()
    {
        RootNodes.Clear();
        SetStatus(I18nService.Instance.Translate("正在加载服务器…"));

        try
        {
            var guilds = await _apiClient.GetGuildsAsync();
            foreach (var guild in guilds.OrderBy(item => item.Name, StringComparer.CurrentCulture))
            {
                var node = new DiscordTargetNode
                {
                    Title = guild.Name,
                    Kind = DiscordTargetNodeKind.Guild,
                    GuildId = guild.Id,
                    GuildName = guild.Name,
                };
                node.Children.Add(CreatePlaceholder());
                RootNodes.Add(node);
            }

            SetStatus(guilds.Count == 0
                ? I18nService.Instance.Translate("机器人尚未加入任何服务器，请先邀请机器人")
                : string.Format(I18nService.Instance.Translate("共 {0} 个服务器，展开后可加载频道与成员"), guilds.Count));
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnTreeItemExpanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not System.Windows.Controls.TreeViewItem { DataContext: DiscordTargetNode node } || node.ChildrenLoaded)
        {
            return;
        }

        node.ChildrenLoaded = true;
        try
        {
            switch (node.Kind)
            {
                case DiscordTargetNodeKind.Guild:
                    BuildGuildGroups(node);
                    break;
                case DiscordTargetNodeKind.ChannelGroup:
                    await LoadChannelsAsync(node);
                    break;
                case DiscordTargetNodeKind.MemberGroup:
                    await LoadMembersAsync(node);
                    break;
            }
        }
        catch (Exception ex)
        {
            // 复位标志，否则用户再次展开该节点时会被直接返回，只能靠刷新整棵树重试
            node.ChildrenLoaded = false;
            node.Children.Clear();
            node.Children.Add(new DiscordTargetNode { Title = ex.Message, Kind = DiscordTargetNodeKind.Info });
            SetStatus(string.Empty);
        }
    }

    private static void BuildGuildGroups(DiscordTargetNode guildNode)
    {
        guildNode.Children.Clear();
        AddGroup(guildNode, I18nService.Instance.Translate("频道"), DiscordTargetNodeKind.ChannelGroup);
        AddGroup(guildNode, I18nService.Instance.Translate("成员（私信对象）"), DiscordTargetNodeKind.MemberGroup);
    }

    private static void AddGroup(DiscordTargetNode parent, string title, DiscordTargetNodeKind kind)
    {
        var group = new DiscordTargetNode
        {
            Title = title,
            Kind = kind,
            GuildId = parent.GuildId,
            GuildName = parent.GuildName,
        };
        group.Children.Add(CreatePlaceholder());
        parent.Children.Add(group);
    }

    /// <summary>
    /// 加载频道并按分类（Discord 的频道分组）做二级归类，没有分类的频道放在最后。
    /// </summary>
    private async Task LoadChannelsAsync(DiscordTargetNode groupNode)
    {
        SetStatus(I18nService.Instance.Translate("正在加载频道…"));
        var channels = await _apiClient.GetGuildChannelsAsync(groupNode.GuildId);

        groupNode.Children.Clear();

        var sendable = channels
            .Where(channel => DiscordChannelTypes.CanSendMessage(channel.Type))
            .OrderBy(channel => channel.Position)
            .ToList();

        if (sendable.Count == 0)
        {
            groupNode.Children.Add(new DiscordTargetNode
            {
                Title = I18nService.Instance.Translate("没有可发送消息的频道"),
                Kind = DiscordTargetNodeKind.Info,
            });
            SetStatus(string.Empty);
            return;
        }

        var categories = channels
            .Where(channel => channel.Type == DiscordChannelTypes.GuildCategory)
            .OrderBy(channel => channel.Position)
            .ToList();

        var categorizedChannelIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var category in categories)
        {
            var children = sendable
                .Where(channel => string.Equals(channel.ParentId, category.Id, StringComparison.Ordinal))
                .ToList();
            if (children.Count == 0)
            {
                continue;
            }

            var categoryNode = new DiscordTargetNode
            {
                Title = category.Name,
                Kind = DiscordTargetNodeKind.Category,
                GuildId = groupNode.GuildId,
                GuildName = groupNode.GuildName,
            };
            foreach (var channel in children)
            {
                categorizedChannelIds.Add(channel.Id);
                categoryNode.Children.Add(CreateChannelNode(channel, groupNode));
            }

            groupNode.Children.Add(categoryNode);
        }

        foreach (var channel in sendable.Where(channel => !categorizedChannelIds.Contains(channel.Id)))
        {
            groupNode.Children.Add(CreateChannelNode(channel, groupNode));
        }

        SetStatus(string.Empty);
    }

    private static DiscordTargetNode CreateChannelNode(DiscordChannelInfo channel, DiscordTargetNode context)
    {
        return new DiscordTargetNode
        {
            Title = $"# {channel.Name}",
            Kind = DiscordTargetNodeKind.Channel,
            GuildId = context.GuildId,
            GuildName = context.GuildName,
            Target = new DiscordBotTarget(
                DiscordBotTargetTypes.Channel,
                channel.Id,
                $"#{channel.Name}（{context.GuildName}）"),
        };
    }

    /// <summary>
    /// 加载服务器成员作为私信对象。需要机器人开启 SERVER MEMBERS INTENT，否则会显示 Discord 返回的错误。
    /// </summary>
    private async Task LoadMembersAsync(DiscordTargetNode groupNode)
    {
        SetStatus(I18nService.Instance.Translate("正在加载成员…"));
        var members = await _apiClient.GetGuildMembersAsync(groupNode.GuildId);

        groupNode.Children.Clear();
        if (members.Count == 0)
        {
            groupNode.Children.Add(new DiscordTargetNode
            {
                Title = I18nService.Instance.Translate("没有可私信的成员"),
                Kind = DiscordTargetNodeKind.Info,
            });
            SetStatus(string.Empty);
            return;
        }

        foreach (var member in members.OrderBy(item => item.DisplayName, StringComparer.CurrentCulture))
        {
            groupNode.Children.Add(new DiscordTargetNode
            {
                Title = $"@ {member.DisplayName}",
                Kind = DiscordTargetNodeKind.Member,
                GuildId = groupNode.GuildId,
                GuildName = groupNode.GuildName,
                Target = new DiscordBotTarget(
                    DiscordBotTargetTypes.DirectMessage,
                    member.Id,
                    $"@{member.DisplayName}（{groupNode.GuildName}）"),
            });
        }

        SetStatus(string.Format(I18nService.Instance.Translate("共 {0} 个成员"), members.Count));
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        _ = LoadGuildsAsync();
    }

    private void OnAddSelectedClick(object sender, RoutedEventArgs e)
    {
        if (TargetTree.SelectedItem is not DiscordTargetNode { Target: { } target })
        {
            SetStatus(I18nService.Instance.Translate("请先在左侧选择一个频道或成员"));
            return;
        }

        if (EditingTargets.Any(item => item.Type == target.Type && item.Id == target.Id))
        {
            SetStatus(I18nService.Instance.Translate("该目标已经在清单中"));
            return;
        }

        EditingTargets.Add(target.Clone());
        SetStatus(string.Empty);
    }

    private void OnRemoveSelectedClick(object sender, RoutedEventArgs e)
    {
        if (TargetList.SelectedItem is DiscordBotTarget target)
        {
            EditingTargets.Remove(target);
        }
    }

    private void OnManualChannelClick(object sender, RoutedEventArgs e)
    {
        AddManualTarget(DiscordBotTargetTypes.Channel, I18nService.Instance.Translate("频道 ID"));
    }

    private void OnManualUserClick(object sender, RoutedEventArgs e)
    {
        AddManualTarget(DiscordBotTargetTypes.DirectMessage, I18nService.Instance.Translate("用户 ID"));
    }

    /// <summary>
    /// 手动输入 ID 新增目标，用于没有开启成员 intent 或不在服务器里的情况。
    /// </summary>
    private void AddManualTarget(string type, string label)
    {
        var id = PromptDialog.Prompt(I18nService.Instance.Translate("请输入 ID，留空则取消"), label).Trim();
        if (string.IsNullOrEmpty(id))
        {
            return;
        }

        if (EditingTargets.Any(item => item.Type == type && item.Id == id))
        {
            SetStatus(I18nService.Instance.Translate("该目标已经在清单中"));
            return;
        }

        EditingTargets.Add(new DiscordBotTarget(type, id, $"{label}：{id}"));
        SetStatus(string.Empty);
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        Result = EditingTargets.ToList();
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

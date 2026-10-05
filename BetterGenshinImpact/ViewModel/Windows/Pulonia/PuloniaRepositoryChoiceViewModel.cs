using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Core.Script.Repositories;
using CommunityToolkit.Mvvm.ComponentModel;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>仓库选择器的共享展示模型，原始注册对象与持久化身份保持不变。</summary>
public partial class PuloniaRepositoryChoiceViewModel : ObservableObject
{
    /// <summary>选择器回传的原始仓库注册对象，不建立新的资源身份。</summary>
    public ScriptRepositoryRegistration Repository { get; }

    /// <summary>主行展示的仓库名称，也是键盘文字搜索依据。</summary>
    public string DisplayName => Repository.Name;

    /// <summary>官方图标采用主题强调色，其余来源使用中性色。</summary>
    public bool IsOfficial => Repository.IsOfficial;

    /// <summary>区分同名来源的紧凑类型标记。</summary>
    public string TypeLabel => IsOfficial ? "官方" : Repository.Kind == "remote" ? "第三方" : "本地";

    /// <summary>来源图标：官方盾牌、远程分支、本地文件夹。</summary>
    public SymbolRegular Icon => IsOfficial ? SymbolRegular.Shield24
        : Repository.Kind == "remote" ? SymbolRegular.BranchFork24 : SymbolRegular.Folder24;

    /// <summary>仅在展开选项中展示的渠道、远端或目录摘要。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tooltip))]
    private string _sourceDescription;

    /// <summary>完整位置通过悬停查看，不参与下拉选项宽度计算。</summary>
    public string Tooltip => $"{DisplayName} · {TypeLabel}\n{SourceDescription}\n本地目录：{Repository.Directory}";

    /// <summary>建立一项纯配置展示，不访问 Git、目录或任何资源正文。</summary>
    private PuloniaRepositoryChoiceViewModel(ScriptRepositoryRegistration repository, string sourceDescription)
    {
        Repository = repository;
        _sourceDescription = sourceDescription;
    }

    /// <summary>依据完整候选列表压缩来源，并为同名项补足区分信息。</summary>
    public static IReadOnlyList<PuloniaRepositoryChoiceViewModel> CreateChoices(
        IReadOnlyList<ScriptRepositoryRegistration> repositories, string? officialChannel)
    {
        var choices = repositories.Select(repository => new PuloniaRepositoryChoiceViewModel(repository,
            repository.IsOfficial ? FormatOfficialChannel(officialChannel)
            : repository.Kind == "remote" ? FormatRemote(repository) : GetDirectorySuffix(repository.Directory, 2))).ToArray();

        // 同名本地来源从末尾两级开始，逐级补充父目录；保持已有名称和实际目录不变。
        foreach (var choice in choices.Where(c => !c.Repository.IsRemote))
        {
            var peers = choices.Where(c => !c.Repository.IsRemote && !ReferenceEquals(c, choice)
                && string.Equals(c.DisplayName, choice.DisplayName, StringComparison.OrdinalIgnoreCase)).ToArray();
            var depth = 2;
            var parts = SplitDirectory(choice.Repository.Directory);
            while (depth < parts.Length && peers.Any(peer => string.Equals(
                       GetDirectorySuffix(peer.Repository.Directory, depth), GetDirectorySuffix(choice.Repository.Directory, depth),
                       StringComparison.OrdinalIgnoreCase))) depth++;
            choice.SourceDescription = GetDirectorySuffix(choice.Repository.Directory, depth);
        }

        // 相同名称、类型、远端和分支仍有多个副本时，仅这些项显示稳定短 ID。
        foreach (var group in choices.Where(c => c.Repository.Kind == "remote" && !c.IsOfficial)
                     .GroupBy(c => c.DisplayName + "\0" + c.TypeLabel + "\0" + c.SourceDescription, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
            foreach (var choice in group)
            {
                var id = choice.Repository.Id;
                choice.SourceDescription += " · #" + id[Math.Max(0, id.Length - 6)..];
            }
        return choices;
    }

    /// <summary>渠道变化直接刷新现有展示项，不重建候选列表或扰动当前选择。</summary>
    public void UpdateOfficialChannel(string? channel)
    {
        if (IsOfficial) SourceDescription = FormatOfficialChannel(channel);
    }

    /// <summary>官方仅显示当前更新渠道，空配置沿用默认 CNB。</summary>
    private static string FormatOfficialChannel(string? channel)
        => (channel is "CNB" or "GitHub" or "自定义" ? channel : "CNB") + " 更新源";

    /// <summary>第三方显示托管主机、仓库位置和分支，省略协议、认证信息与 .git 后缀。</summary>
    private static string FormatRemote(ScriptRepositoryRegistration repository)
    {
        var remote = repository.Remotes.FirstOrDefault(r => r.Id == repository.ActiveRemoteId)
                     ?? repository.Remotes.FirstOrDefault();
        var address = "尚未配置地址";
        if (remote is not null && Uri.TryCreate(remote.Url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
        {
            var host = uri.Host + (uri.IsDefaultPort || uri.Port < 0 ? string.Empty : ":" + uri.Port);
            var path = uri.GetComponents(UriComponents.Path, UriFormat.SafeUnescaped).Trim('/');
            if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
            address = host + (path.Length > 0 ? "/" + path : string.Empty);
        }
        return address + " · " + repository.Branch;
    }

    /// <summary>按 Windows 目录分段，不使用磁盘扫描或目录存在性判断。</summary>
    private static string[] SplitDirectory(string directory)
        => directory.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>显示所需的末尾层级，层级覆盖整个路径时保留盘符或 UNC 前缀。</summary>
    private static string GetDirectorySuffix(string directory, int depth)
    {
        var normalized = directory.Replace('/', '\\');
        var parts = SplitDirectory(directory);
        return parts.Length <= depth ? normalized : string.Join("\\", parts.TakeLast(depth));
    }
}

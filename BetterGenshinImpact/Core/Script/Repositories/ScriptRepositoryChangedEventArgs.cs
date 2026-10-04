using System;

namespace BetterGenshinImpact.Core.Script.Repositories;

/// <summary>当前仓库版本成功发布后的通知，不代表计划已经采用更新。</summary>
public sealed class ScriptRepositoryChangedEventArgs(string repositoryId, string revision) : EventArgs
{
    /// <summary>发生变化的仓库 ID。</summary>
    public string RepositoryId { get; } = repositoryId;
    /// <summary>更新后的 Git 提交或文件式来源版本，不代表已为计划保留内容。</summary>
    public string Revision { get; } = revision;
}

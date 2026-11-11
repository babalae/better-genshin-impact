using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 账户进度 <c>accounts/&lt;账户 ID&gt;/</c> 的读写：完成记录 <c>route-completions.json</c> 与首领领奖次数 <c>boss-rewards.json</c>。
/// 任意实例都可以写：写入在存储门和 <c>.writer.lock</c> 内读改写并原子替换，不丢其他实例的更新；读取不等待写入门。
/// </summary>
public sealed partial class PuloniaTaskStore
{
    /// <summary>账户进度目录的父目录名，相对于 User/Pulonia。</summary>
    internal const string AccountProgressDirectoryName = "accounts";

    /// <summary>完成记录文件名。</summary>
    internal const string RouteCompletionsFileName = "route-completions.json";

    /// <summary>首领领奖次数文件名。</summary>
    internal const string BossRewardsFileName = "boss-rewards.json";

    /// <summary>每个账户最多保存的完成记录数。</summary>
    internal const int MaxRouteCompletions = 100000;

    /// <summary>每个账户最多保存的首领领奖条目数。</summary>
    internal const int MaxBossRewardEntries = 10000;

    /// <summary>
    /// 一个账户的进度目录 <c>accounts/&lt;账户 ID&gt;</c>；账户 ID 先经格式校验再参与路径计算。
    /// </summary>
    /// <param name="root">Pulonia 根目录</param>
    /// <param name="accountId">账户 ID</param>
    internal static string GetAccountProgressDirectory(string root, string accountId)
    {
        PuloniaTaskValidator.ValidateId(accountId, "account/id");
        return Path.Combine(root, AccountProgressDirectoryName, accountId);
    }

    /// <summary>一个账户的完成记录文件路径。</summary>
    /// <param name="root">Pulonia 根目录</param>
    /// <param name="accountId">账户 ID</param>
    internal static string GetRouteCompletionsPath(string root, string accountId)
        => Path.Combine(GetAccountProgressDirectory(root, accountId), RouteCompletionsFileName);

    /// <summary>一个账户的首领领奖次数文件路径。</summary>
    /// <param name="root">Pulonia 根目录</param>
    /// <param name="accountId">账户 ID</param>
    internal static string GetBossRewardsPath(string root, string accountId)
        => Path.Combine(GetAccountProgressDirectory(root, accountId), BossRewardsFileName);

    /// <summary>
    /// 完成记录文件的修改标记（最后写入时间与长度）；文件不存在时为空。账户服务据此判断缓存是否需要重读。
    /// </summary>
    /// <param name="accountId">账户 ID</param>
    public PuloniaFileStamp? GetRouteCompletionsStamp(string accountId)
    {
        var file = new FileInfo(GetRouteCompletionsPath(RootDirectory, accountId));
        return file.Exists ? new PuloniaFileStamp(file.LastWriteTimeUtc.Ticks, file.Length) : null;
    }

    /// <summary>
    /// 读取一个账户的完成记录；文件不存在时为空列表，损坏时报错。不等待写入门，原子替换保证读到完整版本。
    /// </summary>
    /// <param name="accountId">账户 ID</param>
    /// <param name="ct">取消令牌</param>
    public async Task<IReadOnlyList<PuloniaRouteCompletion>> ReadRouteCompletionsAsync(string accountId, CancellationToken ct = default)
        => (await ReadRouteCompletionFileAsync(GetRouteCompletionsPath(RootDirectory, accountId), ct).ConfigureAwait(false))
            .Completions.AsReadOnly();

    /// <summary>
    /// 写入一条完成记录：同一路线只保留这条最新记录；同一运行、节点、尝试已写入时不重复写入。
    /// 在写锁内读改写并原子替换，返回后已耐久落盘。
    /// </summary>
    /// <remarks>新成功总是刚刚发生，按“后写入即最新”替换，不处理系统时钟回拨造成的时间倒序。</remarks>
    /// <param name="accountId">运行账户 ID</param>
    /// <param name="completion">本次完成的路线</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>是否写入了新记录；幂等重复时为 false</returns>
    /// <exception cref="InvalidOperationException">超过每个账户的记录上限</exception>
    public async Task<bool> PutRouteCompletionAsync(string accountId, PuloniaRouteCompletion completion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(completion);
        ValidateRouteCompletion(completion, "route_completion");
        var path = GetRouteCompletionsPath(RootDirectory, accountId);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var writerLock = await AcquireWriterLockAsync(ct).ConfigureAwait(false);
            var file = await ReadRouteCompletionFileAsync(path, ct).ConfigureAwait(false);
            // 1. 幂等：同一运行、节点、尝试已经写入
            if (file.Completions.Any(item => item.IsSameAttempt(completion)))
                return false;
            // 2. 每份路线只保留最新一条
            file.Completions.RemoveAll(item => item.Route == completion.Route);
            if (file.Completions.Count >= MaxRouteCompletions)
                throw new InvalidOperationException($"账户的完成记录超过 {MaxRouteCompletions} 条上限，请先在账户页清除不再需要的记录。");
            file.Completions.Add(completion);
            await WriteAtomicallyAsync(path, PuloniaTaskJson.Write(file), ct).ConfigureAwait(false);
            return true;
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// 删除一个账户中指定路线的完成记录，在写锁内读改写，不覆盖其他实例刚写入的记录；运行历史不受影响。
    /// </summary>
    /// <param name="accountId">账户 ID</param>
    /// <param name="routes">要删除的路线身份</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>删除的条数</returns>
    public async Task<int> RemoveRouteCompletionsAsync(string accountId, IReadOnlyCollection<string> routes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(routes);
        var path = GetRouteCompletionsPath(RootDirectory, accountId);
        if (routes.Count == 0)
            return 0;
        var targets = routes.ToHashSet(StringComparer.Ordinal);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var writerLock = await AcquireWriterLockAsync(ct).ConfigureAwait(false);
            var file = await ReadRouteCompletionFileAsync(path, ct).ConfigureAwait(false);
            var removed = file.Completions.RemoveAll(item => targets.Contains(item.Route));
            if (removed > 0)
                await WriteAtomicallyAsync(path, PuloniaTaskJson.Write(file), ct).ConfigureAwait(false);
            return removed;
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// 读取一个账户在一个首领讨伐节点当前重置批次内的累计成功领奖次数；没有记录时为 0。
    /// </summary>
    /// <param name="accountId">账户 ID</param>
    /// <param name="planId">计划 ID</param>
    /// <param name="taskId">节点 ID</param>
    /// <param name="resetId">重置批次</param>
    /// <param name="ct">取消令牌</param>
    public async Task<int> ReadBossRewardCountAsync(string accountId, string planId, string taskId, string resetId,
        CancellationToken ct = default)
    {
        var file = await ReadBossRewardFileAsync(GetBossRewardsPath(RootDirectory, accountId), ct).ConfigureAwait(false);
        var key = GetBossRewardKey(planId, taskId, resetId);
        return file.Rewards.FirstOrDefault(item => item.Key == key)?.Count ?? 0;
    }

    /// <summary>
    /// 一个账户记录了首领领奖次数（大于 0）的（计划、节点、重置批次）条目数；文件不存在时为 0。供删除账户前说明会删除的数据。
    /// </summary>
    /// <param name="accountId">账户 ID</param>
    /// <param name="ct">取消令牌</param>
    public async Task<int> CountBossRewardEntriesAsync(string accountId, CancellationToken ct = default)
        => (await ReadBossRewardFileAsync(GetBossRewardsPath(RootDirectory, accountId), ct).ConfigureAwait(false))
            .Rewards.Count(entry => entry.Count > 0);

    /// <summary>
    /// 每次确认领奖成功后立即把运行账户的累计次数加 1 并落盘；在写锁内读改写，失败时阻止任务继续消耗树脂。
    /// </summary>
    /// <param name="accountId">运行账户 ID</param>
    /// <param name="planId">计划 ID</param>
    /// <param name="taskId">节点 ID</param>
    /// <param name="resetId">重置批次</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>加 1 后的累计次数</returns>
    public async Task<int> IncrementBossRewardCountAsync(string accountId, string planId, string taskId, string resetId,
        CancellationToken ct = default)
    {
        var path = GetBossRewardsPath(RootDirectory, accountId);
        var key = GetBossRewardKey(planId, taskId, resetId);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var writerLock = await AcquireWriterLockAsync(ct).ConfigureAwait(false);
            var file = await ReadBossRewardFileAsync(path, ct).ConfigureAwait(false);
            var entry = file.Rewards.FirstOrDefault(item => item.Key == key);
            if (entry is null)
            {
                if (file.Rewards.Count >= MaxBossRewardEntries)
                    throw new InvalidOperationException($"账户的首领领奖记录超过 {MaxBossRewardEntries} 条上限。");
                entry = new PuloniaBossRewardEntry { Key = key };
                file.Rewards.Add(entry);
            }
            entry.Count = checked(entry.Count + 1);
            // 新写入时补全可读身份，迁移来的旧条目由此获得计划、节点与批次
            entry.PlanId = planId;
            entry.TaskId = taskId;
            entry.ResetId = resetId;
            await WriteAtomicallyAsync(path, PuloniaTaskJson.Write(file), ct).ConfigureAwait(false);
            return entry.Count;
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// 首领领奖次数的键：（计划 ID、节点 ID、重置批次）的 JSON 数组按 UTF-8 计算 SHA-256，输出大写十六进制。
    /// 与旧 <c>boss-rewards/&lt;哈希&gt;.json</c> 的文件名算法相同，迁移直接用文件名作键。
    /// </summary>
    /// <param name="planId">计划 ID</param>
    /// <param name="taskId">节点 ID</param>
    /// <param name="resetId">重置批次</param>
    public static string GetBossRewardKey(string planId, string taskId, string resetId)
    {
        var identity = new JArray(planId, taskId, resetId).ToString(Newtonsoft.Json.Formatting.None);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    /// <summary>读取完成记录文件；不存在时返回空文件，损坏时报错，避免把未知进度当作没有记录而重复执行。</summary>
    /// <param name="path">文件路径</param>
    /// <param name="ct">取消令牌</param>
    internal static async Task<PuloniaRouteCompletionFile> ReadRouteCompletionFileAsync(string path, CancellationToken ct)
    {
        if (await ReadIfExistsAsync(path, ct).ConfigureAwait(false) is not { } json)
            return new PuloniaRouteCompletionFile();
        var file = PuloniaTaskJson.Read<PuloniaRouteCompletionFile>(json);
        ValidateRouteCompletionFile(file, path);
        return file;
    }

    /// <summary>
    /// 校验完成记录文件：格式版本、上限、每条记录的字段，以及路线身份和（运行、节点、尝试）不重复。
    /// </summary>
    /// <param name="file">待校验的文件内容</param>
    /// <param name="path">文件路径，用于错误定位</param>
    internal static void ValidateRouteCompletionFile(PuloniaRouteCompletionFile file, string path)
    {
        if (file.SchemaVersion != PuloniaRouteCompletionFile.CurrentSchemaVersion)
            throw new PuloniaTaskValidationException(path, $"不支持完成记录格式版本 {file.SchemaVersion}。");
        if (file.Completions is null || file.Completions.Count > MaxRouteCompletions)
            throw new PuloniaTaskValidationException(path, "完成记录缺失或超过数量上限。");
        var routes = new HashSet<string>(StringComparer.Ordinal);
        var attempts = new HashSet<(string, string, int)>();
        foreach (var completion in file.Completions)
        {
            ValidateRouteCompletion(completion, path);
            if (!routes.Add(completion.Route) || !attempts.Add((completion.RunId, completion.TaskAddress, completion.Attempt)))
                throw new PuloniaTaskValidationException(path, "完成记录中的路线或（运行、节点、尝试）重复。");
        }
    }

    /// <summary>
    /// 校验一条完成记录的字段；完成记录文件与运行记录的“本次完成的路线”共用。
    /// </summary>
    /// <param name="completion">完成记录</param>
    /// <param name="path">错误定位</param>
    internal static void ValidateRouteCompletion(PuloniaRouteCompletion? completion, string path)
    {
        if (completion is null || string.IsNullOrWhiteSpace(completion.Route) || string.IsNullOrWhiteSpace(completion.TaskAddress)
            || completion.Attempt <= 0 || string.IsNullOrWhiteSpace(completion.Instance) || completion.Rule is null
            || completion.CompletedAt == default || !Enum.IsDefined(completion.Rule.Mode)
            || completion.Rule.Mode == PuloniaRouteRefreshMode.Auto || !Enum.IsDefined(completion.Rule.Source))
            throw new PuloniaTaskValidationException(path, "完成记录字段无效。");
        PuloniaTaskValidator.ValidateId(completion.RunId, path + "/run_id");
    }

    /// <summary>读取首领领奖次数文件；不存在时返回空文件，负值或损坏内容报错，避免把未知累计当零重新刷取。</summary>
    /// <param name="path">文件路径</param>
    /// <param name="ct">取消令牌</param>
    internal static async Task<PuloniaBossRewardFile> ReadBossRewardFileAsync(string path, CancellationToken ct)
    {
        if (await ReadIfExistsAsync(path, ct).ConfigureAwait(false) is not { } json)
            return new PuloniaBossRewardFile();
        var file = PuloniaTaskJson.Read<PuloniaBossRewardFile>(json);
        ValidateBossRewardFile(file, path);
        return file;
    }

    /// <summary>校验首领领奖次数文件：格式版本、上限、键不重复且为 64 位十六进制、次数不为负。</summary>
    /// <param name="file">待校验的文件内容</param>
    /// <param name="path">文件路径，用于错误定位</param>
    internal static void ValidateBossRewardFile(PuloniaBossRewardFile file, string path)
    {
        if (file.SchemaVersion != PuloniaBossRewardFile.CurrentSchemaVersion)
            throw new PuloniaTaskValidationException(path, $"不支持首领领奖次数格式版本 {file.SchemaVersion}。");
        if (file.Rewards is null || file.Rewards.Count > MaxBossRewardEntries)
            throw new PuloniaTaskValidationException(path, "首领领奖次数缺失或超过数量上限。");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in file.Rewards)
        {
            if (entry is null || entry.Key.Length != 64 || !entry.Key.All(Uri.IsHexDigit) || !keys.Add(entry.Key))
                throw new PuloniaTaskValidationException(path, "首领领奖次数的键无效或重复。");
            if (entry.Count < 0)
                throw new PuloniaTaskValidationException(path, "首领累计领奖次数不能为负数。");
        }
    }

    /// <summary>
    /// 账户目录保存成功后，尽力删除被移除账户的进度目录 <c>accounts/&lt;账户 ID&gt;/</c>（完成记录、首领领奖次数等）。
    /// 删除失败不影响保存结果，遗留目录不会被其他账户读取。
    /// </summary>
    /// <param name="previous">保存前的耐久目录</param>
    /// <param name="saved">刚保存的目录</param>
    private void DeleteRemovedAccountProgress(PuloniaGenshinAccountSettings previous, PuloniaGenshinAccountSettings saved)
    {
        var savedIds = saved.Accounts.Select(account => account.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var account in previous.Accounts.Where(account => !savedIds.Contains(account.Id)))
        {
            try
            {
                var directory = GetAccountProgressDirectory(RootDirectory, account.Id);
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PuloniaTaskValidationException) { }
        }
    }
}

/// <summary>
/// 文件的修改标记：最后写入时间（UTC 刻度）与长度。两次标记相同即视为内容未变。
/// </summary>
/// <remarks>同一时钟刻度内、长度相同的两次外部写入无法区分；本进程的写入会直接让缓存失效，不依赖标记。</remarks>
/// <param name="LastWriteTicks">最后写入时间的 UTC 刻度。</param>
/// <param name="Length">文件长度（字节）。</param>
public readonly record struct PuloniaFileStamp(long LastWriteTicks, long Length);

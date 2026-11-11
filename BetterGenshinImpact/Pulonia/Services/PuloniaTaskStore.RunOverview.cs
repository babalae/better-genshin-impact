using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 执行记录跨实例显示（统筹设计 6.5）所需的只读访问：其他实例的 <c>state.json</c>、共享历史目录的修改标记，
/// 以及按运行 ID 在共享历史中查找运行。全部只读，不等待存储门、不取 <c>.writer.lock</c>：
/// 写入方以原子替换提交，读取以 <see cref="FileShare.Read"/> | <see cref="FileShare.Delete"/> 打开，读到的总是完整的旧版或新版。
/// </summary>
public sealed partial class PuloniaTaskStore
{
    /// <summary>运行历史目录名，相对于 User/Pulonia。</summary>
    private const string HistoryDirectoryName = "history";

    /// <summary>
    /// 共享历史目录的修改标记：<c>history</c> 目录与其下各计划目录的最后写入时间和历史文件数。
    /// 新增、删除或替换历史文件都会改变所在目录的写入时间，文件数作为补充；只读取目录元数据，不读取文件内容。
    /// </summary>
    /// <returns>修改标记；历史目录不存在时为空字符串</returns>
    public string GetHistoryStamp()
    {
        var root = new DirectoryInfo(Path.Combine(RootDirectory, HistoryDirectoryName));
        if (!root.Exists)
            return string.Empty;
        var builder = new StringBuilder();
        builder.Append(root.LastWriteTimeUtc.Ticks);
        foreach (var directory in root.EnumerateDirectories().OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            builder.Append('|').Append(directory.Name)
                .Append(':').Append(directory.LastWriteTimeUtc.Ticks)
                .Append(':').Append(directory.EnumerateFiles("*.json").Count());
        }
        return builder.ToString();
    }

    /// <summary>
    /// 列出除本实例外各实例目录中已存在的 <c>state.json</c> 及其修改标记（文件长度与最后写入时间），按目录名排序。
    /// 只读取文件元数据；本实例的运行状态由任务服务在内存中实时提供，不在这里列出。
    /// </summary>
    /// <param name="currentInstanceKey">本实例的实例键</param>
    /// <returns>其他实例的运行状态文件</returns>
    internal IReadOnlyList<PuloniaInstanceStateFile> ListOtherInstanceStateFiles(string currentInstanceKey)
    {
        var root = new DirectoryInfo(Path.Combine(RootDirectory, InstancesDirectoryName));
        if (!root.Exists)
            return [];
        var own = PuloniaInstanceKey.ToDirectoryName(currentInstanceKey);
        var files = new List<PuloniaInstanceStateFile>();
        foreach (var directory in root.EnumerateDirectories().OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            if (string.Equals(directory.Name, own, StringComparison.OrdinalIgnoreCase))
                continue;
            // 实例目录中可能只有实例设置，没有运行过的实例没有运行状态
            var file = new FileInfo(Path.Combine(directory.FullName, StateFileName));
            if (!file.Exists)
                continue;
            files.Add(new PuloniaInstanceStateFile(directory.Name, file.FullName,
                file.Length + ":" + file.LastWriteTimeUtc.Ticks));
        }
        return files;
    }

    /// <summary>
    /// 只读读取另一个实例的运行状态文件：不等待存储门、不取写锁，也不从备份恢复；读不出时由调用方跳过并说明。
    /// </summary>
    /// <param name="path">state.json 的完整路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>运行状态；文件已不存在时为 null</returns>
    /// <exception cref="PuloniaTaskValidationException">文件格式无效</exception>
    internal static async Task<PuloniaTaskState?> ReadInstanceStateAsync(string path, CancellationToken ct)
        => await ReadIfExistsAsync(path, ct).ConfigureAwait(false) is { } json ? PuloniaTaskJson.ReadState(json) : null;

    /// <summary>
    /// 只按运行 ID 在共享历史中查找一份完整历史（含快照）：其他实例在本实例启动后归档的运行不在任务服务的常驻索引中，
    /// 续跑这类运行时据此读取。只检查各计划目录中是否有同名文件，不等待写入门。
    /// </summary>
    /// <param name="runId">运行 ID</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>历史记录；没有时为 null，损坏时抛出</returns>
    public async Task<PuloniaTaskRunRecord?> FindHistoryRecordAsync(string runId, CancellationToken ct = default)
    {
        PuloniaTaskValidator.ValidateId(runId, "history/run_id");
        var root = Path.Combine(RootDirectory, HistoryDirectoryName);
        if (!Directory.Exists(root))
            return null;
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            ct.ThrowIfCancellationRequested();
            var path = Path.Combine(directory, runId + ".json");
            if (await ReadIfExistsAsync(path, ct).ConfigureAwait(false) is { } json)
                return ReadHistoryJson(path, json);
        }
        return null;
    }
}

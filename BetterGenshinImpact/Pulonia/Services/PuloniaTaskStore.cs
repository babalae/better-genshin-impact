using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 计划与预设的最小文件存储；不引用页面，不负责执行或运行状态。
/// </summary>
public sealed class PuloniaTaskStore : IDisposable
{
    /// <summary>
    /// UTF-8 无 BOM，遇到无效字节时明确报错。
    /// </summary>
    private static readonly UTF8Encoding Utf8WithoutBom = new(false, true);

    /// <summary>
    /// 串行化当前存储实例的读写。
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// 存储根目录，默认位于程序的 User/Pulonia。
    /// </summary>
    public string RootDirectory { get; }

    /// <summary>
    /// 建立默认存储对象；构造过程不创建文件或启动游戏。
    /// </summary>
    public PuloniaTaskStore() : this(Global.Absolute("User/Pulonia"))
    {
    }

    /// <summary>
    /// 使用显式目录，便于导入工具和验收样例调用。
    /// </summary>
    public PuloniaTaskStore(string rootDirectory)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
    }

    /// <summary>
    /// 按稳定 ID 读取计划；文件缺失返回 null，损坏直接报错。
    /// </summary>
    public async Task<PuloniaTaskPlan?> LoadPlanAsync(string id, CancellationToken ct = default)
    {
        var path = GetPath("plans", id);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReadPlanAsync(path, id, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 读取预设，不将缺失预设替换为空参数。
    /// </summary>
    public async Task<PuloniaTaskPreset?> LoadPresetAsync(string id, CancellationToken ct = default)
    {
        var path = GetPath("presets", id);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReadPresetAsync(path, id, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 保存独立副本并返回新修订；调用者仅在成功后替换编辑模型。
    /// </summary>
    public async Task<PuloniaTaskPlan> SavePlanAsync(PuloniaTaskPlan plan, CancellationToken ct = default)
    {
        var candidate = PuloniaTaskJson.ClonePlan(plan);
        var path = GetPath("plans", candidate.Id);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var writerLock = AcquireWriterLock();
            var current = await ReadPlanAsync(path, candidate.Id, ct).ConfigureAwait(false);
            CheckRevision(candidate.Revision, current?.Revision, candidate.Id);
            // 已持有存储门和文件写锁，直接读文件，不能再调用会重入 _gate 的公开读取方法。
            await PuloniaTaskValidator.ValidatePlanReferencesAsync(candidate,
                (id, token) => ReadPlanAsync(GetPath("plans", id), id, token), ct).ConfigureAwait(false);
            candidate.Revision = checked(candidate.Revision + 1);
            await WriteAtomicallyAsync(path, PuloniaTaskJson.WritePlan(candidate), ct).ConfigureAwait(false);
            return candidate;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 保存预设，使用相同的修订检查和完整文件替换。
    /// </summary>
    public async Task<PuloniaTaskPreset> SavePresetAsync(PuloniaTaskPreset preset, CancellationToken ct = default)
    {
        var candidate = PuloniaTaskJson.ReadPreset(PuloniaTaskJson.WritePreset(preset));
        var path = GetPath("presets", candidate.Id);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var writerLock = AcquireWriterLock();
            var current = await ReadPresetAsync(path, candidate.Id, ct).ConfigureAwait(false);
            CheckRevision(candidate.Revision, current?.Revision, candidate.Id);
            candidate.Revision = checked(candidate.Revision + 1);
            await WriteAtomicallyAsync(path, PuloniaTaskJson.WritePreset(candidate), ct).ConfigureAwait(false);
            return candidate;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 列出计划的独立编辑副本；任一损坏文件明确报错，不悄悄忽略。
    /// </summary>
    public async Task<IReadOnlyList<PuloniaTaskPlan>> ListPlansAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var directory = Path.Combine(RootDirectory, "plans");
            if (!Directory.Exists(directory))
                return Array.Empty<PuloniaTaskPlan>();
            var result = new List<PuloniaTaskPlan>();
            foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
            {
                ct.ThrowIfCancellationRequested();
                var id = Path.GetFileNameWithoutExtension(path);
                PuloniaTaskValidator.ValidateId(id, path);
                var plan = await ReadPlanAsync(path, id, ct).ConfigureAwait(false);
                if (plan is not null)
                    result.Add(plan);
            }
            // 列表顺序是编辑器布局元数据，不写入任何单个计划，也不影响执行修订。
            var catalog = await ReadPlanCatalogAsync(ct).ConfigureAwait(false);
            var positions = catalog.PlanOrder
                .Select((id, index) => (id, index))
                .ToDictionary(item => item.id, item => item.index, StringComparer.Ordinal);
            result.Sort((a, b) => ComparePlanOrder(a, b, positions));
            return result.AsReadOnly();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 保存计划列表的显示顺序；未知或尚未保存的计划 ID 可以暂存，读取列表时会自动忽略。
    /// </summary>
    public async Task SavePlanOrderAsync(IReadOnlyList<string> planIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(planIds);
        var catalog = new PuloniaTaskPlanCatalog { PlanOrder = planIds.ToList() };
        ValidatePlanCatalog(catalog);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var writerLock = AcquireWriterLock();
            var path = Path.Combine(RootDirectory, "plan-order.json");
            await WriteAtomicallyAsync(path, PuloniaTaskJson.Write(catalog), ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 列出共享预设的独立编辑副本；任一损坏文件明确报错，不悄悄忽略。
    /// </summary>
    public async Task<IReadOnlyList<PuloniaTaskPreset>> ListPresetsAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var directory = Path.Combine(RootDirectory, "presets");
            if (!Directory.Exists(directory))
                return Array.Empty<PuloniaTaskPreset>();
            var result = new List<PuloniaTaskPreset>();
            foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
            {
                ct.ThrowIfCancellationRequested();
                var id = Path.GetFileNameWithoutExtension(path);
                PuloniaTaskValidator.ValidateId(id, path);
                var preset = await ReadPresetAsync(path, id, ct).ConfigureAwait(false);
                if (preset is not null)
                    result.Add(preset);
            }
            result.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            return result.AsReadOnly();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 读取唯一当前运行状态；正式文件损坏时仅允许从有效备份显式恢复。
    /// </summary>
    public async Task<PuloniaTaskStateLoadResult> LoadStateAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var path = Path.Combine(RootDirectory, "state.json");
            var json = await ReadIfExistsAsync(path, ct).ConfigureAwait(false);
            if (json is null)
            {
                var backupOnlyJson = await ReadIfExistsAsync(path + ".bak", ct).ConfigureAwait(false);
                return backupOnlyJson is null
                    ? new PuloniaTaskStateLoadResult(new PuloniaTaskState(), false)
                    : new PuloniaTaskStateLoadResult(PuloniaTaskJson.ReadState(backupOnlyJson), true);
            }

            try
            {
                return new PuloniaTaskStateLoadResult(PuloniaTaskJson.ReadState(json), false);
            }
            catch (Exception primaryError) when (primaryError is Newtonsoft.Json.JsonException
                                                  or PuloniaTaskValidationException)
            {
                var backupJson = await ReadIfExistsAsync(path + ".bak", ct).ConfigureAwait(false);
                if (backupJson is null)
                    throw new PuloniaTaskValidationException(path,
                        "正式运行状态已损坏且没有可用备份，已停止自动运行。", primaryError);
                try
                {
                    return new PuloniaTaskStateLoadResult(PuloniaTaskJson.ReadState(backupJson), true);
                }
                catch (Exception backupError) when (backupError is Newtonsoft.Json.JsonException
                                                     or PuloniaTaskValidationException)
                {
                    throw new PuloniaTaskValidationException(path,
                        $"正式运行状态和备份都无法读取，已停止自动运行。备份错误：{backupError.Message}",
                        primaryError);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 用刷新到底层存储的完整文件替换保存当前运行状态。
    /// </summary>
    public async Task SaveStateAsync(PuloniaTaskState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        var candidate = PuloniaTaskJson.ReadState(PuloniaTaskJson.WriteState(state));
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var writerLock = AcquireWriterLock();
            var path = Path.Combine(RootDirectory, "state.json");
            var replaceWithoutChangingBackup = false;
            var currentJson = await ReadIfExistsAsync(path, ct).ConfigureAwait(false);
            if (currentJson is not null)
            {
                try
                {
                    _ = PuloniaTaskJson.ReadState(currentJson);
                }
                catch (Exception ex) when (ex is Newtonsoft.Json.JsonException or PuloniaTaskValidationException)
                {
                    // 正式文件损坏且本次状态来自有效备份时，不能用损坏文件覆盖该备份。
                    replaceWithoutChangingBackup = true;
                }
            }
            await WriteAtomicallyAsync(path, PuloniaTaskJson.WriteState(candidate), ct,
                replaceWithoutChangingBackup).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 幂等写入一份运行历史；已经存在的同一运行不会被覆盖。
    /// </summary>
    public async Task ArchiveRunAsync(PuloniaTaskRunRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        PuloniaTaskValidator.ValidateId(record.Request.PlanId, "history/plan_id");
        if (record.RunId == Guid.Empty || record.RequestId == Guid.Empty)
            throw new PuloniaTaskValidationException("history", "运行或请求 ID 不能为空。");
        var candidateJson = PuloniaTaskJson.WriteRunRecord(record);
        var path = Path.Combine(RootDirectory, "history", record.Request.PlanId, record.RunId.ToString("N") + ".json");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var writerLock = AcquireWriterLock();
            var existingJson = await ReadIfExistsAsync(path, ct).ConfigureAwait(false);
            if (existingJson is not null)
            {
                var existing = PuloniaTaskJson.ReadRunRecord(existingJson);
                if (existing.RunId != record.RunId || existing.RequestId != record.RequestId)
                    throw new PuloniaTaskValidationException(path, "历史文件身份与待归档运行不一致。");
                if (!string.Equals(PuloniaTaskJson.WriteRunRecord(existing), candidateJson,
                        StringComparison.Ordinal))
                    throw new PuloniaTaskValidationException(path, "同一运行 ID 的历史内容不一致，拒绝静默覆盖。");
                return;
            }
            await WriteAtomicallyAsync(path, candidateJson, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 列出全部不可变运行历史，损坏文件会明确阻止返回不完整列表。
    /// </summary>
    public async Task<IReadOnlyList<PuloniaTaskRunRecord>> ListHistoryAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var root = Path.Combine(RootDirectory, "history");
            if (!Directory.Exists(root))
                return Array.Empty<PuloniaTaskRunRecord>();
            var records = new List<PuloniaTaskRunRecord>();
            foreach (var path in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var json = await ReadIfExistsAsync(path, ct).ConfigureAwait(false)
                           ?? throw new IOException($"读取历史时文件消失：{path}");
                var record = PuloniaTaskJson.ReadRunRecord(json);
                if (!string.Equals(Path.GetFileNameWithoutExtension(path), record.RunId.ToString("N"),
                        StringComparison.OrdinalIgnoreCase))
                    throw new PuloniaTaskValidationException(path, "历史文件名与运行 ID 不一致。");
                records.Add(record);
            }
            return records.OrderByDescending(item => item.SubmittedAt).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 生成文件路径；名称不能参与路径计算。
    /// </summary>
    private string GetPath(string folder, string id)
    {
        PuloniaTaskValidator.ValidateId(id, folder);
        return Path.Combine(RootDirectory, folder, id + ".json");
    }

    /// <summary>
    /// 跨存储对象或进程排斥同时写入；占用时直接向调用端报告 IOException。
    /// </summary>
    private FileStream AcquireWriterLock()
    {
        Directory.CreateDirectory(RootDirectory);
        return new FileStream(Path.Combine(RootDirectory, ".writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    /// <summary>
    /// 拒绝旧修订覆盖及误把已保存对象当作新文件。
    /// </summary>
    private static void CheckRevision(long expected, long? actual, string id)
    {
        if (expected != (actual ?? 0))
            throw new PuloniaTaskValidationException(id, $"保存冲突：编辑修订为 {expected}，文件修订为 {actual?.ToString() ?? "不存在"}。请重新读取并保留当前编辑内容。");
    }

    /// <summary>
    /// 读取并检查计划文件内部 ID 与文件名是否一致。
    /// </summary>
    private static async Task<PuloniaTaskPlan?> ReadPlanAsync(string path, string id, CancellationToken ct)
    {
        var json = await ReadIfExistsAsync(path, ct).ConfigureAwait(false);
        if (json is null)
            return null;
        var plan = PuloniaTaskJson.ReadPlan(json);
        if (plan.Id != id)
            throw new PuloniaTaskValidationException(path, "文件名与计划 ID 不一致。");
        return plan;
    }

    /// <summary>
    /// 读取并检查预设文件的身份。
    /// </summary>
    private static async Task<PuloniaTaskPreset?> ReadPresetAsync(string path, string id, CancellationToken ct)
    {
        var json = await ReadIfExistsAsync(path, ct).ConfigureAwait(false);
        if (json is null)
            return null;
        var preset = PuloniaTaskJson.ReadPreset(json);
        if (preset.Id != id)
            throw new PuloniaTaskValidationException(path, "文件名与预设 ID 不一致。");
        return preset;
    }

    /// <summary>
    /// 读取计划列表顺序；文件尚未建立时返回空目录，已有文件损坏时明确报错。
    /// </summary>
    private async Task<PuloniaTaskPlanCatalog> ReadPlanCatalogAsync(CancellationToken ct)
    {
        var json = await ReadIfExistsAsync(Path.Combine(RootDirectory, "plan-order.json"), ct).ConfigureAwait(false);
        if (json is null)
            return new PuloniaTaskPlanCatalog();
        var catalog = PuloniaTaskJson.Read<PuloniaTaskPlanCatalog>(json);
        ValidatePlanCatalog(catalog);
        return catalog;
    }

    /// <summary>
    /// 校验计划目录版本、数量、ID 格式和重复项，避免损坏元数据产生不稳定排序。
    /// </summary>
    private static void ValidatePlanCatalog(PuloniaTaskPlanCatalog catalog)
    {
        if (catalog.SchemaVersion != PuloniaTaskPlanCatalog.CurrentSchemaVersion)
            throw new PuloniaTaskValidationException("plan-order.json", $"不支持计划目录格式版本 {catalog.SchemaVersion}。");
        if (catalog.PlanOrder.Count > 10000)
            throw new PuloniaTaskValidationException("plan-order.json", "计划目录超过数量上限。");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in catalog.PlanOrder)
        {
            PuloniaTaskValidator.ValidateId(id, "plan-order.json");
            if (!ids.Add(id))
                throw new PuloniaTaskValidationException("plan-order.json", $"计划 ID {id} 在显示顺序中重复。");
        }
    }

    /// <summary>
    /// 优先按目录位置排序，目录未记录的计划按稳定 ID 排在末尾。
    /// </summary>
    private static int ComparePlanOrder(PuloniaTaskPlan left, PuloniaTaskPlan right,
        IReadOnlyDictionary<string, int> positions)
    {
        var leftKnown = positions.TryGetValue(left.Id, out var leftPosition);
        var rightKnown = positions.TryGetValue(right.Id, out var rightPosition);
        if (leftKnown && rightKnown)
            return leftPosition.CompareTo(rightPosition);
        if (leftKnown)
            return -1;
        if (rightKnown)
            return 1;
        return StringComparer.Ordinal.Compare(left.Id, right.Id);
    }

    /// <summary>
    /// 只把真正的文件缺失当作 null，权限或编码错误必须保留。
    /// </summary>
    private static async Task<string?> ReadIfExistsAsync(string path, CancellationToken ct)
    {
        try
        {
            // 允许正式文件被完整替换；已打开的句柄仍读取原版本。
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous);
            using var reader = new StreamReader(stream, Utf8WithoutBom, detectEncodingFromByteOrderMarks: true);
            return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// 写同目录临时文件、刷新到底层存储，再替换并保留上一版。
    /// </summary>
    private static async Task WriteAtomicallyAsync(string path, string json, CancellationToken ct,
        bool preserveExistingBackup = false)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var bytes = Utf8WithoutBom.GetBytes(json);
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            ct.ThrowIfCancellationRequested();

            // 替换是提交点；提交后即使令牌刚好取消，也应返回保存成功。
            if (File.Exists(path) && preserveExistingBackup)
                File.Move(temporaryPath, path, overwrite: true);
            else if (File.Exists(path))
                File.Replace(temporaryPath, path, path + ".bak");
            else
                File.Move(temporaryPath, path);
        }
        finally
        {
            // 仅清理本次创建的临时文件，失败时不覆盖原始保存异常。
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// 宿主停止使用存储后释放同步资源。
    /// </summary>
    public void Dispose() => _gate.Dispose();
}

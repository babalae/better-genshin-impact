using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.AccountRecords.Model;

namespace BetterGenshinImpact.Service.AccountRecords;

/// <summary>需求文件沿用账号锁与原子保存；不向库存事件写入需求状态。</summary>
public sealed partial class InventoryStore
{
    /// <summary>活动需求与交换文件采用相同上限，避免生成无法重新导入的备份。</summary>
    private const int MaxDemandItems = 5000;

    /// <summary>读取最新的全局需求判断设置，不识别账号、不创建文件或恢复备份。</summary>
    public Task<MaterialDemandSettings> ReadDemandSettingsAsync() => Locked(() =>
    {
        var settings = Settings(restoreBackup: false);
        return new MaterialDemandSettings(settings.UseDeltaForDemands, settings.UseFamiliesForDemands,
            settings.DemandCraftingReservePercent);
    }, initialize: false);

    /// <summary>在同一锁内读取需求、当前库存及未归档事件，不加载库存历史。</summary>
    public Task<(InventoryWorkspace Inventory, MaterialDemandDocument Demands)> ReadDemandWorkspaceAsync(string uid) =>
        Locked(() => (ReadWorkspace(uid, includeHistory: false), ReadDemands(uid)), initialize: false);

    /// <summary>读取需求，不创建文件或恢复写入备份。</summary>
    public Task<MaterialDemandDocument> ReadDemandsAsync(string uid) => Locked(() => ReadDemands(uid), initialize: false);

    /// <summary>读取并校验需求，拒绝覆盖未知版本或损坏结构。</summary>
    private MaterialDemandDocument ReadDemands(string uid)
    {
        var data = Read<MaterialDemandDocument>(Path.Combine(AccountDirectory(uid), "demands.json"), restoreBackup: false);
        if (data.SchemaVersion != 1 || data.Items == null || data.History == null || data.Prior == null || data.Items.Count > MaxDemandItems)
            throw new InvalidDataException("需求文件的版本、物品名称、目标数量或时间无效。");
        foreach (var (name, demand) in data.Items)
        {
            ValidateDemandName(name);
            ValidateStoredDemand(demand);
        }
        foreach (var name in data.Prior) ValidateDemandName(name);
        foreach (var archive in data.History)
        {
            if (archive == null || string.IsNullOrWhiteSpace(archive.ConfirmationMethod) ||
                archive.CompletedAt == default || archive.ArchivedAt == default ||
                archive.CraftingReservePercent is < 0 or > 20)
                throw new InvalidDataException("需求归档条目的完成依据或时间无效。");
            ValidateDemandName(archive.Name);
            ValidateStoredDemand(archive.Demand);
        }
        return data;
    }

    /// <summary>校验活动与归档目标，禁止空来源或无效时间进入后续判断。</summary>
    private static void ValidateStoredDemand(MaterialDemand? demand)
    {
        if (demand == null || string.IsNullOrWhiteSpace(demand.Revision) ||
            string.IsNullOrWhiteSpace(demand.Source) || demand.TargetQuantity < 0 ||
            demand.CreatedAt == default || demand.ConfirmedAt < demand.CreatedAt)
            throw new InvalidDataException("需求条目的目标、版本、来源或登记时间无效。");
    }

    /// <summary>新建或修改目标；修改目标开始新一轮需求，同值保存保留当前进度。</summary>
    public Task SetDemandAsync(string uid, string name, long targetQuantity, string? expectedRevision) => Locked(() =>
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || targetQuantity < 0)
            throw new ArgumentException("物品名称须为 1～128 字符，目标数量须为非负整数。");
        name = name.Trim();
        var data = ReadDemands(uid);
        var current = CheckDemandRevision(data, name, expectedRevision);
        if (current?.TargetQuantity == targetQuantity) return false;
        if (current == null && data.Items.Count >= MaxDemandItems)
            throw new InvalidOperationException("活动需求已达 5000 项，请先删除或归档已有需求。");
        // 与库存人工编辑一样登记共享来源名称。
        Sources();
        SaveSources();
        data.Items[name] = new MaterialDemand
        {
            TargetQuantity = targetQuantity
        };
        Write(Path.Combine(AccountDirectory(uid), "demands.json"), data);
        return true;
    });

    /// <summary>脚本批量登记绝对目标；同来源同值重报保留进度，其他修改开始新一轮需求。</summary>
    public Task<InventoryReportResult> ReportDemandsAsync(string uid, AccountRecordSource source,
        IReadOnlyDictionary<string, (long Quantity, string? Category, string? Comment)> targets) => Locked(() =>
    {
        source = ValidateSource(source);
        var normalized = new Dictionary<string, (long Quantity, string? Category, string? Comment)>(StringComparer.Ordinal);
        foreach (var (name, target) in targets)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 128 || target.Quantity < 0 || target.Quantity > 9007199254740991L ||
                !normalized.TryAdd(name.Trim(), target))
                throw new ArgumentException("需求名称须唯一且为 1～128 字符，目标须为非负安全整数。");
        }
        var data = ReadDemands(uid);
        var result = new InventoryReportResult();
        if (normalized.Count == 0) return result;
        var inventory = Load(uid, includeHistory: false, restoreBackup: false);
        var definitions = Definitions();
        var now = DateTimeOffset.UtcNow;
        // 仅错误或分类提醒需要写库存事件；写前沿用日期快照，普通目标登记不更新时间。
        var updatesInventory = normalized.Any(pair =>
            !definitions.ContainsKey(pair.Key) && pair.Value.Category?.Trim().Length is not (> 0 and <= 128) ||
            !string.IsNullOrEmpty(definitions.GetValueOrDefault(pair.Key)) && pair.Value.Category != null);
        if (updatesInventory) Snapshot(inventory, Settings());
        var changed = false;
        foreach (var (name, target) in normalized)
        {
            if (!data.Items.ContainsKey(name) && data.Items.Count >= MaxDemandItems)
            {
                result.Rejected[name] = "活动需求已达 5000 项，请先删除或归档已有需求。";
                continue;
            }
            var quantity = target.Quantity;
            var category = target.Category?.Trim();
            var known = definitions.TryGetValue(name, out var localCategory);
            var canSupplement = string.IsNullOrEmpty(localCategory) && category?.Length is > 0 and <= 128;
            // 与库存一致：未知名称必须附有效分类；拒绝目标时保留已有需求并记录库存错误。
            if (!known && !canSupplement)
            {
                const string message = "需求物品名称未收录且未提供有效分类，本次目标未采纳。请提供标准名称和分类，或确认添加到补充字典。";
                if (inventory.Errors.Count >= 5000 && !inventory.Errors.ContainsKey(name))
                    throw new InvalidDataException("待处理库存异常已达 5000 项，请先处理已有异常。");
                var repeated = inventory.Events.FirstOrDefault(item => !item.Archived && !item.ItemDeleted &&
                    item.Name == name && item.Source == source.Reference && item.State == "Error" && item.Message == message);
                var error = new InventoryError { Code = "UnknownName", Source = source.Reference, Message = message };
                if (repeated == null)
                {
                    var entry = AddEvent(inventory, name, source.Reference, "Report", "Error", 0, now, message);
                    entry.ErrorId = error.Id;
                }
                else
                {
                    if (repeated.RepeatCount < long.MaxValue) repeated.RepeatCount++;
                    repeated.LastReceivedAt = now;
                    error.Id = repeated.ErrorId;
                }
                inventory.Errors[name] = error;
                result.Rejected[name] = message;
                continue;
            }
            if (!string.IsNullOrEmpty(localCategory))
                CheckReportedCategories(inventory, name, source.Reference, [target.Category], localCategory, now, result);
            if (canSupplement)
                SupplementReportedDefinition(name, category!, [target.Comment], source.Reference, now, definitions);
            result.Accepted.Add(name);
            if (data.Items.TryGetValue(name, out var current) && current.TargetQuantity == quantity && current.Source == source.Reference)
                continue;
            data.Items[name] = new MaterialDemand
            {
                TargetQuantity = quantity,
                Source = source.Reference
            };
            changed = true;
        }
        // 来源名称与库存上报共享字典；重复目标也允许更新脚本显示名称。
        Sources()[source.Reference] = source;
        SaveSources();
        if (updatesInventory) Save(inventory);
        if (changed) Write(Path.Combine(AccountDirectory(uid), "demands.json"), data);
        return result;
    });

    /// <summary>独立保存需求页置顶，可置顶尚未创建目标的库存物品。</summary>
    public Task SetDemandPriorAsync(string uid, string name, bool prior) => Locked(() =>
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128) throw new ArgumentException("物品名称无效。");
        var data = ReadDemands(uid);
        if (prior) data.Prior.Add(name); else data.Prior.Remove(name);
        Write(Path.Combine(AccountDirectory(uid), "demands.json"), data);
        return true;
    });

    /// <summary>人工确认仅作用于需求，不伪造实测库存。</summary>
    public Task ConfirmDemandAsync(string uid, string name, string expectedRevision) => Locked(() =>
    {
        var data = ReadDemands(uid);
        var demand = CheckDemandRevision(data, name, expectedRevision) ?? throw new InvalidOperationException("需求已删除，请刷新。");
        EnsureDemandInventoryReady(Load(uid, includeHistory: false, restoreBackup: false), name);
        demand.ConfirmedAt = DateTimeOffset.UtcNow;
        demand.Revision = Guid.NewGuid().ToString("N");
        Write(Path.Combine(AccountDirectory(uid), "demands.json"), data);
        return true;
    });

    /// <summary>删除需求保留库存，下一次创建使用新的起始时间。</summary>
    public Task DeleteDemandAsync(string uid, string name, string expectedRevision) => Locked(() =>
    {
        var data = ReadDemands(uid);
        CheckDemandRevision(data, name, expectedRevision);
        data.Items.Remove(name);
        Write(Path.Combine(AccountDirectory(uid), "demands.json"), data);
        return true;
    });

    /// <summary>重新核实完成状态和版本，将所选需求一次性转入历史；不修改库存。</summary>
    public Task ArchiveDemandsAsync(string uid, IReadOnlyDictionary<string, string> expectedRevisions) => Locked(() =>
    {
        var data = ReadDemands(uid);
        var workspace = ReadWorkspace(uid, includeHistory: false);
        var archives = new List<MaterialDemandArchive>();
        var progressByName = MaterialDemandPolicy.EvaluateAll(workspace, data);
        // 先全部验证，防止批量操作只归档部分目标，或使用旧界面中的绿色状态。
        foreach (var (name, revision) in expectedRevisions)
        {
            var demand = CheckDemandRevision(data, name, revision) ?? throw new InvalidOperationException("需求已删除，请刷新。");
            EnsureDemandInventoryReady(workspace.Inventory, name);
            var progress = progressByName[name];
            if (progress.State != MaterialDemandState.Completed || progress.CompletedAt == null || progress.ConfirmationMethod == null)
                throw new InvalidOperationException("需求尚未完成或完成依据已变化，请刷新后重试。");
            archives.Add(new MaterialDemandArchive
            {
                Name = name, Demand = demand, Source = progress.Source,
                SourceDisplayName = workspace.Sources.TryGetValue(progress.Source, out var descriptor) ? descriptor.DisplayName : progress.Source,
                ConfirmationMethod = progress.ConfirmationMethod, CompletedAt = progress.CompletedAt.Value,
                CraftingReservePercent = progress.CraftingReservePercent,
                ArchivedAt = DateTimeOffset.UtcNow
            });
        }
        if (archives.Count == 0) return false;
        data.History.AddRange(archives);
        foreach (var archive in archives) data.Items.Remove(archive.Name);
        Write(Path.Combine(AccountDirectory(uid), "demands.json"), data);
        return true;
    });

    /// <summary>在写入前再次检查库存红灯，避免旧界面确认或归档被错误遮挡的需求。</summary>
    private static void EnsureDemandInventoryReady(InventoryDocument inventory, string name)
    {
        if (inventory.Errors.ContainsKey(name) || inventory.Events.Any(item => item.Name == name && !item.Archived && item.State == "Error"))
            throw new InvalidOperationException("库存存在错误，请先前往库存记录处理。");
    }

    /// <summary>所有人工操作在锁内核对需求版本，避免旧对话框覆盖新目标。</summary>
    private static MaterialDemand? CheckDemandRevision(MaterialDemandDocument data, string name, string? revision)
    {
        data.Items.TryGetValue(name, out var current);
        if (current?.Revision != revision) throw new InvalidOperationException("需求已发生变化，请刷新后重试。");
        return current;
    }
}

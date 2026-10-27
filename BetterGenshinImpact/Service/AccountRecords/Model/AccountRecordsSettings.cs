using System.Collections.Generic;

namespace BetterGenshinImpact.Service.AccountRecords.Model;

/// <summary>三个账号记录模块共用的来源和保存设置。</summary>
public sealed class AccountRecordsSettings
{
    /// <summary>每种材料最近来源数，含当前项，范围 1～10。</summary>
    public int RecentLimit { get; set; } = 5;
    /// <summary>是否自动生成日期快照。</summary>
    public bool SnapshotsEnabled { get; set; } = true;
    /// <summary>保留有快照的日期数量，范围 1～30。</summary>
    public int SnapshotLimit { get; set; } = 7;
    /// <summary>优先展示可读来源名称。</summary>
    public bool PreferReadableSource { get; set; } = true;
}

/// <summary>跨账号共享的来源描述，不保存临时执行目录。</summary>
public sealed class AccountRecordSource
{
    /// <summary>目录或内置任务标识，包含仓库前缀以区分同名目录。</summary>
    public string Reference { get; set; } = "";
    /// <summary>上报时的可读名称。</summary>
    public string DisplayName { get; set; } = "";
}

/// <summary>用户明确添加的字典补充；不修改模型资源。</summary>
public sealed class InventoryItemOverride
{
    /// <summary>用户确认的分类；原字典已有分类时不允许覆盖。</summary>
    public string Category { get; set; } = "";
}

/// <summary>一次读取所需的账号、来源和设置快照。</summary>
public sealed class InventoryWorkspace
{
    /// <summary>公共目录叠加用户补充后的名称和分类。</summary>
    public Dictionary<string, string> Definitions { get; set; } = [];
    /// <summary>账号库存与异常。</summary>
    public InventoryDocument Inventory { get; set; } = new();
    /// <summary>公共来源描述。</summary>
    public Dictionary<string, AccountRecordSource> Sources { get; set; } = [];
    /// <summary>存储及显示设置。</summary>
    public AccountRecordsSettings Settings { get; set; } = new();
}

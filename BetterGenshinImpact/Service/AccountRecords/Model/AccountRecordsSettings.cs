using System.Collections.Generic;
using System;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.AccountRecords.Model;

/// <summary>三个账号记录模块共用的来源和保存设置。</summary>
public sealed class AccountRecordsSettings
{
    /// <summary>允许用确认库存加有效增量直接判断需求达标；默认只用实测总量与人工确认。</summary>
    public bool UseDeltaForDemands { get; set; }
    /// <summary>可合成材料按培养指南的家族保留与逐级合成规则判断，默认开启。</summary>
    public bool UseFamiliesForDemands { get; set; } = true;
    /// <summary>合成收益预留百分比，沿用培养指南的 0～20 范围及默认 7。</summary>
    public int DemandCraftingReservePercent { get; set; } = 7;

    /// <summary>所有子表共享的图像缓存开关，关闭不修改或删除已有磁盘缓存。</summary>
    /// <remarks>
    /// 约定：开启时允许读取、显示和建立缓存；关闭时忽略外围程序建立或更新缓存的请求，数据上报仍正常处理。
    /// JS 上报与人工导入在调用缓存写入入口前检查此开关。
    /// 切换开关本身不删除文件；主动清理和写入时的容量淘汰分别由缓存服务负责。
    /// </remarks>
    public bool UseImageCache { get; set; } = true;
    /// <summary>后续写入的图标缓存边长，单位像素；不自动重采集已有缓存。</summary>
    public int IconCacheSize { get; set; } = 36;
    /// <summary>缺少物品图标时以显示行号代替方块占位，默认关闭。</summary>
    public bool ShowMissingIconRowNumbers { get; set; }
    /// <summary>是否显示库存到期时间列，默认隐藏，不因数据包含到期信息而自动开启。</summary>
    public bool ShowExpirationColumn { get; set; }
    /// <summary>仅需求表显示目标登记时间，默认隐藏。</summary>
    public bool ShowDemandCreatedAt { get; set; }
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

/// <summary>补充字典条目，包含初始值和用户添加的内容；不修改模型资源。</summary>
public sealed class InventoryItemOverride
{
    /// <summary>供开发者查看的说明，包含人工备注及补充来源，不参与业务判断。</summary>
    public string Comment { get; set; } = "";
    /// <summary>背包页覆写；省略或 null 回退 CSV，空字符串明确设为未分类。</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? Page { get; set; }
    /// <summary>补充分类；原字典已有非空分类时不允许覆盖。</summary>
    public string Category { get; set; } = "";
}

/// <summary>一次读取所需的账号、来源和设置快照。</summary>
public sealed class InventoryWorkspace
{
    /// <summary>读取时总表文件的修改时间，仅用于判断是否复用展示快照。</summary>
    [JsonIgnore]
    public DateTime UpdatedAt { get; set; }
    /// <summary>公共目录叠加用户补充后的名称和分类。</summary>
    public Dictionary<string, string> Definitions { get; set; } = [];
    /// <summary>公共目录叠加补充字典后的物品背包页。</summary>
    public Dictionary<string, string> Pages { get; set; } = [];
    /// <summary>账号库存与异常。</summary>
    public InventoryDocument Inventory { get; set; } = new();
    /// <summary>公共来源描述。</summary>
    public Dictionary<string, AccountRecordSource> Sources { get; set; } = [];
    /// <summary>存储及显示设置。</summary>
    public AccountRecordsSettings Settings { get; set; } = new();
}

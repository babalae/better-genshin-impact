using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 一份任务计划的元信息、任务树和入口配置。
/// </summary>
public sealed class PuloniaTaskPlan
{
    /// <summary>
    /// 当前支持的文件格式版本。
    /// </summary>
    /// <remarks>
    /// 版本 3 以共享设置（settings、profile_id）取代预设和分组公共参数；
    /// 版本 4 以单值运行账户设置 <c>account_id</c> 取代账号绑定列表 <c>accounts</c>，并删除触发器的 <c>account_id</c>。
    /// 开发期不转换也不读取旧版本，旧计划文件需要删除后重建。
    /// </remarks>
    public const int CurrentSchemaVersion = 4;

    /// <summary>
    /// 稳定计划 ID，同时作为存储文件名。
    /// </summary>
    [JsonProperty("id", Required = Required.Always)]
    public string Id { get; set; } = PuloniaId.NewPlanId();

    /// <summary>
    /// 文件格式版本，未知版本不能自动降级读取。
    /// </summary>
    [JsonProperty("schema_version", Required = Required.Always)]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>
    /// 内容修订号；新计划为 0，成功保存后递增。
    /// </summary>
    [JsonProperty("revision", Required = Required.Always)]
    public long Revision { get; set; }

    /// <summary>
    /// 计划显示名称，修改时不改变 ID。
    /// </summary>
    [JsonProperty("name", Required = Required.Always)]
    public string Name { get; set; } = "新计划";

    /// <summary>
    /// 用户填写的计划说明。
    /// </summary>
    [JsonProperty("description")]
    public string? Description { get; set; }

    /// <summary>
    /// 计划用途标记，仅用于界面归属分组，不参与执行逻辑；旧文件缺省时按普通计划读取。
    /// </summary>
    [JsonProperty("purpose", Required = Required.DisallowNull)]
    public PuloniaTaskPlanPurpose Purpose { get; set; } = PuloniaTaskPlanPurpose.General;

    /// <summary>
    /// 唯一根节点，类型固定为 group。
    /// </summary>
    [JsonProperty("root_task", Required = Required.Always)]
    public PuloniaTask RootTask { get; set; } = new() { Name = "根分组" };

    /// <summary>
    /// 计划内定时与热键配置，运行状态和调度游标不写入此处。
    /// </summary>
    [JsonProperty("triggers", Required = Required.DisallowNull)]
    public List<PuloniaTaskTrigger> Triggers { get; set; } = [];

    /// <summary>
    /// 运行账户设置：为空（默认）表示跟随实例当前账户，不切换；<c>acc_…</c> 表示指定账户，必要时先切换到该账户。
    /// 普通计划和一条龙都支持；只能指定填写了 UID 的账户，由运行准备检查。
    /// </summary>
    [JsonProperty("account_id")]
    public string? AccountId { get; set; }
}

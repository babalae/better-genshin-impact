using System.Collections.Generic;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>全局账户目录 <c>User/Pulonia/accounts.json</c>，至少有一个账户。</summary>
public sealed class PuloniaGenshinAccountSettings
{
    /// <summary>
    /// 当前支持的账户目录格式版本。版本 2 删除默认账号 <c>default</c> 与 <c>multi_account_enabled</c>，账户增加服务器时区；
    /// 开发期不转换旧格式，读取时不兼容；旧文件需要删除后重建。
    /// </summary>
    public const int CurrentSchemaVersion = 2;
    /// <summary>账户目录格式版本，缺失或不同时拒绝读取。</summary>
    [JsonProperty("schema_version", Required = Required.Always)]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    /// <summary>耐久保存的修订号，用于拒绝旧编辑副本覆盖。</summary>
    public long Revision { get; set; }
    /// <summary>账户目录，列表顺序就是界面显示顺序；至少有一个账户。</summary>
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<PuloniaGenshinAccountProfile> Accounts { get; set; } = [];

    /// <summary>
    /// 建立首次使用时的账户目录：只含“我的账户”。
    /// </summary>
    /// <param name="serverTimeZone">“我的账户”的服务器时区</param>
    /// <returns>修订号为 0 的新目录</returns>
    public static PuloniaGenshinAccountSettings CreateInitial(PuloniaTaskServerTimeZone serverTimeZone)
        => new() { Accounts = [PuloniaGenshinAccountProfile.CreateMyAccount(serverTimeZone)] };
}

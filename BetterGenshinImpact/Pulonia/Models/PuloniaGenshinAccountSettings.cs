using System.Collections.Generic;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>全局账号目录和可选的多账号界面设置。</summary>
public sealed class PuloniaGenshinAccountSettings
{
    /// <summary>耐久保存的修订号，用于拒绝旧编辑副本覆盖。</summary>
    public long Revision { get; set; }
    /// <summary>是否显示一条龙中的账号准备项；不改变已有计划绑定。</summary>
    public bool MultiAccountEnabled { get; set; }
    /// <summary>账号目录，默认账号始终存在。</summary>
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<PuloniaGenshinAccountProfile> Accounts { get; set; } = [PuloniaGenshinAccountProfile.CreateDefault()];
}

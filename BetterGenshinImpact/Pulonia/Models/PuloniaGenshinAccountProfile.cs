using System;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>原神账号的切换资料；登录密码只保存在 Windows 凭据管理器。</summary>
public sealed class PuloniaGenshinAccountProfile
{
    /// <summary>当前登录账号的固定别名，不代表某个已核验的 UID。</summary>
    public const string DefaultId = "default";
    /// <summary>供计划引用的稳定身份。</summary>
    public string Id { get; set; } = "account_" + Guid.NewGuid().ToString("N");
    /// <summary>用户可读名称。</summary>
    public string Name { get; set; } = "新账号";
    /// <summary>目标完整 UID；默认账号不指定目标。</summary>
    public string? Uid { get; set; }
    /// <summary>优先使用的登录方式。</summary>
    public PuloniaGenshinAccountLoginMode LoginMode { get; set; }
    /// <summary>账号图片相对于 User/Pulonia 的路径。</summary>
    public string? ImagePath { get; set; }
    /// <summary>固定到运行快照中的账号图片内容指纹。</summary>
    public string? ImageVersion { get; set; }
    /// <summary>登录列表显示的完整脱敏账号文字。</summary>
    public string ListText { get; set; } = "";
    /// <summary>Windows 凭据的引用，不包含用户名或密码。</summary>
    public string? CredentialReference { get; set; }
    /// <summary>是否表示当前登录账号。</summary>
    [JsonIgnore] public bool IsDefault => Id == DefaultId;
    /// <summary>建立免配置的当前账号。</summary>
    public static PuloniaGenshinAccountProfile CreateDefault() => new() { Id = DefaultId, Name = "默认账号（当前登录账号）" };
}

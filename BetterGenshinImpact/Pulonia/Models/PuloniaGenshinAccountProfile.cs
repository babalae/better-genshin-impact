namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// Pulonia 账户：一个原神游戏账号，以账户 ID 区分，显示为用户起的名称，带 UID 和服务器。
/// 登录资料（登录方式、图片、列表文字、凭据引用）可选，只在需要自动切换到该账户时填写；登录密码只保存在 Windows 凭据管理器。
/// </summary>
public sealed class PuloniaGenshinAccountProfile
{
    /// <summary>账户目录初始自带账户的名称；改名后它与普通账户没有区别。</summary>
    public const string MyAccountName = "我的账户";
    /// <summary>供计划和实例设置引用的稳定身份，形如 <c>acc_</c> + 16 位短 ID。</summary>
    public string Id { get; set; } = PuloniaId.NewAccountId();
    /// <summary>用户可读名称，必填且目录内不重复。</summary>
    public string Name { get; set; } = "新账户";
    /// <summary>完整 UID（9 到 10 位数字），目录内不重复；目录中最多一个账户可以不填。</summary>
    public string? Uid { get; set; }
    /// <summary>服务器时区，取值与触发器相同；新增账户时默认取通用配置中的服务器时区。</summary>
    public PuloniaTaskServerTimeZone ServerTimeZone { get; set; } = PuloniaTaskServerTimeZone.UtcPlus8;
    /// <summary>优先使用的登录方式。</summary>
    public PuloniaGenshinAccountLoginMode LoginMode { get; set; }
    /// <summary>
    /// 账户图片相对于 User/Pulonia 的路径。图片文件每次采集或导入都新建、不覆盖，
    /// 因此运行快照只需记录此路径即可固定图片内容；旧图片暂不清理。
    /// </summary>
    public string? ImagePath { get; set; }
    /// <summary>登录列表显示的完整脱敏账号文字。</summary>
    public string ListText { get; set; } = "";
    /// <summary>
    /// Windows 凭据的引用，不包含用户名或密码；目标名按账户固定，保存时覆盖同名凭据。
    /// 由账户存储确定：账户目录保存成功后才写入凭据，账户被移除或改用不使用密码的登录方式时清除引用并删除凭据。
    /// </summary>
    public string? CredentialReference { get; set; }

    /// <summary>
    /// 建立账户目录初始自带的“我的账户”：没有 UID，也没有登录资料。
    /// </summary>
    /// <param name="serverTimeZone">服务器时区，由调用方按通用配置决定</param>
    /// <returns>新的“我的账户”</returns>
    public static PuloniaGenshinAccountProfile CreateMyAccount(PuloniaTaskServerTimeZone serverTimeZone)
        => new() { Name = MyAccountName, ServerTimeZone = serverTimeZone };
}

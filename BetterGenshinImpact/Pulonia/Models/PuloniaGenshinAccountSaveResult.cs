namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 账号目录的保存结果。目录已经耐久保存；登录凭据在其后提交，写入失败时由 <see cref="CredentialError"/> 说明；
/// 其他模块按 UID 存放的数据（如狛荷屋记录）也在其后随 UID 改名或随账户删除，失败时由 <see cref="DataWarning"/> 说明。
/// </summary>
/// <param name="Settings">保存后的账号目录，包含新修订号和存储层确定的凭据引用</param>
/// <param name="CredentialError">登录凭据写入失败的说明；为空表示本次填写的凭据全部已提交</param>
/// <param name="DataWarning">关联数据改名或删除失败的说明；为空表示全部完成。目录不因此回滚</param>
public sealed record PuloniaGenshinAccountSaveResult(PuloniaGenshinAccountSettings Settings, string? CredentialError,
    string? DataWarning = null);

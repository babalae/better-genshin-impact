namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 账号目录的保存结果。目录已经耐久保存；登录凭据在其后提交，写入失败时由 <see cref="CredentialError"/> 说明。
/// </summary>
/// <param name="Settings">保存后的账号目录，包含新修订号和存储层确定的凭据引用</param>
/// <param name="CredentialError">登录凭据写入失败的说明；为空表示本次填写的凭据全部已提交</param>
public sealed record PuloniaGenshinAccountSaveResult(PuloniaGenshinAccountSettings Settings, string? CredentialError);

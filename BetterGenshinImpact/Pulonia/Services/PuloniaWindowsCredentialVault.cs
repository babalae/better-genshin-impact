using BetterGenshinImpact.Helpers.Win32;
using Meziantou.Framework.Win32;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>使用 Windows 凭据管理器保存 Pulonia 原神账号的登录凭据。</summary>
internal sealed class PuloniaWindowsCredentialVault : IPuloniaCredentialVault
{
    /// <summary>凭据管理器中显示的备注。</summary>
    private const string Comment = "Pulonia 原神登录资料";

    /// <inheritdoc />
    public void Write(string target, string userName, string password)
        => CredentialManagerHelper.SaveCredential(target, userName, password, Comment, CredentialPersistence.LocalMachine);

    /// <inheritdoc />
    public void Delete(string target) => CredentialManagerHelper.DeleteCredential(target);
}

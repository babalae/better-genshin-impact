using System;
using Meziantou.Framework.Win32;

namespace BetterGenshinImpact.Helpers.Win32;

public static class CredentialManagerHelper
{
    public static void SaveCredential(string applicationName, string userName, string secret, string comment,
        CredentialPersistence persistence)
    {
        CredentialManager.WriteCredential(
            applicationName: applicationName,
            userName: userName,
            secret: secret,
            comment: comment,
            persistence: persistence);
    }

    public static Credential? ReadCredential(string applicationName)
    {
        var credential = CredentialManager.ReadCredential(applicationName);
        if (credential == null)
        {
            Console.WriteLine("No credential found.");
            return null;
        }

        return credential;
    }

    public static void UpdateCredential(string applicationName, string newUserName, string newSecret, string newComment)
    {
        SaveCredential(applicationName, newUserName, newSecret, newComment, CredentialPersistence.LocalMachine);
    }

    /// <summary>
    /// 删除 Windows 为 RDP 服务器保存的凭据（凭据管理器里的 <c>TERMSRV/&lt;主机&gt;</c>）。
    ///
    /// 这类凭据由「远程桌面连接」保存，类型是 <c>DomainPassword</c>——按默认的
    /// <c>Generic</c> 删不掉，所以两种类型都试。不删的话，即使关掉自动登录、
    /// 清掉本程序保存的密码，RDP 仍会用这份缓存凭据静默登录。
    /// </summary>
    /// <returns>删除后凭据是否确实不存在。</returns>
    public static bool DeleteRdpServerCredential(string server)
    {
        var target = $"TERMSRV/{server}";

        foreach (var type in new[] { CredentialType.DomainPassword, CredentialType.Generic })
        {
            try
            {
                CredentialManager.DeleteCredential(target, type);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"删除 RDP 凭据 {target}（{type}）失败：{ex.Message}");
            }
        }

        try
        {
            return CredentialManager.ReadCredential(target) is null;
        }
        catch (Exception)
        {
            // 读不出来不能断言「已删除」，由调用方决定是否继续。
            return false;
        }
    }

    public static void DeleteCredential(string applicationName)
    {
        try
        {
            CredentialManager.DeleteCredential(applicationName);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting credential: {ex.Message}");
        }
    }
}
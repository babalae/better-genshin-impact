using BetterGenshinImpact.Helpers.Win32;
using Meziantou.Framework.Win32;

namespace BetterGenshinImpact.Service.ChildSession;

internal static class ChildSessionCredentialStore
{
    private const string ApplicationName = "BetterGenshinImpact.ChildSessionCredentials";
    private const string Comment = "Windows credentials for BetterGI Child Session RDP";

    public static (string UserName, string Password)? TryLoad()
    {
        var credential = CredentialManagerHelper.ReadCredential(ApplicationName);
        if (credential is null
            || string.IsNullOrWhiteSpace(credential.UserName)
            || string.IsNullOrEmpty(credential.Password))
        {
            return null;
        }

        return (credential.UserName, credential.Password);
    }

    public static void Save(string userName, string password)
    {
        CredentialManagerHelper.SaveCredential(
            ApplicationName,
            userName,
            password,
            Comment,
            CredentialPersistence.LocalMachine);
    }

    public static void Delete()
    {
        CredentialManagerHelper.DeleteCredential(ApplicationName);
    }
}

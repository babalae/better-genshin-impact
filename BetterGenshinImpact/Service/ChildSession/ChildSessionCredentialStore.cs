using Meziantou.Framework.Win32;

namespace BetterGenshinImpact.Service.ChildSession;

internal sealed record ChildSessionCredential(string UserName, string Password);

internal static class ChildSessionCredentialStore
{
    internal const string ApplicationName = "BetterGenshinImpact.ChildSession";

    internal static ChildSessionCredential? TryRead()
    {
        var credential = CredentialManager.ReadCredential(ApplicationName);
        return credential is null
               || string.IsNullOrWhiteSpace(credential.UserName)
               || string.IsNullOrEmpty(credential.Password)
            ? null
            : new ChildSessionCredential(credential.UserName, credential.Password);
    }
}

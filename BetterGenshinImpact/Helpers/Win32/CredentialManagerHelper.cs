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

        Console.WriteLine($"UserName: {credential.UserName}");
        Console.WriteLine($"Secret: {credential.Password}");
        Console.WriteLine($"Comment: {credential.Comment}");

        return credential;
    }

    public static void UpdateCredential(string applicationName, string newUserName, string newSecret, string newComment)
    {
        SaveCredential(applicationName, newUserName, newSecret, newComment, CredentialPersistence.LocalMachine);
    }

    public static void DeleteCredential(string applicationName)
    {
        try
        {
            CredentialManager.DeleteCredential(applicationName);
            Console.WriteLine("Credential deleted successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting credential: {ex.Message}");
        }
    }
}
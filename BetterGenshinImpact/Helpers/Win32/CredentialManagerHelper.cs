using System;
using System.Text;
using Windows.System;
using BetterGenshinImpact.View.Windows;
using Meziantou.Framework.Win32;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.Helpers.Win32;

public static class CredentialManagerHelper
{
    public static readonly string MirrorChyanCdk = "KachinaInstaller_MirrorChyanCDK_BetterGI";
    
    public static void SaveCredential(string applicationName, string userName, string secret, string comment, CredentialPersistence persistence)
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

    public static string? GetAndSaveMirrorChyanCdk()
    {
        var credential = ReadCredential(MirrorChyanCdk);
        if (credential == null || credential.Password == null)
        {
            var cdk = PromptDialog.Prompt("Mirror酱是独立的第三方软件下载平台，提供付费的软件下载加速服务。\n如果你有 Mirror酱的 CDK，可以在这里输入。", 
                "请输入Mirror酱CDK",
                string.Empty,
                new PromptDialogConfig
                {
                    ShowLeftButton = true,
                    LeftButtonText = "获取CDK",
                    LeftButtonClick = (sender, args) =>
                    {
                         Launcher.LaunchUriAsync(new Uri("https://mirrorchyan.com/zh/get-start"));
                    }
                }
                );
            if (string.IsNullOrEmpty(cdk))
            {
                Toast.Warning("输入CDK为空，无法继续操作");
                return null;
            }
            SaveCredential(
                MirrorChyanCdk,
                string.Empty,
                cdk,
                string.Empty,
                CredentialPersistence.LocalMachine);
            return cdk;
        }
        else
        {
            return credential.Password;
        }
    }
}
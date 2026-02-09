using System;
using Windows.System;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.View.Windows;
using Meziantou.Framework.Win32;
using Wpf.Ui.Violeta.Controls;
using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.Helpers.Win32;

public static class MirrorChyanHelper
{
    public static readonly string MirrorChyanCdkAppName = "KachinaInstaller_MirrorChyanCDK_BetterGI";


    public static string? GetCdk()
    {
        var credential = CredentialManagerHelper.ReadCredential(MirrorChyanCdkAppName);
        return credential?.Password;
    }
    
    public static string? GetAndPromptCdk()
    {
        var credential = CredentialManagerHelper.ReadCredential(MirrorChyanCdkAppName);
        if (credential == null || credential.Password == null)
        {
            var cdk = PromptDialog.Prompt(Lang.S["Gen_11921_122813"],
                Lang.S["Gen_11923_9900ea"],
                string.Empty,
                new PromptDialogConfig
                {
                    ShowLeftButton = true,
                    LeftButtonText = Lang.S["Gen_11919_3b3340"],
                    LeftButtonClick = (sender, args) =>
                    {
                        OpenMirrorChyanWebsite();
                    }
                }
            );
            if (string.IsNullOrEmpty(cdk))
            {
                Toast.Warning(Lang.S["Gen_11922_ef3dd7"]);
                return null;
            }

            CredentialManagerHelper.SaveCredential(
                MirrorChyanCdkAppName,
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
    
    public static void EditCdk()
    {
        var credential = CredentialManagerHelper.ReadCredential(MirrorChyanCdkAppName);
        var cdk = PromptDialog.Prompt(Lang.S["Gen_11921_122813"],
            Lang.S["Gen_11920_56c5ca"],
            credential?.Password!,
            new PromptDialogConfig
            {
                ShowLeftButton = true,
                LeftButtonText = Lang.S["Gen_11919_3b3340"],
                LeftButtonClick = (sender, args) =>
                {
                    OpenMirrorChyanWebsite();
                }
            }
        );
        if (string.IsNullOrEmpty(cdk))
        {
            DeleteCdk();
        }
        else
        {
            CredentialManagerHelper.SaveCredential(
                MirrorChyanCdkAppName,
                string.Empty,
                cdk,
                string.Empty,
                CredentialPersistence.LocalMachine);
        }
    }

    public static void DeleteCdk()
    {
        CredentialManagerHelper.DeleteCredential(MirrorChyanCdkAppName);
    }
    
    
    private static void OpenMirrorChyanWebsite()
    {
        Launcher.LaunchUriAsync(new Uri($"https://mirrorchyan.com/zh/get-start?source=bgi-{Global.Version}"));
    }
}
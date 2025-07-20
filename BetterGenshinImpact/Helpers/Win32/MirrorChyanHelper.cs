using System;
using Windows.System;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.View.Windows;
using Meziantou.Framework.Win32;
using Wpf.Ui.Violeta.Controls;

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
            var cdk = PromptDialog.Prompt("Mirror酱是独立的第三方软件下载平台，提供付费的软件下载加速服务。\n如果你有 Mirror酱的 CDK，可以在这里输入。",
                "请输入Mirror酱CDK",
                string.Empty,
                new PromptDialogConfig
                {
                    ShowLeftButton = true,
                    LeftButtonText = "获取CDK",
                    LeftButtonClick = (sender, args) =>
                    {
                        OpenMirrorChyanWebsite();
                    }
                }
            );
            if (string.IsNullOrEmpty(cdk))
            {
                Toast.Warning("输入CDK为空，无法继续操作");
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
        var cdk = PromptDialog.Prompt("Mirror酱是独立的第三方软件下载平台，提供付费的软件下载加速服务。\n如果你有 Mirror酱的 CDK，可以在这里输入。",
            "修改Mirror酱CDK",
            credential?.Password!,
            new PromptDialogConfig
            {
                ShowLeftButton = true,
                LeftButtonText = "获取CDK",
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
        Launcher.LaunchUriAsync(new Uri($"https://mirrorchyan.com/zh/get-start?source=bgi-desktop-{Global.Version}"));
    }
}
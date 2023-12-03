using SharpCompress.Common;
using SharpCompress.Readers;
using System;
using System.IO;
using System.Text;

namespace MicaSetup.Helper.Helper;

public static class InstallHelper
{
    public static void Install(Stream archiveStream, Action<double, string> progressCallback = null!)
    {
        if (Option.Current.IsInstallCertificate)
        {
            try
            {
                byte[] cer = ResourceHelper.GetBytes("pack://application:,,,/MicaSetup;component/Resources/Setups/publish.cer");
                CertificateHelper.Install(cer);
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        if (Option.Current.IsCreateDesktopShortcut)
        {
            try
            {
                ShortcutHelper.CreateShortcutOnDesktop(Option.Current.DisplayName, Path.Combine(Option.Current.InstallLocation, Option.Current.ExeName));
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        if (!Directory.Exists(Option.Current.InstallLocation))
        {
            _ = Directory.CreateDirectory(Option.Current.InstallLocation);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(Option.Current.OverlayInstallRemoveExt))
            {
                string[] extFilters = Option.Current.OverlayInstallRemoveExt.Split(',');

                foreach (string subDir in Directory.GetDirectories(Option.Current.InstallLocation))
                {
                    foreach (string file in Directory.GetFiles(subDir, "*.*", SearchOption.AllDirectories))
                    {
                        FileInfo fileInfo = new(file);

                        foreach (string extFilter in extFilters)
                        {
                            string ext = extFilter;
                            if (ext.StartsWith("."))
                            {
                                ext = ext.Substring(1);
                            }
                            if (fileInfo.Extension.ToLower() == ext)
                            {
                                try
                                {
                                    File.Delete(file);
                                }
                                catch (Exception e)
                                {
                                    Logger.Error(e);
                                }
                                break;
                            }
                        }
                    }
                }
            }
        }

        ReaderOptions readerOptions = new()
        {
            LookForHeader = true,
            Password = string.IsNullOrEmpty(Option.Current.UnpackingPassword) ? null! : Option.Current.UnpackingPassword,
        };

        ExtractionOptions extractionOptions = new()
        {
            ExtractFullPath = true,
            Overwrite = true,
            PreserveAttributes = false,
            PreserveFileTime = true,
        };

        StringBuilder uninstallData = new();
        ArchiveFileHelper.ExtractAll(Option.Current.InstallLocation, archiveStream, (double progress, string key) =>
        {
            Logger.Debug($"[ExtractAll] {key} {progress * 100d:0.00}%");
            progressCallback?.Invoke(progress, key);
            uninstallData.Append(key);
            uninstallData.Append('|');
        }, readerOptions: readerOptions, options: extractionOptions);

        if (Option.Current.IsCreateRegistryKeys)
        {
            UninstallInfo info = new()
            {
                KeyName = Option.Current.KeyName,
                DisplayName = Option.Current.DisplayName,
                DisplayVersion = Option.Current.DisplayVersion,
                InstallLocation = Option.Current.InstallLocation,
                Publisher = Option.Current.Publisher,
                UninstallString = Option.Current.UninstallString,
                SystemComponent = Option.Current.SystemComponent,
            };

            if (string.IsNullOrWhiteSpace(Option.Current.DisplayIcon))
            {
                info.DisplayIcon = Path.Combine(Option.Current.InstallLocation, Option.Current.ExeName);
            }
            else
            {
                info.DisplayIcon = Path.Combine(Option.Current.InstallLocation, Option.Current.DisplayIcon);
            }
            info.UninstallString ??= Path.Combine(Option.Current.InstallLocation, "Uninst.exe");
            info.UninstallData = uninstallData.ToString();

            try
            {
                RegistyUninstallHelper.Write(info);
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        try
        {
            RegistyAutoRunHelper.SetEnabled(Option.Current.IsCreateAsAutoRun, Option.Current.KeyName, $"{Path.Combine(Option.Current.InstallLocation, Option.Current.ExeName)} {Option.Current.AutoRunLaunchCommand}");
        }
        catch (Exception e)
        {
            Logger.Error(e);
        }

        if (Option.Current.IsCreateQuickLaunch)
        {
            try
            {
                ShortcutHelper.CreateShortcutOnQuickLaunch(Option.Current.DisplayName, Path.Combine(Option.Current.InstallLocation, Option.Current.ExeName));
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        if (Option.Current.IsCreateStartMenu)
        {
            try
            {
                StartMenuHelper.CreateStartMenuFolder(Option.Current.DisplayName, Path.Combine(Option.Current.InstallLocation, Option.Current.ExeName), Option.Current.IsCreateUninst);
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        try
        {
            StartMenuHelper.AddToRecent(Path.Combine(Option.Current.InstallLocation, Option.Current.ExeName));
        }
        catch (Exception e)
        {
            Logger.Error(e);
        }
    }

    public static void CreateUninst(Stream uninstStream)
    {
        if (Option.Current.IsCreateUninst)
        {
            try
            {
                using FileStream fileStream = new(Path.Combine(Option.Current.InstallLocation, "Uninst.exe"), FileMode.Create);
                uninstStream.Seek(0, SeekOrigin.Begin);
                uninstStream.CopyTo(fileStream);
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }
    }
}

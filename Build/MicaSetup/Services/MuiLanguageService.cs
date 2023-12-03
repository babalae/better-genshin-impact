using MicaSetup.Helper;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows.Media;

namespace MicaSetup.Services;

#pragma warning disable IDE0002

public class MuiLanguageService : IMuiLanguageService
{
    public FontFamily FontFamily { get; set; } = null!;

    static MuiLanguageService()
    {
        DebugPrintPrivate();
    }

    public FontFamily GetFontFamily()
    {
        if (FontFamily == null)
        {
            static string GetUriString(string name = null!) => $"pack://application:,,,/MicaSetup;component/Resources/Fonts/{name ?? string.Empty}";

            if (FontSelector.Count > 0)
            {
                MuiLanguageFont font = MuiLanguage.FontSelector.Where(f => f.Name == CultureInfo.CurrentUICulture.Name).ToList().FirstOrDefault()
                    ?? MuiLanguage.FontSelector.Where(f => f.ThreeName == CultureInfo.CurrentUICulture.ThreeLetterISOLanguageName).ToList().FirstOrDefault()
                    ?? MuiLanguage.FontSelector.Where(f => f.TwoName == CultureInfo.CurrentUICulture.TwoLetterISOLanguageName).ToList().FirstOrDefault()
                    ?? MuiLanguage.FontSelector.Where(f => f.Name == null && f.TwoName == null && f.ThreeName == null).ToList().FirstOrDefault();

                if (font != null)
                {
                    if (!string.IsNullOrWhiteSpace(font.ResourceFamilyName))
                    {
                        if (ResourceHelper.HasResource(GetUriString(font.ResourceFontFileName!)))
                        {
                            FontFamily = new FontFamily(new Uri(GetUriString()), font.ResourceFamilyName);
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(font.SystemFamilyName))
                    {
                        if (SystemFontHelper.HasFontFamily(font.SystemFamilyName!))
                        {
                            FontFamily = new FontFamily(font.SystemFamilyName);
                        }
                        else if (SystemFontHelper.HasFontFamily(font.SystemFamilyNameBackup!))
                        {
                            FontFamily = new FontFamily(font.SystemFamilyNameBackup);
                        }
                    }
                }
            }
            else
            {
                if (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja")
                {
                    FontFamily = new FontFamily("Yu Gothic UI");
                }
                else
                {
                    if (ResourceHelper.HasResource(GetUriString("HarmonyOS_Sans_SC_Regular.ttf")))
                    {
                        FontFamily = new FontFamily(new Uri(GetUriString()), "./HarmonyOS_Sans_SC_Regular.ttf#HarmonyOS Sans SC");
                    }
                }
            }
        }
        return FontFamily ??= new FontFamily();
    }

    public string GetXamlUriString()
    {
        static string GetUriString(string name) => $"pack://application:,,,/MicaSetup;component/Resources/Languages/{name}.xaml";

        if (ResourceHelper.HasResource(GetUriString(CultureInfo.CurrentUICulture.Name)))
        {
            return GetUriString(CultureInfo.CurrentUICulture.Name);
        }
        else
        {
            if (ResourceHelper.HasResource(GetUriString(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)))
            {
                return GetUriString(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
            }
            else
            {
                if (ResourceHelper.HasResource(GetUriString(CultureInfo.CurrentUICulture.ThreeLetterISOLanguageName)))
                {
                    return GetUriString(CultureInfo.CurrentUICulture.ThreeLetterISOLanguageName);
                }
            }
        }

        Logger.Debug($"[MuiLanguageService] NotFound with match mui lang name of '{CultureInfo.CurrentUICulture.Name}' or '{CultureInfo.CurrentUICulture.TwoLetterISOLanguageName}' or '{CultureInfo.CurrentUICulture.ThreeLetterISOLanguageName}'.");
        return GetUriString("en");
    }

    public string GetLicenseUriString()
    {
        static string GetUriString(string name) => $"pack://application:,,,/MicaSetup;component/Resources/Licenses/license.{name}.txt";

        if (ResourceHelper.HasResource(GetUriString(CultureInfo.CurrentUICulture.Name)))
        {
            return GetUriString(CultureInfo.CurrentUICulture.Name);
        }
        else
        {
            if (ResourceHelper.HasResource(GetUriString(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)))
            {
                return GetUriString(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
            }
            else
            {
                if (ResourceHelper.HasResource(GetUriString(CultureInfo.CurrentUICulture.ThreeLetterISOLanguageName)))
                {
                    return GetUriString(CultureInfo.CurrentUICulture.ThreeLetterISOLanguageName);
                }
            }
        }
        Logger.Debug($"[MuiLanguageService] NotFound with match mui license name of '{CultureInfo.CurrentUICulture.Name}' or '{CultureInfo.CurrentUICulture.TwoLetterISOLanguageName}' or '{CultureInfo.CurrentUICulture.ThreeLetterISOLanguageName}'.");
        return GetUriString("en");
    }

    [Conditional("DEBUG")]
    private static void DebugPrintPrivate()
    {
        CultureInfo[] cultures = CultureInfo.GetCultures(CultureTypes.AllCultures);
        Logger.Debug("Display Name", "Name", "TwoName", "ThreeName", "IetfTag");

        foreach (var culture in cultures)
        {
            if ((culture.CultureTypes & CultureTypes.UserCustomCulture) == CultureTypes.UserCustomCulture)
            {
                continue;
            }
            Logger.Debug(culture.DisplayName, culture.Name, culture.TwoLetterISOLanguageName, culture.ThreeLetterISOLanguageName, culture.IetfLanguageTag);
            if (culture.DisplayName.Contains("__EDITME__"))
            {
                Debugger.Break();
            }
        }
    }
}

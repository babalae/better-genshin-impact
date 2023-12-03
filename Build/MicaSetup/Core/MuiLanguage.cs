using MicaSetup.Helper;
using MicaSetup.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Baml2006;
using System.Xaml;

namespace MicaSetup.Core;

public static class MuiLanguage
{
    /// <summary>
    /// https://learn.microsoft.com/en-us/typography/fonts/windows_11_font_list
    /// https://learn.microsoft.com/en-us/typography/fonts/windows_10_font_list
    /// https://learn.microsoft.com/en-us/typography/fonts/windows_81_font_list
    /// https://learn.microsoft.com/en-us/typography/fonts/windows_8_font_list
    /// https://learn.microsoft.com/en-us/typography/fonts/windows_7_font_list
    /// </summary>
    public static List<MuiLanguageFont> FontSelector { get; } = new();

    public static void SetupLanguage()
    {
        _ = SetLanguage();
    }

    public static bool SetLanguage() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
    {
        "zh" => SetLanguage("zh"),
        "ja" => SetLanguage("ja"),
        "en" or _ => SetLanguage("en"),
    };

    public static bool SetLanguage(string name = "en")
    {
        if (Application.Current == null)
        {
            return false;
        }

        try
        {
            foreach (ResourceDictionary dictionary in Application.Current.Resources.MergedDictionaries)
            {
                if (dictionary.Source != null && dictionary.Source.OriginalString.Equals($"/Resources/Languages/{name}.xaml", StringComparison.Ordinal))
                {
                    Application.Current.Resources.MergedDictionaries.Remove(dictionary);
                    Application.Current.Resources.MergedDictionaries.Add(dictionary);
                    return true;
                }
            }
        }
        catch (Exception e)
        {
            _ = e;
        }
        return false;
    }

    public static string Mui(string key)
    {
        try
        {
            if (Application.Current == null)
            {
                return MuiBaml(key);
            }
            if (Application.Current!.FindResource(key) is string value)
            {
                return value;
            }
        }
        catch (Exception e)
        {
            _ = e;
        }
        return null!;
    }

    public static string Mui(string key, params object[] args)
    {
        return string.Format(Mui(key)?.ToString(), args);
    }

    private static string MuiBaml(string key)
    {
        try
        {
            using Stream resourceXaml = ResourceHelper.GetStream(new MuiLanguageService().GetXamlUriString());
            if (BamlHelper.LoadBaml(resourceXaml) is ResourceDictionary resourceDictionary)
            {
                return (resourceDictionary[key] as string)!;
            }
        }
        catch (Exception e)
        {
            _ = e;
        }
        return null!;
    }
}

file static class BamlHelper
{
    public static object LoadBaml(Stream stream)
    {
        using Baml2006Reader reader = new(stream);
        using XamlObjectWriter writer = new(reader.SchemaContext);

        while (reader.Read())
        {
            writer.WriteNode(reader);
        }
        return writer.Result;
    }
}

public class MuiLanguageFont
{
    public string? Name { get; set; }
    public string? TwoName { get; set; }
    public string? ThreeName { get; set; }

    public string? ResourceFontFileName { get; set; }
    public string? ResourceFontFamilyName { get; set; }
    public string? ResourceFamilyName => !string.IsNullOrWhiteSpace(ResourceFontFileName) && !string.IsNullOrWhiteSpace(ResourceFontFamilyName) ? $"./{ResourceFontFileName}#{ResourceFontFamilyName}" : null!;

    public string? SystemFamilyName { get; set; }
    public string? SystemFamilyNameBackup { get; set; }
}

public static class MuiLanguageFontExtension
{
    public static MuiLanguageFont OnNameOf(this MuiLanguageFont self, string name)
    {
        self.Name = name ?? throw new ArgumentNullException(nameof(name));
        self.TwoName = null!;
        self.ThreeName = null!;
        return self;
    }

    public static MuiLanguageFont OnTwoNameOf(this MuiLanguageFont self, string twoName)
    {
        self.Name = null!;
        self.TwoName = twoName ?? throw new ArgumentNullException(nameof(twoName));
        self.ThreeName = null!;
        return self;
    }

    public static MuiLanguageFont OnThreeNameOf(this MuiLanguageFont self, string threeName)
    {
        self.Name = null!;
        self.TwoName = null!;
        self.ThreeName = threeName ?? throw new ArgumentNullException(nameof(threeName));
        return self;
    }

    public static MuiLanguageFont OnAnyName(this MuiLanguageFont self)
    {
        self.Name = null!;
        self.TwoName = null!;
        self.ThreeName = null!;
        return self;
    }

    public static MuiLanguageFont ForResourceFont(this MuiLanguageFont self, string fontFileName, string familyName)
    {
        self.ResourceFontFileName = fontFileName ?? throw new ArgumentNullException(nameof(fontFileName));
        self.ResourceFontFamilyName = familyName ?? throw new ArgumentNullException(nameof(familyName));
        return self;
    }

    public static MuiLanguageFont ForSystemFont(this MuiLanguageFont self, string familyName, string familyNameBackup = null!)
    {
        self.SystemFamilyName = familyName ?? throw new ArgumentNullException(nameof(familyName));
        self.SystemFamilyNameBackup = familyNameBackup;
        _ = !new Regex("^[a-zA-Z ]+$").IsMatch(familyName) ? throw new ArgumentException(nameof(familyName)) : default(object);
        return self;
    }
}

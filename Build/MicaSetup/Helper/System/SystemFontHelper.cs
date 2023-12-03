using MicaSetup.Natives;
using System.Drawing;
using System.Drawing.Text;

namespace MicaSetup.Helper;

public static class SystemFontHelper
{
    public static bool HasFontFamily(string familyName, int? language = null!)
    {
        language ??= NativeMethods.MakeLangId(PrimaryLanguageID.LANG_ENGLISH, SublanguageID.SUBLANG_ENGLISH_US);

        InstalledFontCollection installedFonts = new();

        foreach (FontFamily fontFamily in installedFonts.Families)
        {
            if (fontFamily.GetName(language!.Value) == familyName)
            {
                return true;
            }
        }
        return false;
    }
}

using System.Windows.Media;

namespace MicaSetup.Services;

public interface IMuiLanguageService
{
    public FontFamily GetFontFamily();

    public string GetXamlUriString();

    public string GetLicenseUriString();
}

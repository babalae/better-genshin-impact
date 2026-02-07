using System.Globalization;

namespace BetterGenshinImpact.Service.Interface;

public enum MissingTextSource
{
    Log,
    UiStaticLiteral,
    UiDynamicBinding,
    Unknown
}

public interface ITranslationService
{
    string Translate(string text);
    string Translate(string text, MissingTextSource source);
    CultureInfo GetCurrentCulture();
}


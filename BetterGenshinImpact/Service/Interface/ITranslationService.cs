using System.Globalization;

namespace BetterGenshinImpact.Service.Interface;

public enum MissingTextSource
{
    Log,
    UiStaticLiteral,
    UiDynamicBinding,
    Unknown
}

public sealed class TranslationSourceInfo
{
    public MissingTextSource Source { get; set; } = MissingTextSource.Unknown;
    public string? ViewXamlPath { get; set; }
    public string? ViewType { get; set; }
    public string? ElementType { get; set; }
    public string? ElementName { get; set; }
    public string? PropertyName { get; set; }
    public string? BindingPath { get; set; }
    public string? Notes { get; set; }

    public static TranslationSourceInfo From(MissingTextSource source)
    {
        return new TranslationSourceInfo
        {
            Source = source
        };
    }
}

public interface ITranslationService
{
    string Translate(string text);
    string Translate(string text, TranslationSourceInfo sourceInfo);
    CultureInfo GetCurrentCulture();
    void Reload();
}


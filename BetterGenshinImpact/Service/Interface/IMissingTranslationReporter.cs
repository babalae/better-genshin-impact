namespace BetterGenshinImpact.Service.Interface;

public interface IMissingTranslationReporter
{
    bool TryEnqueue(string language, string key, TranslationSourceInfo sourceInfo);
}

namespace BetterGenshinImpact.Genshin.Settings;

public class LanguageSettings
{
    public TextLanguage TextLang { get; protected set; }
    public VoiceLanguage VoiceLang { get; protected set; }

    public LanguageSettings(MainJson data)
    {
        Load(data);
    }

    public void Load(MainJson data)
    {
        TextLang = (TextLanguage)data.DeviceLanguageType;
        VoiceLang = (VoiceLanguage)data.DeviceVoiceLanguageType;
    }
}

public enum VoiceLanguage
{
    Chinese,
    English,
    Japanese,
    Korean,
}

public enum TextLanguage
{
    None,
    English,
    SimplifiedChinese,
    TraditionalChinese,
    French,
    German,
    Spanish,
    Portugese,
    Russian,
    Japanese,
    Korean,
    Thai,
    Vietnamese,
    Indonesian,
}

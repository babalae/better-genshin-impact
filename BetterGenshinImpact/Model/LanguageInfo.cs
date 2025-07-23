using System;

namespace BetterGenshinImpact.Model;

/// <summary>
/// Represents information about a language supported by the application
/// </summary>
public class LanguageInfo
{
    /// <summary>
    /// Language code (e.g., "en-US", "zh-CN")
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Display name in English (e.g., "English", "Chinese")
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Native name in the language itself (e.g., "English", "中文")
    /// </summary>
    public string NativeName { get; set; } = string.Empty;

    /// <summary>
    /// Path to the language file
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Version of the language file
    /// </summary>
    public string Version { get; set; } = "1.0.0";

    public override string ToString()
    {
        return $"{NativeName} ({DisplayName})";
    }

    public override bool Equals(object? obj)
    {
        return obj is LanguageInfo other && Code.Equals(other.Code, StringComparison.OrdinalIgnoreCase);
    }

    public override int GetHashCode()
    {
        return Code.GetHashCode(StringComparison.OrdinalIgnoreCase);
    }
}
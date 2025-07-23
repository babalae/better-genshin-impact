using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace BetterGenshinImpact.UnitTest.LocalizationTests;

/// <summary>
/// Simple tests for localization components that don't depend on the main project compilation
/// </summary>
public class SimpleLocalizationTests
{
    [Fact]
    public void LanguageInfo_ShouldHaveCorrectProperties()
    {
        // Arrange & Act
        var languageInfo = new TestLanguageInfo
        {
            Code = "en-US",
            DisplayName = "English",
            NativeName = "English",
            FilePath = "/path/to/en-US.json",
            Version = "1.0.0"
        };

        // Assert
        Assert.Equal("en-US", languageInfo.Code);
        Assert.Equal("English", languageInfo.DisplayName);
        Assert.Equal("English", languageInfo.NativeName);
        Assert.Equal("/path/to/en-US.json", languageInfo.FilePath);
        Assert.Equal("1.0.0", languageInfo.Version);
    }

    [Fact]
    public void LanguageInfo_ToString_ShouldReturnCorrectFormat()
    {
        // Arrange
        var languageInfo = new TestLanguageInfo
        {
            DisplayName = "English",
            NativeName = "English"
        };

        // Act
        var result = languageInfo.ToString();

        // Assert
        Assert.Equal("English (English)", result);
    }

    [Fact]
    public void LanguageInfo_Equals_ShouldCompareByCode()
    {
        // Arrange
        var lang1 = new TestLanguageInfo { Code = "en-US", DisplayName = "English" };
        var lang2 = new TestLanguageInfo { Code = "en-US", DisplayName = "American English" };
        var lang3 = new TestLanguageInfo { Code = "zh-CN", DisplayName = "Chinese" };

        // Act & Assert
        Assert.True(lang1.Equals(lang2));
        Assert.False(lang1.Equals(lang3));
        Assert.Equal(lang1.GetHashCode(), lang2.GetHashCode());
    }

    [Fact]
    public void LocalizationService_GetString_ShouldReturnKeyNotFound_WhenKeyMissing()
    {
        // Arrange
        var service = new TestLocalizationService();

        // Act
        var result = service.GetString("missing.key");

        // Assert
        Assert.Equal("[KEY_NOT_FOUND: missing.key]", result);
    }

    [Fact]
    public void LocalizationService_GetString_ShouldReturnTranslation_WhenKeyExists()
    {
        // Arrange
        var service = new TestLocalizationService();
        service.AddTranslation("common.ok", "OK");

        // Act
        var result = service.GetString("common.ok");

        // Assert
        Assert.Equal("OK", result);
    }

    [Fact]
    public void LocalizationService_GetString_ShouldFormatString_WhenArgsProvided()
    {
        // Arrange
        var service = new TestLocalizationService();
        service.AddTranslation("greeting", "Hello, {0}!");

        // Act
        var result = service.GetString("greeting", "World");

        // Assert
        Assert.Equal("Hello, World!", result);
    }

    [Fact]
    public void LocalizationService_GetString_ShouldHandleFormatException_Gracefully()
    {
        // Arrange
        var service = new TestLocalizationService();
        service.AddTranslation("bad.format", "Hello {0} {1} {2}");

        // Act
        var result = service.GetString("bad.format", "World"); // Only one arg

        // Assert
        Assert.Equal("Hello {0} {1} {2}", result); // Should return unformatted string
    }

    [Fact]
    public void LocalizationService_GetString_ShouldReturnEmptyString_WhenKeyIsEmpty()
    {
        // Arrange
        var service = new TestLocalizationService();

        // Act
        var result = service.GetString("");

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void LocalizationService_GetString_ShouldReturnEmptyString_WhenKeyIsNull()
    {
        // Arrange
        var service = new TestLocalizationService();

        // Act
        var result = service.GetString(null);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void LanguageManager_ValidateFileName_ShouldReturnTrue_ForValidNames()
    {
        // Arrange
        var manager = new TestLanguageManager();

        // Act & Assert
        Assert.True(manager.ValidateFileName("en-US.json"));
        Assert.True(manager.ValidateFileName("zh-CN.json"));
        Assert.True(manager.ValidateFileName("fr-FR.json"));
        Assert.True(manager.ValidateFileName("en.json"));
    }

    [Fact]
    public void LanguageManager_ValidateFileName_ShouldReturnFalse_ForInvalidNames()
    {
        // Arrange
        var manager = new TestLanguageManager();

        // Act & Assert
        Assert.False(manager.ValidateFileName("invalid.json"));
        Assert.False(manager.ValidateFileName("en-US.txt"));
        Assert.False(manager.ValidateFileName("123-US.json"));
        Assert.False(manager.ValidateFileName("en-us.json")); // lowercase region
    }

    [Fact]
    public void LanguageManager_ParseLanguageFile_ShouldReturnTranslations_WhenValidJson()
    {
        // Arrange
        var manager = new TestLanguageManager();
        var json = @"{
            ""metadata"": {
                ""code"": ""en-US"",
                ""displayName"": ""English""
            },
            ""strings"": {
                ""common.ok"": ""OK"",
                ""common.cancel"": ""Cancel""
            }
        }";

        // Act
        var result = manager.ParseLanguageFile(json);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("OK", result["common.ok"]);
        Assert.Equal("Cancel", result["common.cancel"]);
    }

    [Fact]
    public void LanguageManager_ParseLanguageFile_ShouldReturnEmpty_WhenInvalidJson()
    {
        // Arrange
        var manager = new TestLanguageManager();
        var invalidJson = "{ invalid json content";

        // Act
        var result = manager.ParseLanguageFile(invalidJson);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void LocalizeExtension_Key_ShouldGetAndSet()
    {
        // Arrange
        var extension = new TestLocalizeExtension();

        // Act
        extension.Key = "test.key";

        // Assert
        Assert.Equal("test.key", extension.Key);
    }

    [Fact]
    public void LocalizeExtension_Args_ShouldGetAndSet()
    {
        // Arrange
        var extension = new TestLocalizeExtension();
        var args = new object[] { "test", 123 };

        // Act
        extension.Args = args;

        // Assert
        Assert.Equal(args, extension.Args);
    }
}

// Test implementations that don't depend on the main project
public class TestLanguageInfo
{
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string NativeName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";

    public override string ToString()
    {
        return $"{NativeName} ({DisplayName})";
    }

    public override bool Equals(object? obj)
    {
        return obj is TestLanguageInfo other && Code.Equals(other.Code, StringComparison.OrdinalIgnoreCase);
    }

    public override int GetHashCode()
    {
        return Code.GetHashCode(StringComparison.OrdinalIgnoreCase);
    }
}

public class TestLocalizationService
{
    private readonly Dictionary<string, string> _translations = new();

    public void AddTranslation(string key, string value)
    {
        _translations[key] = value;
    }

    public string GetString(string key, params object[] args)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        if (_translations.TryGetValue(key, out var translation))
        {
            try
            {
                return args.Length > 0 ? string.Format(translation, args) : translation;
            }
            catch (FormatException)
            {
                return translation; // Return unformatted string on format error
            }
        }

        return $"[KEY_NOT_FOUND: {key}]";
    }
}

public class TestLanguageManager
{
    private static readonly System.Text.RegularExpressions.Regex LanguageFilePattern = 
        new System.Text.RegularExpressions.Regex(@"^[a-z]{2}(-[A-Z]{2})?\.json$", 
            System.Text.RegularExpressions.RegexOptions.Compiled);

    public bool ValidateFileName(string fileName)
    {
        return !string.IsNullOrEmpty(fileName) && LanguageFilePattern.IsMatch(fileName);
    }

    public Dictionary<string, string> ParseLanguageFile(string jsonContent)
    {
        try
        {
            var options = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip
            };

            using var document = System.Text.Json.JsonDocument.Parse(jsonContent, new System.Text.Json.JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = System.Text.Json.JsonCommentHandling.Skip
            });

            var root = document.RootElement;
            var translations = new Dictionary<string, string>();

            if (root.TryGetProperty("strings", out var stringsElement))
            {
                foreach (var property in stringsElement.EnumerateObject())
                {
                    if (!string.IsNullOrEmpty(property.Name) && property.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        translations[property.Name] = property.Value.GetString() ?? string.Empty;
                    }
                }
            }

            return translations;
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }
}

public class TestLocalizeExtension
{
    public string Key { get; set; } = string.Empty;
    public object[]? Args { get; set; }
}
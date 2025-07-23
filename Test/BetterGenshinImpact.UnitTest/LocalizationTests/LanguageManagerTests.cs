using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BetterGenshinImpact.UnitTest.LocalizationTests;

public class LanguageManagerTests : IDisposable
{
    private readonly Mock<ILogger<LanguageManager>> _mockLogger;
    private readonly string _testLanguagesDirectory;
    private readonly LanguageManager _languageManager;

    public LanguageManagerTests()
    {
        _mockLogger = new Mock<ILogger<LanguageManager>>();
        _testLanguagesDirectory = Path.Combine(Path.GetTempPath(), "TestLanguages", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testLanguagesDirectory);
        
        // Use reflection to create LanguageManager with custom directory
        _languageManager = new LanguageManager(_mockLogger.Object);
        
        // Set the private field to use our test directory
        var languagesDirectoryField = typeof(LanguageManager)
            .GetField("_languagesDirectory", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        languagesDirectoryField?.SetValue(_languageManager, _testLanguagesDirectory);
    }

    [Fact]
    public async Task DiscoverLanguagesAsync_ShouldReturnEmptyList_WhenDirectoryDoesNotExist()
    {
        // Arrange
        var nonExistentDirectory = Path.Combine(Path.GetTempPath(), "NonExistentLanguages");
        var languagesDirectoryField = typeof(LanguageManager)
            .GetField("_languagesDirectory", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        languagesDirectoryField?.SetValue(_languageManager, nonExistentDirectory);

        // Act
        var result = await _languageManager.DiscoverLanguagesAsync();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task DiscoverLanguagesAsync_ShouldReturnLanguages_WhenValidFilesExist()
    {
        // Arrange
        await CreateTestLanguageFile("en-US.json", new
        {
            metadata = new
            {
                code = "en-US",
                displayName = "English",
                nativeName = "English",
                version = "1.0.0"
            },
            strings = new Dictionary<string, string>
            {
                ["common.ok"] = "OK",
                ["common.cancel"] = "Cancel"
            }
        });

        await CreateTestLanguageFile("zh-CN.json", new
        {
            metadata = new
            {
                code = "zh-CN",
                displayName = "Chinese",
                nativeName = "中文",
                version = "1.0.0"
            },
            strings = new Dictionary<string, string>
            {
                ["common.ok"] = "确定",
                ["common.cancel"] = "取消"
            }
        });

        // Act
        var result = await _languageManager.DiscoverLanguagesAsync();
        var languages = result.ToList();

        // Assert
        Assert.Equal(2, languages.Count);
        Assert.Contains(languages, l => l.Code == "en-US" && l.DisplayName == "English");
        Assert.Contains(languages, l => l.Code == "zh-CN" && l.DisplayName == "Chinese");
    }

    [Fact]
    public async Task DiscoverLanguagesAsync_ShouldIgnoreInvalidFiles_AndReturnValidOnes()
    {
        // Arrange
        await CreateTestLanguageFile("en-US.json", new
        {
            metadata = new
            {
                code = "en-US",
                displayName = "English",
                nativeName = "English"
            },
            strings = new Dictionary<string, string> { ["test"] = "Test" }
        });

        // Create invalid file
        await File.WriteAllTextAsync(Path.Combine(_testLanguagesDirectory, "invalid.json"), "invalid json content");
        
        // Create file with wrong naming convention
        await CreateTestLanguageFile("wrongname.json", new
        {
            metadata = new { code = "wrong" },
            strings = new Dictionary<string, string>()
        });

        // Act
        var result = await _languageManager.DiscoverLanguagesAsync();
        var languages = result.ToList();

        // Assert
        Assert.Single(languages);
        Assert.Equal("en-US", languages[0].Code);
    }

    [Fact]
    public async Task DiscoverLanguagesAsync_ShouldEnsureEnglishExists_WhenNotFound()
    {
        // Arrange
        await CreateTestLanguageFile("fr-FR.json", new
        {
            metadata = new
            {
                code = "fr-FR",
                displayName = "French",
                nativeName = "Français"
            },
            strings = new Dictionary<string, string> { ["test"] = "Test" }
        });

        // Act
        var result = await _languageManager.DiscoverLanguagesAsync();
        var languages = result.ToList();

        // Assert
        Assert.Equal(2, languages.Count);
        Assert.Contains(languages, l => l.Code == "en-US");
        Assert.Contains(languages, l => l.Code == "fr-FR");
    }

    [Fact]
    public async Task LoadLanguageAsync_ShouldReturnTranslations_WhenValidFileExists()
    {
        // Arrange
        var expectedTranslations = new Dictionary<string, string>
        {
            ["common.ok"] = "OK",
            ["common.cancel"] = "Cancel",
            ["settings.title"] = "Settings"
        };

        await CreateTestLanguageFile("en-US.json", new
        {
            metadata = new
            {
                code = "en-US",
                displayName = "English",
                nativeName = "English"
            },
            strings = expectedTranslations
        });

        // Act
        var result = await _languageManager.LoadLanguageAsync("en-US");

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("OK", result["common.ok"]);
        Assert.Equal("Cancel", result["common.cancel"]);
        Assert.Equal("Settings", result["settings.title"]);
    }

    [Fact]
    public async Task LoadLanguageAsync_ShouldReturnEmptyDictionary_WhenFileDoesNotExist()
    {
        // Act
        var result = await _languageManager.LoadLanguageAsync("nonexistent");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task LoadLanguageAsync_ShouldHandleCorruptedJson_Gracefully()
    {
        // Arrange
        await File.WriteAllTextAsync(
            Path.Combine(_testLanguagesDirectory, "corrupted.json"), 
            "{ invalid json content");

        // Act
        var result = await _languageManager.LoadLanguageAsync("corrupted");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task LoadLanguageAsync_ShouldHandleEmptyFile_Gracefully()
    {
        // Arrange
        await File.WriteAllTextAsync(
            Path.Combine(_testLanguagesDirectory, "empty.json"), 
            "");

        // Act
        var result = await _languageManager.LoadLanguageAsync("empty");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task LoadLanguageAsync_ShouldHandleMissingStringsSection_Gracefully()
    {
        // Arrange
        await CreateTestLanguageFile("no-strings.json", new
        {
            metadata = new
            {
                code = "no-strings",
                displayName = "No Strings"
            }
            // No strings section
        });

        // Act
        var result = await _languageManager.LoadLanguageAsync("no-strings");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task LoadLanguageAsync_ShouldFilterInvalidTranslations()
    {
        // Arrange
        var testData = new
        {
            metadata = new
            {
                code = "test",
                displayName = "Test"
            },
            strings = new Dictionary<string, object>
            {
                ["valid.key"] = "Valid Value",
                [""] = "Empty Key", // Should be filtered
                ["null.value"] = null, // Should be filtered
                ["valid.key2"] = "Another Valid Value"
            }
        };

        var json = JsonSerializer.Serialize(testData);
        await File.WriteAllTextAsync(Path.Combine(_testLanguagesDirectory, "test.json"), json);

        // Act
        var result = await _languageManager.LoadLanguageAsync("test");

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("valid.key", result.Keys);
        Assert.Contains("valid.key2", result.Keys);
        Assert.DoesNotContain("", result.Keys);
        Assert.DoesNotContain("null.value", result.Keys);
    }

    [Fact]
    public async Task LoadLanguageAsync_ShouldRecoverFromTrailingCommas()
    {
        // Arrange
        var jsonWithTrailingCommas = @"{
            ""metadata"": {
                ""code"": ""test"",
                ""displayName"": ""Test"",
            },
            ""strings"": {
                ""key1"": ""value1"",
                ""key2"": ""value2"",
            }
        }";

        await File.WriteAllTextAsync(Path.Combine(_testLanguagesDirectory, "trailing-commas.json"), jsonWithTrailingCommas);

        // Act
        var result = await _languageManager.LoadLanguageAsync("trailing-commas");

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("value1", result["key1"]);
        Assert.Equal("value2", result["key2"]);
    }

    [Fact]
    public void LanguagesDirectory_ShouldReturnCorrectPath()
    {
        // Act
        var directory = _languageManager.LanguagesDirectory;

        // Assert
        Assert.Equal(_testLanguagesDirectory, directory);
    }

    [Fact]
    public async Task FileWatcher_ShouldFireEvent_WhenFileIsCreated()
    {
        // Arrange
        var eventFired = false;
        string? eventFilePath = null;
        WatcherChangeTypes? eventChangeType = null;

        _languageManager.LanguageFilesChanged += (sender, args) =>
        {
            eventFired = true;
            eventFilePath = args.FilePath;
            eventChangeType = args.ChangeType;
        };

        // Wait a moment for file watcher to initialize
        await Task.Delay(100);

        // Act
        await CreateTestLanguageFile("new-lang.json", new
        {
            metadata = new { code = "new-lang" },
            strings = new Dictionary<string, string>()
        });

        // Wait for file system event
        await Task.Delay(500);

        // Assert
        Assert.True(eventFired);
        Assert.Equal(WatcherChangeTypes.Created, eventChangeType);
        Assert.Contains("new-lang.json", eventFilePath);
    }

    [Fact]
    public async Task FileWatcher_ShouldIgnoreInvalidFiles()
    {
        // Arrange
        var eventFired = false;

        _languageManager.LanguageFilesChanged += (sender, args) =>
        {
            eventFired = true;
        };

        // Wait a moment for file watcher to initialize
        await Task.Delay(100);

        // Act - Create file with invalid naming convention
        await File.WriteAllTextAsync(Path.Combine(_testLanguagesDirectory, "invalid-name.json"), "{}");

        // Wait for potential file system event
        await Task.Delay(500);

        // Assert
        Assert.False(eventFired);
    }

    private async Task CreateTestLanguageFile(string fileName, object data)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(_testLanguagesDirectory, fileName), json);
    }

    public void Dispose()
    {
        _languageManager?.Dispose();
        
        if (Directory.Exists(_testLanguagesDirectory))
        {
            try
            {
                Directory.Delete(_testLanguagesDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
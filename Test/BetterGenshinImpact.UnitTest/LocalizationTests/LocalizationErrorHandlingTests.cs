using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Core.Config;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BetterGenshinImpact.UnitTest.LocalizationTests;

public class LocalizationErrorHandlingTests
{
    private readonly Mock<ILanguageManager> _mockLanguageManager;
    private readonly Mock<IConfigService> _mockConfigService;
    private readonly Mock<ILogger<LocalizationService>> _mockLogger;
    private readonly LocalizationService _localizationService;

    public LocalizationErrorHandlingTests()
    {
        _mockLanguageManager = new Mock<ILanguageManager>();
        _mockConfigService = new Mock<IConfigService>();
        _mockLogger = new Mock<ILogger<LocalizationService>>();
        
        _localizationService = new LocalizationService(
            _mockLanguageManager.Object,
            _mockConfigService.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task InitializeAsync_ShouldHandleMalformedLanguageFiles_Gracefully()
    {
        // Arrange
        var availableLanguages = new List<LanguageInfo>
        {
            new LanguageInfo { Code = "en-US", DisplayName = "English", NativeName = "English" },
            new LanguageInfo { Code = "corrupted", DisplayName = "Corrupted", NativeName = "Corrupted" }
        };
        
        var englishTranslations = new Dictionary<string, string>
        {
            ["common.ok"] = "OK",
            ["common.cancel"] = "Cancel"
        };

        _mockLanguageManager.Setup(x => x.DiscoverLanguagesAsync())
            .ReturnsAsync(availableLanguages);
        _mockLanguageManager.Setup(x => x.LoadLanguageAsync("en-US"))
            .ReturnsAsync(englishTranslations);
        _mockLanguageManager.Setup(x => x.LoadLanguageAsync("corrupted"))
            .ThrowsAsync(new FormatException("Malformed JSON"));
        
        var mockConfig = new MockAllConfig();
        mockConfig.CommonConfig.Language = "corrupted";
        _mockConfigService.Setup(x => x.Get()).Returns(mockConfig);

        // Act
        await _localizationService.InitializeAsync();

        // Assert
        // Should fallback to English when the requested language file is corrupted
        Assert.Equal("en-US", _localizationService.CurrentLanguage);
        Assert.Equal("OK", _localizationService.GetString("common.ok"));
    }

    [Fact]
    public async Task SetLanguageAsync_ShouldHandleLoadLanguageException_Gracefully()
    {
        // Arrange
        var availableLanguages = new List<LanguageInfo>
        {
            new LanguageInfo { Code = "en-US", DisplayName = "English", NativeName = "English" },
            new LanguageInfo { Code = "zh-CN", DisplayName = "Chinese", NativeName = "中文" }
        };
        
        var englishTranslations = new Dictionary<string, string>
        {
            ["common.ok"] = "OK"
        };

        _mockLanguageManager.Setup(x => x.DiscoverLanguagesAsync())
            .ReturnsAsync(availableLanguages);
        _mockLanguageManager.Setup(x => x.LoadLanguageAsync("en-US"))
            .ReturnsAsync(englishTranslations);
        _mockLanguageManager.Setup(x => x.LoadLanguageAsync("zh-CN"))
            .ThrowsAsync(new IOException("File access error"));
        
        var mockConfig = new MockAllConfig();
        _mockConfigService.Setup(x => x.Get()).Returns(mockConfig);

        await _localizationService.InitializeAsync();

        // Act
        await _localizationService.SetLanguageAsync("zh-CN");

        // Assert
        // Should keep the current language when loading the new language fails
        Assert.Equal("en-US", _localizationService.CurrentLanguage);
    }

    [Fact]
    public void GetString_ShouldHandleFormattingExceptions_Gracefully()
    {
        // Arrange
        var translations = new Dictionary<string, string>
        {
            ["format.error"] = "Value: {0}, {1}, {2}"
        };

        // Use reflection to set private field for testing
        var currentTranslationsField = typeof(LocalizationService)
            .GetField("_currentTranslations", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        currentTranslationsField?.SetValue(_localizationService, translations);

        // Act - Only providing one argument when the format string expects three
        var result = _localizationService.GetString("format.error", "First");

        // Assert
        // Should return a formatted error message instead of throwing FormatException
        Assert.Contains("[FORMAT_ERROR", result);
        Assert.Contains("format.error", result);
    }

    [Fact]
    public async Task LanguageFilesChanged_ShouldReloadLanguage_WhenCurrentLanguageFileChanges()
    {
        // Arrange
        var availableLanguages = new List<LanguageInfo>
        {
            new LanguageInfo { Code = "en-US", DisplayName = "English", NativeName = "English" }
        };
        
        var initialTranslations = new Dictionary<string, string>
        {
            ["common.ok"] = "OK"
        };
        
        var updatedTranslations = new Dictionary<string, string>
        {
            ["common.ok"] = "Okay"
        };

        _mockLanguageManager.Setup(x => x.DiscoverLanguagesAsync())
            .ReturnsAsync(availableLanguages);
        
        // Setup initial load
        _mockLanguageManager.Setup(x => x.LoadLanguageAsync("en-US"))
            .ReturnsAsync(initialTranslations);
        
        var mockConfig = new MockAllConfig();
        _mockConfigService.Setup(x => x.Get()).Returns(mockConfig);

        await _localizationService.InitializeAsync();
        
        // Verify initial state
        Assert.Equal("OK", _localizationService.GetString("common.ok"));
        
        // Change the mock to return updated translations
        _mockLanguageManager.Setup(x => x.LoadLanguageAsync("en-US"))
            .ReturnsAsync(updatedTranslations);
        
        // Act - Simulate file change event
        _mockLanguageManager.Raise(
            x => x.LanguageFilesChanged += null,
            new LanguageFilesChangedEventArgs(WatcherChangeTypes.Changed, "en-US.json"));
        
        // Allow async event handling to complete
        await Task.Delay(100);
        
        // Assert
        Assert.Equal("Okay", _localizationService.GetString("common.ok"));
    }

    [Fact]
    public async Task LanguageFilesChanged_ShouldUpdateAvailableLanguages_WhenNewLanguageAdded()
    {
        // Arrange
        var initialLanguages = new List<LanguageInfo>
        {
            new LanguageInfo { Code = "en-US", DisplayName = "English", NativeName = "English" }
        };
        
        var updatedLanguages = new List<LanguageInfo>
        {
            new LanguageInfo { Code = "en-US", DisplayName = "English", NativeName = "English" },
            new LanguageInfo { Code = "fr-FR", DisplayName = "French", NativeName = "Français" }
        };

        _mockLanguageManager.Setup(x => x.DiscoverLanguagesAsync())
            .ReturnsAsync(initialLanguages);
        _mockLanguageManager.Setup(x => x.LoadLanguageAsync("en-US"))
            .ReturnsAsync(new Dictionary<string, string>());
        
        var mockConfig = new MockAllConfig();
        _mockConfigService.Setup(x => x.Get()).Returns(mockConfig);

        await _localizationService.InitializeAsync();
        
        // Verify initial state
        Assert.Single(_localizationService.AvailableLanguages);
        
        // Update the mock to return the new language list
        _mockLanguageManager.Setup(x => x.DiscoverLanguagesAsync())
            .ReturnsAsync(updatedLanguages);
        
        // Act - Simulate file change event for a new language file
        _mockLanguageManager.Raise(
            x => x.LanguageFilesChanged += null,
            new LanguageFilesChangedEventArgs(WatcherChangeTypes.Created, "fr-FR.json"));
        
        // Allow async event handling to complete
        await Task.Delay(100);
        
        // Assert
        Assert.Equal(2, _localizationService.AvailableLanguages.Count());
        Assert.Contains(_localizationService.AvailableLanguages, l => l.Code == "fr-FR");
    }
}

// Mock class for testing
public class MockAllConfig : AllConfig
{
}
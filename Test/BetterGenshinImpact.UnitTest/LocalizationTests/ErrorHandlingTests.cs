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

/// <summary>
/// Tests for error handling scenarios in the localization system
/// </summary>
public class ErrorHandlingTests
{
    private readonly Mock<ILanguageManager> _mockLanguageManager;
    private readonly Mock<IConfigService> _mockConfigService;
    private readonly Mock<ILogger<LocalizationService>> _mockLogger;
    private readonly LocalizationService _localizationService;

    public ErrorHandlingTests()
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
    public async Task InitializeAsync_ShouldHandleLanguageManagerException_AndUseEmergencyFallback()
    {
        // Arrange
        _mockLanguageManager.Setup(x => x.DiscoverLanguagesAsync())
            .ThrowsAsync(new IOException("Disk error"));
        _mockLanguageManager.Setup(x => x.LoadLanguageAsync(It.IsAny<string>()))
            .ThrowsAsync(new IOException("Disk error"));
        
        var mockConfig = new MockAllConfig();
        _mockConfigService.Setup(x => x.Get()).Returns(mockConfig);

        // Act & Assert - Should not throw
        await _localizationService.InitializeAsync();
        
        // Should fall back to emergency mode
        Assert.Equal("en-US", _localizationService.CurrentLanguage);
    }

    [Fact]
    public async Task SetLanguageAsync_ShouldHandleLoadException_AndFallbackToEnglish()
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
            .ThrowsAsync(new System.Text.Json.JsonException("Corrupted JSON"));
        
        var mockConfig = new MockAllConfig();
        mockConfig.CommonConfig.Language = "en-US"; // Explicitly set to English to ensure predictable initial state
        _mockConfigService.Setup(x => x.Get()).Returns(mockConfig);

        await _localizationService.InitializeAsync();

        // Act
        await _localizationService.SetLanguageAsync("zh-CN");

        // Assert - Should fallback to English when zh-CN fails to load
        Assert.Equal("en-US", _localizationService.CurrentLanguage);
    }

    [Fact]
    public async Task SetLanguageAsync_ShouldHandleConfigServiceException_Gracefully()
    {
        // Arrange
        var availableLanguages = new List<LanguageInfo>
        {
            new LanguageInfo { Code = "en-US", DisplayName = "English", NativeName = "English" }
        };
        
        var englishTranslations = new Dictionary<string, string>
        {
            ["common.ok"] = "OK"
        };

        _mockLanguageManager.Setup(x => x.DiscoverLanguagesAsync())
            .ReturnsAsync(availableLanguages);
        _mockLanguageManager.Setup(x => x.LoadLanguageAsync("en-US"))
            .ReturnsAsync(englishTranslations);
        
        var mockConfig = new MockAllConfig();
        _mockConfigService.Setup(x => x.Get()).Returns(mockConfig);
        _mockConfigService.Setup(x => x.Save()).Throws(new IOException("Config save failed"));

        await _localizationService.InitializeAsync();

        // Act & Assert - Should not throw even if config save fails
        await _localizationService.SetLanguageAsync("en-US");
        
        Assert.Equal("en-US", _localizationService.CurrentLanguage);
    }

    [Fact]
    public void GetString_ShouldHandleFormatException_AndReturnUnformattedString()
    {
        // Arrange
        var translations = new Dictionary<string, string>
        {
            ["bad.format"] = "Hello {0} {1} {2}" // More placeholders than args
        };

        var currentTranslationsField = typeof(LocalizationService)
            .GetField("_currentTranslations", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        currentTranslationsField?.SetValue(_localizationService, translations);

        // Act
        var result = _localizationService.GetString("bad.format", "World"); // Only one arg

        // Assert - Should return unformatted string instead of throwing
        Assert.Equal("Hello {0} {1} {2}", result);
    }

    [Fact]
    public void GetString_ShouldHandleNullArgs_Gracefully()
    {
        // Arrange
        var translations = new Dictionary<string, string>
        {
            ["simple.key"] = "Simple Value"
        };

        var currentTranslationsField = typeof(LocalizationService)
            .GetField("_currentTranslations", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        currentTranslationsField?.SetValue(_localizationService, translations);

        // Act
        var result = _localizationService.GetString("simple.key", null);

        // Assert
        Assert.Equal("Simple Value", result);
    }

    [Fact]
    public async Task InitializeAsync_ShouldHandleCorruptedFallbackTranslations_Gracefully()
    {
        // Arrange
        var availableLanguages = new List<LanguageInfo>
        {
            new LanguageInfo { Code = "zh-CN", DisplayName = "Chinese", NativeName = "中文" }
        };

        _mockLanguageManager.Setup(x => x.DiscoverLanguagesAsync())
            .ReturnsAsync(availableLanguages);
        _mockLanguageManager.Setup(x => x.LoadLanguageAsync("en-US"))
            .ThrowsAsync(new System.Text.Json.JsonException("Corrupted English file"));
        _mockLanguageManager.Setup(x => x.LoadLanguageAsync("zh-CN"))
            .ReturnsAsync(new Dictionary<string, string> { ["test"] = "测试" });
        
        var mockConfig = new MockAllConfig();
        _mockConfigService.Setup(x => x.Get()).Returns(mockConfig);

        // Act & Assert - Should not throw
        await _localizationService.InitializeAsync();
        
        // Should still initialize with some language
        Assert.NotNull(_localizationService.CurrentLanguage);
    }

    [Fact]
    public async Task SetLanguageAsync_ShouldHandleEmptyTranslations_AndCreateMinimalSet()
    {
        // Arrange
        var availableLanguages = new List<LanguageInfo>
        {
            new LanguageInfo { Code = "en-US", DisplayName = "English", NativeName = "English" },
            new LanguageInfo { Code = "empty-lang", DisplayName = "Empty", NativeName = "Empty" }
        };

        _mockLanguageManager.Setup(x => x.DiscoverLanguagesAsync())
            .ReturnsAsync(availableLanguages);
        _mockLanguageManager.Setup(x => x.LoadLanguageAsync("en-US"))
            .ReturnsAsync(new Dictionary<string, string> { ["test"] = "Test" });
        _mockLanguageManager.Setup(x => x.LoadLanguageAsync("empty-lang"))
            .ReturnsAsync(new Dictionary<string, string>()); // Empty translations
        
        var mockConfig = new MockAllConfig();
        mockConfig.CommonConfig.Language = "en-US"; // Explicitly set to English to ensure predictable initial state
        _mockConfigService.Setup(x => x.Get()).Returns(mockConfig);

        await _localizationService.InitializeAsync();

        // Act
        await _localizationService.SetLanguageAsync("empty-lang");

        // Assert - Should fallback to English due to empty translations
        Assert.Equal("en-US", _localizationService.CurrentLanguage);
    }

    [Fact]
    public async Task InitializeAsync_ShouldHandleCompleteFailure_AndSetMinimalState()
    {
        // Arrange - Everything fails
        _mockLanguageManager.Setup(x => x.DiscoverLanguagesAsync())
            .ThrowsAsync(new Exception("Complete failure"));
        _mockLanguageManager.Setup(x => x.LoadLanguageAsync(It.IsAny<string>()))
            .ThrowsAsync(new Exception("Complete failure"));
        _mockConfigService.Setup(x => x.Get())
            .Throws(new Exception("Config failure"));

        // Act & Assert - Should not throw
        await _localizationService.InitializeAsync();
        
        // Should have minimal state
        Assert.Equal("en-US", _localizationService.CurrentLanguage);
        Assert.NotNull(_localizationService.AvailableLanguages);
    }

    [Fact]
    public void GetString_ShouldLogMissingTranslations_ForMonitoring()
    {
        // Arrange
        var currentTranslations = new Dictionary<string, string>();
        var fallbackTranslations = new Dictionary<string, string>();

        var currentTranslationsField = typeof(LocalizationService)
            .GetField("_currentTranslations", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var fallbackTranslationsField = typeof(LocalizationService)
            .GetField("_fallbackTranslations", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        currentTranslationsField?.SetValue(_localizationService, currentTranslations);
        fallbackTranslationsField?.SetValue(_localizationService, fallbackTranslations);

        // Act
        var result = _localizationService.GetString("missing.key");

        // Assert
        Assert.Equal("[KEY_NOT_FOUND: missing.key]", result);
        
        // Verify logging was called
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Missing translation key: missing.key")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task LanguageFileChange_ShouldHandleException_Gracefully()
    {
        // Arrange
        var availableLanguages = new List<LanguageInfo>
        {
            new LanguageInfo { Code = "en-US", DisplayName = "English", NativeName = "English" }
        };
        
        var englishTranslations = new Dictionary<string, string>
        {
            ["common.ok"] = "OK"
        };

        _mockLanguageManager.Setup(x => x.DiscoverLanguagesAsync())
            .ReturnsAsync(availableLanguages);
        _mockLanguageManager.Setup(x => x.LoadLanguageAsync("en-US"))
            .ReturnsAsync(englishTranslations);
        
        var mockConfig = new MockAllConfig();
        _mockConfigService.Setup(x => x.Get()).Returns(mockConfig);

        await _localizationService.InitializeAsync();

        // Act - Simulate file change event that causes exception
        _mockLanguageManager.Setup(x => x.DiscoverLanguagesAsync())
            .ThrowsAsync(new IOException("File system error"));

        // Trigger file change event
        _mockLanguageManager.Raise(x => x.LanguageFilesChanged += null,
            new LanguageFilesChangedEventArgs(WatcherChangeTypes.Created, "test.json"));

        // Wait a moment for async handling
        await Task.Delay(100);

        // Assert - Service should still be functional
        Assert.Equal("en-US", _localizationService.CurrentLanguage);
        Assert.Equal("OK", _localizationService.GetString("common.ok"));
    }
}


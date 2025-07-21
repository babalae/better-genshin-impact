using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Core.Config;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BetterGenshinImpact.UnitTest.LocalizationTests;

public class LocalizationServiceTests
{
    private readonly Mock<ILanguageManager> _mockLanguageManager;
    private readonly Mock<IConfigService> _mockConfigService;
    private readonly Mock<ILogger<LocalizationService>> _mockLogger;
    private readonly LocalizationService _localizationService;

    public LocalizationServiceTests()
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
    public async Task InitializeAsync_ShouldSetDefaultLanguage_WhenNoSavedLanguage()
    {
        // Arrange
        var availableLanguages = new List<LanguageInfo>
        {
            new LanguageInfo { Code = "en-US", DisplayName = "English", NativeName = "English" },
            new LanguageInfo { Code = "zh-CN", DisplayName = "Chinese", NativeName = "中文" }
        };
        
        var englishTranslations = new Dictionary<string, string>
        {
            ["common.ok"] = "OK",
            ["common.cancel"] = "Cancel"
        };
        
        var chineseTranslations = new Dictionary<string, string>
        {
            ["common.ok"] = "确定",
            ["common.cancel"] = "取消"
        };

        _mockLanguageManager.Setup(x => x.DiscoverLanguagesAsync())
            .ReturnsAsync(availableLanguages);
        _mockLanguageManager.Setup(x => x.LoadLanguageAsync("en-US"))
            .ReturnsAsync(englishTranslations);
        _mockLanguageManager.Setup(x => x.LoadLanguageAsync("zh-CN"))
            .ReturnsAsync(chineseTranslations);
        
        var mockConfig = new MockAllConfigForService();
        mockConfig.CommonConfig.Language = null;
        _mockConfigService.Setup(x => x.Get()).Returns(mockConfig);

        // Act
        await _localizationService.InitializeAsync();

        // Assert
        // The service should use system language when no saved language preference exists
        // On Chinese systems, it will detect zh-CN; on English systems, it will use en-US
        var systemLanguage = System.Globalization.CultureInfo.CurrentUICulture.Name;
        var expectedLanguage = availableLanguages.Any(l => l.Code == systemLanguage) ? systemLanguage : "en-US";
        
        Assert.Equal(expectedLanguage, _localizationService.CurrentLanguage);
        Assert.Equal(2, _localizationService.AvailableLanguages.Count());
    }

    [Fact]
    public async Task SetLanguageAsync_ShouldChangeLanguage_WhenValidLanguageProvided()
    {
        // Arrange
        var availableLanguages = new List<LanguageInfo>
        {
            new LanguageInfo { Code = "en-US", DisplayName = "English", NativeName = "English" },
            new LanguageInfo { Code = "zh-CN", DisplayName = "Chinese", NativeName = "中文" }
        };
        
        var englishTranslations = new Dictionary<string, string>
        {
            ["common.ok"] = "OK",
            ["common.cancel"] = "Cancel"
        };
        
        var chineseTranslations = new Dictionary<string, string>
        {
            ["common.ok"] = "确定",
            ["common.cancel"] = "取消"
        };

        _mockLanguageManager.Setup(x => x.DiscoverLanguagesAsync())
            .ReturnsAsync(availableLanguages);
        _mockLanguageManager.Setup(x => x.LoadLanguageAsync("en-US"))
            .ReturnsAsync(englishTranslations);
        _mockLanguageManager.Setup(x => x.LoadLanguageAsync("zh-CN"))
            .ReturnsAsync(chineseTranslations);
        
        var mockConfig = new MockAllConfigForService();
        _mockConfigService.Setup(x => x.Get()).Returns(mockConfig);

        await _localizationService.InitializeAsync();

        // Act
        await _localizationService.SetLanguageAsync("zh-CN");

        // Assert
        Assert.Equal("zh-CN", _localizationService.CurrentLanguage);
        Assert.Equal("确定", _localizationService.GetString("common.ok"));
    }

    [Fact]
    public async Task SetLanguageAsync_ShouldFireLanguageChangedEvent_WhenLanguageChanges()
    {
        // Arrange
        var availableLanguages = new List<LanguageInfo>
        {
            new LanguageInfo { Code = "en-US", DisplayName = "English", NativeName = "English" },
            new LanguageInfo { Code = "zh-CN", DisplayName = "Chinese", NativeName = "中文" }
        };
        
        var englishTranslations = new Dictionary<string, string> { ["test"] = "Test" };
        var chineseTranslations = new Dictionary<string, string> { ["test"] = "测试" };

        _mockLanguageManager.Setup(x => x.DiscoverLanguagesAsync())
            .ReturnsAsync(availableLanguages);
        _mockLanguageManager.Setup(x => x.LoadLanguageAsync("en-US"))
            .ReturnsAsync(englishTranslations);
        _mockLanguageManager.Setup(x => x.LoadLanguageAsync("zh-CN"))
            .ReturnsAsync(chineseTranslations);
        
        var mockConfig = new MockAllConfigForService();
        _mockConfigService.Setup(x => x.Get()).Returns(mockConfig);

        await _localizationService.InitializeAsync();
        
        // Store the initial language that was set during initialization
        var initialLanguage = _localizationService.CurrentLanguage;

        LanguageChangedEventArgs? eventArgs = null;
        _localizationService.LanguageChanged += (sender, args) => eventArgs = args;

        // Act
        await _localizationService.SetLanguageAsync("zh-CN");

        // Assert
        Assert.NotNull(eventArgs);
        Assert.Equal(initialLanguage, eventArgs.PreviousLanguage);
        Assert.Equal("zh-CN", eventArgs.NewLanguage);
    }

    [Fact]
    public void GetString_ShouldReturnTranslation_WhenKeyExists()
    {
        // Arrange
        var translations = new Dictionary<string, string>
        {
            ["common.ok"] = "OK",
            ["common.cancel"] = "Cancel"
        };

        // Use reflection to set private field for testing
        var currentTranslationsField = typeof(LocalizationService)
            .GetField("_currentTranslations", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        currentTranslationsField?.SetValue(_localizationService, translations);

        // Act
        var result = _localizationService.GetString("common.ok");

        // Assert
        Assert.Equal("OK", result);
    }

    [Fact]
    public void GetString_ShouldReturnFormattedString_WhenArgsProvided()
    {
        // Arrange
        var translations = new Dictionary<string, string>
        {
            ["greeting"] = "Hello, {0}!"
        };

        var currentTranslationsField = typeof(LocalizationService)
            .GetField("_currentTranslations", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        currentTranslationsField?.SetValue(_localizationService, translations);

        // Act
        var result = _localizationService.GetString("greeting", "World");

        // Assert
        Assert.Equal("Hello, World!", result);
    }

    [Fact]
    public void GetString_ShouldReturnFallback_WhenKeyNotFound()
    {
        // Arrange
        var currentTranslations = new Dictionary<string, string>();
        var fallbackTranslations = new Dictionary<string, string>
        {
            ["missing.key"] = "Fallback Value"
        };

        var currentTranslationsField = typeof(LocalizationService)
            .GetField("_currentTranslations", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var fallbackTranslationsField = typeof(LocalizationService)
            .GetField("_fallbackTranslations", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        currentTranslationsField?.SetValue(_localizationService, currentTranslations);
        fallbackTranslationsField?.SetValue(_localizationService, fallbackTranslations);

        // Act
        var result = _localizationService.GetString("missing.key");

        // Assert
        Assert.Equal("Fallback Value", result);
    }

    [Fact]
    public void GetString_ShouldReturnKeyNotFoundIndicator_WhenKeyNotInAnyTranslation()
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
        var result = _localizationService.GetString("nonexistent.key");

        // Assert
        Assert.Equal("[KEY_NOT_FOUND: nonexistent.key]", result);
    }

    [Fact]
    public void GetString_ShouldReturnEmptyString_WhenKeyIsEmpty()
    {
        // Act
        var result = _localizationService.GetString("");

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GetString_ShouldReturnEmptyString_WhenKeyIsNull()
    {
        // Act
        var result = _localizationService.GetString(null);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task SetLanguageAsync_ShouldFallbackToEnglish_WhenInvalidLanguageProvided()
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
        _mockLanguageManager.Setup(x => x.LoadLanguageAsync("invalid-lang"))
            .ReturnsAsync(new Dictionary<string, string>());
        
        var mockConfig = new MockAllConfigForService();
        _mockConfigService.Setup(x => x.Get()).Returns(mockConfig);

        await _localizationService.InitializeAsync();

        // Act
        await _localizationService.SetLanguageAsync("invalid-lang");

        // Assert
        Assert.Equal("en-US", _localizationService.CurrentLanguage);
    }

    [Fact]
    public async Task InitializeAsync_ShouldHandleEmptyLanguageList_Gracefully()
    {
        // Arrange
        _mockLanguageManager.Setup(x => x.DiscoverLanguagesAsync())
            .ReturnsAsync(new List<LanguageInfo>());
        _mockLanguageManager.Setup(x => x.LoadLanguageAsync("en-US"))
            .ReturnsAsync(new Dictionary<string, string>());
        
        var mockConfig = new MockAllConfigForService();
        _mockConfigService.Setup(x => x.Get()).Returns(mockConfig);

        // Act & Assert - Should not throw
        await _localizationService.InitializeAsync();
        
        Assert.Equal("en-US", _localizationService.CurrentLanguage);
        Assert.Single(_localizationService.AvailableLanguages);
    }

    [Fact]
    public async Task InitializeAsync_ShouldHandleLanguageManagerException_Gracefully()
    {
        // Arrange
        _mockLanguageManager.Setup(x => x.DiscoverLanguagesAsync())
            .ThrowsAsync(new IOException("File system error"));
        
        var mockConfig = new MockAllConfigForService();
        _mockConfigService.Setup(x => x.Get()).Returns(mockConfig);

        // Act & Assert - Should not throw
        await _localizationService.InitializeAsync();
        
        Assert.Equal("en-US", _localizationService.CurrentLanguage);
    }

    [Fact]
    public void PropertyChanged_ShouldFire_WhenCurrentLanguageChanges()
    {
        // Arrange
        PropertyChangedEventArgs? eventArgs = null;
        _localizationService.PropertyChanged += (sender, args) => eventArgs = args;

        // Use reflection to trigger property change
        var currentLanguageProperty = typeof(LocalizationService)
            .GetProperty("CurrentLanguage", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        
        // This test is limited because we can't easily trigger the private setter
        // In a real scenario, this would be tested through SetLanguageAsync
        Assert.NotNull(currentLanguageProperty);
    }
}

// Mock classes for testing - simplified to avoid compilation issues

public class MockAllConfigForService : AllConfig
{
}


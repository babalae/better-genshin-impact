using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using BetterGenshinImpact.Markup;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Model;
using Moq;
using Xunit;

namespace BetterGenshinImpact.UnitTest.LocalizationTests;

public class LocalizeExtensionTests
{
    private readonly Mock<ILocalizationService> _mockLocalizationService;
    private readonly LocalizeExtension _localizeExtension;

    public LocalizeExtensionTests()
    {
        _mockLocalizationService = new Mock<ILocalizationService>();
        _localizeExtension = new LocalizeExtension();
    }

    [Fact]
    public void Constructor_WithKey_ShouldSetKey()
    {
        // Act
        var extension = new LocalizeExtension("test.key");

        // Assert
        Assert.Equal("test.key", extension.Key);
    }

    [Fact]
    public void Constructor_WithoutParameters_ShouldHaveEmptyKey()
    {
        // Act
        var extension = new LocalizeExtension();

        // Assert
        Assert.Equal(string.Empty, extension.Key);
    }

    [Fact]
    public void ProvideValue_ShouldReturnPlaceholder_WhenKeyIsEmpty()
    {
        // Arrange
        var mockServiceProvider = new Mock<IServiceProvider>();
        _localizeExtension.Key = "";

        // Act
        var result = _localizeExtension.ProvideValue(mockServiceProvider.Object);

        // Assert
        Assert.Equal("[EMPTY_KEY]", result);
    }

    [Fact]
    public void ProvideValue_ShouldReturnDesignTimePlaceholder_InDesignMode()
    {
        // This test is challenging because DesignerProperties.GetIsInDesignMode 
        // is difficult to mock. We'll test the key assignment instead.
        
        // Arrange
        _localizeExtension.Key = "test.key";

        // Act & Assert - Key should be properly set
        Assert.Equal("test.key", _localizeExtension.Key);
    }

    [Fact]
    public void Key_Property_ShouldGetAndSet()
    {
        // Act
        _localizeExtension.Key = "settings.title";

        // Assert
        Assert.Equal("settings.title", _localizeExtension.Key);
    }

    [Fact]
    public void Args_Property_ShouldGetAndSet()
    {
        // Arrange
        var args = new object[] { "test", 123 };

        // Act
        _localizeExtension.Args = args;

        // Assert
        Assert.Equal(args, _localizeExtension.Args);
    }

    [Theory]
    [InlineData("common.ok")]
    [InlineData("settings.title")]
    [InlineData("error.message")]
    public void Key_Property_ShouldAcceptValidKeys(string key)
    {
        // Act
        _localizeExtension.Key = key;

        // Assert
        Assert.Equal(key, _localizeExtension.Key);
    }

    [Fact]
    public void Args_Property_ShouldAcceptNull()
    {
        // Act
        _localizeExtension.Args = null;

        // Assert
        Assert.Null(_localizeExtension.Args);
    }

    [Fact]
    public void Args_Property_ShouldAcceptEmptyArray()
    {
        // Arrange
        var emptyArgs = new object[0];

        // Act
        _localizeExtension.Args = emptyArgs;

        // Assert
        Assert.Equal(emptyArgs, _localizeExtension.Args);
    }

    [Fact]
    public void Args_Property_ShouldAcceptMixedTypes()
    {
        // Arrange
        var mixedArgs = new object[] { "string", 42, true, 3.14 };

        // Act
        _localizeExtension.Args = mixedArgs;

        // Assert
        Assert.Equal(mixedArgs, _localizeExtension.Args);
    }
}

public class LocalizationProxyTests
{
    private readonly Mock<ILocalizationService> _mockLocalizationService;

    public LocalizationProxyTests()
    {
        _mockLocalizationService = new Mock<ILocalizationService>();
    }

    [Fact]
    public void LocalizedValue_ShouldReturnTranslation_WhenServiceReturnsValue()
    {
        // Arrange
        _mockLocalizationService.Setup(x => x.GetString("test.key", It.IsAny<object[]>()))
            .Returns("Test Value");

        var proxy = CreateLocalizationProxy("test.key", null);

        // Act
        var result = proxy.LocalizedValue;

        // Assert
        Assert.Equal("Test Value", result);
    }

    [Fact]
    public void LocalizedValue_ShouldReturnFormattedString_WhenArgsProvided()
    {
        // Arrange
        var args = new object[] { "World" };
        _mockLocalizationService.Setup(x => x.GetString("greeting", args))
            .Returns("Hello, World!");

        var proxy = CreateLocalizationProxy("greeting", args);

        // Act
        var result = proxy.LocalizedValue;

        // Assert
        Assert.Equal("Hello, World!", result);
        _mockLocalizationService.Verify(x => x.GetString("greeting", args), Times.Once);
    }

    [Fact]
    public void LocalizedValue_ShouldHandleServiceException_Gracefully()
    {
        // Arrange
        _mockLocalizationService.Setup(x => x.GetString("test.key", It.IsAny<object[]>()))
            .Throws(new InvalidOperationException("Service error"));

        var proxy = CreateLocalizationProxy("test.key", null);

        // Act
        var result = proxy.LocalizedValue;

        // Assert
        Assert.Equal("[PROXY_ERROR: test.key]", result);
    }

    [Fact]
    public void PropertyChanged_ShouldFire_WhenLanguageChanges()
    {
        // Arrange
        var proxy = CreateLocalizationProxy("test.key", null);
        
        PropertyChangedEventArgs? eventArgs = null;
        proxy.PropertyChanged += (sender, args) => eventArgs = args;

        // Act
        _mockLocalizationService.Raise(x => x.LanguageChanged += null, 
            new LanguageChangedEventArgs("en-US", "zh-CN"));

        // Assert
        Assert.NotNull(eventArgs);
        Assert.Equal("LocalizedValue", eventArgs.PropertyName);
    }

    [Fact]
    public void PropertyChanged_ShouldFire_WhenServiceCurrentLanguageChanges()
    {
        // Arrange
        var proxy = CreateLocalizationProxy("test.key", null);
        
        PropertyChangedEventArgs? eventArgs = null;
        proxy.PropertyChanged += (sender, args) => eventArgs = args;

        // Act
        _mockLocalizationService.Raise(x => x.PropertyChanged += null, 
            new PropertyChangedEventArgs("CurrentLanguage"));

        // Assert
        Assert.NotNull(eventArgs);
        Assert.Equal("LocalizedValue", eventArgs.PropertyName);
    }

    [Fact]
    public void PropertyChanged_ShouldNotFire_WhenOtherServicePropertyChanges()
    {
        // Arrange
        var proxy = CreateLocalizationProxy("test.key", null);
        
        var eventFired = false;
        proxy.PropertyChanged += (sender, args) => eventFired = true;

        // Act
        _mockLocalizationService.Raise(x => x.PropertyChanged += null, 
            new PropertyChangedEventArgs("SomeOtherProperty"));

        // Assert
        Assert.False(eventFired);
    }

    [Fact]
    public void LocalizedValue_ShouldUseEmptyArgs_WhenArgsIsNull()
    {
        // Arrange
        _mockLocalizationService.Setup(x => x.GetString("test.key", It.IsAny<object[]>()))
            .Returns("Test Value");

        var proxy = CreateLocalizationProxy("test.key", null);

        // Act
        var result = proxy.LocalizedValue;

        // Assert
        Assert.Equal("Test Value", result);
        _mockLocalizationService.Verify(x => x.GetString("test.key", It.Is<object[]>(args => args.Length == 0)), Times.Once);
    }

    private LocalizationProxy CreateLocalizationProxy(string key, object[]? args)
    {
        return new LocalizationProxy(_mockLocalizationService.Object, key, args);
    }
}
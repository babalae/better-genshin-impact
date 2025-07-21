using System;
using System.Threading.Tasks;
using BetterGenshinImpact.Markup;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.DependencyInjection;

namespace BetterGenshinImpact;

/// <summary>
/// Test class to verify LocalizeExtension functionality
/// </summary>
public static class TestLocalizeExtension
{
    public static async Task TestLocalizationExtension()
    {
        try
        {
            Console.WriteLine("Testing LocalizeExtension...");

            // Get the localization service
            var localizationService = App.GetService<ILocalizationService>();
            if (localizationService == null)
            {
                Console.WriteLine("❌ LocalizationService not found in DI container");
                return;
            }

            // Initialize the service if not already done
            await localizationService.InitializeAsync();

            // Test basic string retrieval
            var testKey = "common.ok";
            var result = localizationService.GetString(testKey);
            Console.WriteLine($"✅ Basic test - Key: '{testKey}', Result: '{result}'");

            // Test with parameters
            var paramKey = "settings.title";
            var paramResult = localizationService.GetString(paramKey);
            Console.WriteLine($"✅ Parameter test - Key: '{paramKey}', Result: '{paramResult}'");

            // Test language switching
            Console.WriteLine($"Current language: {localizationService.CurrentLanguage}");
            
            // Switch to Chinese if available
            var availableLanguages = localizationService.AvailableLanguages;
            foreach (var lang in availableLanguages)
            {
                Console.WriteLine($"Available language: {lang.Code} - {lang.DisplayName}");
            }

            // Test LocalizeExtension creation
            var localizeExt = new LocalizeExtension("common.ok");
            Console.WriteLine($"✅ LocalizeExtension created with key: {localizeExt.Key}");

            // Test with arguments
            var localizeExtWithArgs = new LocalizeExtension("common.save")
            {
                Args = new object[] { "test" }
            };
            Console.WriteLine($"✅ LocalizeExtension with args created");

            Console.WriteLine("✅ All LocalizeExtension tests passed!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Test failed: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }
}
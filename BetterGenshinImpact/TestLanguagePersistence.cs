using System;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact;

/// <summary>
/// Test class to verify language persistence functionality
/// </summary>
public static class TestLanguagePersistence
{
    public static async Task TestLanguagePersistenceAsync()
    {
        Console.WriteLine("Testing Language Persistence...");
        
        try
        {
            // Create test services
            using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var languageManagerLogger = loggerFactory.CreateLogger<LanguageManager>();
            var localizationServiceLogger = loggerFactory.CreateLogger<LocalizationService>();
            
            var configService = new ConfigService();
            var languageManager = new LanguageManager(languageManagerLogger);
            var localizationService = new LocalizationService(languageManager, configService, localizationServiceLogger);
            
            Console.WriteLine("✅ Services created successfully");
            
            // Test 1: Check initial configuration
            var config = configService.Get();
            Console.WriteLine($"Initial language setting: {config.CommonConfig.Language}");
            
            // Test 2: Initialize localization service
            await localizationService.InitializeAsync();
            Console.WriteLine($"✅ Localization service initialized with language: {localizationService.CurrentLanguage}");
            
            // Test 3: Change language and verify persistence
            Console.WriteLine("\n--- Testing Language Change ---");
            var originalLanguage = localizationService.CurrentLanguage;
            var targetLanguage = originalLanguage == "en-US" ? "zh-CN" : "en-US";
            
            Console.WriteLine($"Changing language from {originalLanguage} to {targetLanguage}");
            await localizationService.SetLanguageAsync(targetLanguage);
            
            // Verify the change was applied
            Console.WriteLine($"Current language after change: {localizationService.CurrentLanguage}");
            
            // Verify the change was persisted to configuration
            var updatedConfig = configService.Get();
            Console.WriteLine($"Language in configuration: {updatedConfig.CommonConfig.Language}");
            
            // Test 4: Verify configuration persistence by creating a new config service
            Console.WriteLine("\n--- Testing Configuration Persistence ---");
            var newConfigService = new ConfigService();
            var newConfig = newConfigService.Get();
            Console.WriteLine($"Language from new config service: {newConfig.CommonConfig.Language}");
            
            // Test 5: Test language restoration on startup
            Console.WriteLine("\n--- Testing Startup Language Restoration ---");
            var newLocalizationService = new LocalizationService(languageManager, newConfigService, localizationServiceLogger);
            await newLocalizationService.InitializeAsync();
            Console.WriteLine($"New localization service initialized with language: {newLocalizationService.CurrentLanguage}");
            
            // Verify results
            bool persistenceWorking = 
                localizationService.CurrentLanguage == targetLanguage &&
                updatedConfig.CommonConfig.Language == targetLanguage &&
                newConfig.CommonConfig.Language == targetLanguage &&
                newLocalizationService.CurrentLanguage == targetLanguage;
            
            if (persistenceWorking)
            {
                Console.WriteLine("\n✅ ALL TESTS PASSED - Language persistence is working correctly!");
                Console.WriteLine($"   - Language change: {originalLanguage} → {targetLanguage}");
                Console.WriteLine($"   - Configuration persistence: ✅");
                Console.WriteLine($"   - Startup restoration: ✅");
            }
            else
            {
                Console.WriteLine("\n❌ TESTS FAILED - Language persistence has issues");
                Console.WriteLine($"   - Expected language: {targetLanguage}");
                Console.WriteLine($"   - LocalizationService language: {localizationService.CurrentLanguage}");
                Console.WriteLine($"   - Config language: {updatedConfig.CommonConfig.Language}");
                Console.WriteLine($"   - New config language: {newConfig.CommonConfig.Language}");
                Console.WriteLine($"   - New service language: {newLocalizationService.CurrentLanguage}");
            }
            
            // Test 6: Test string retrieval
            Console.WriteLine("\n--- Testing String Retrieval ---");
            var testString = newLocalizationService.GetString("common.ok");
            Console.WriteLine($"Retrieved string for 'common.ok': '{testString}'");
            
            Console.WriteLine("\n🎉 Language persistence test completed!");
            
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Test failed with error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }
    
    /// <summary>
    /// Simple test method that can be called from anywhere
    /// </summary>
    public static void RunTest()
    {
        Task.Run(async () => await TestLanguagePersistenceAsync()).Wait();
    }
}
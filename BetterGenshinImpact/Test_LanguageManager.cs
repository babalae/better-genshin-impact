using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Service;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact;

/// <summary>
/// Simple test class to verify language management functionality
/// This file can be deleted after testing
/// </summary>
public static class Test_LanguageManager
{
    public static async Task TestLanguageManagerAsync()
    {
        // Create a simple logger for testing
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<LanguageManager>();

        // Create language manager instance
        var languageManager = new LanguageManager(logger);

        Console.WriteLine("Testing Language Manager...");
        Console.WriteLine($"Languages Directory: {languageManager.LanguagesDirectory}");

        try
        {
            // Test language discovery
            Console.WriteLine("\n1. Testing language discovery...");
            var languages = await languageManager.DiscoverLanguagesAsync();
            var languageList = languages.ToList();
            
            Console.WriteLine($"Found {languageList.Count} languages:");
            foreach (var lang in languageList)
            {
                Console.WriteLine($"  - {lang.Code}: {lang.DisplayName} ({lang.NativeName})");
            }

            // Test language loading
            Console.WriteLine("\n2. Testing language loading...");
            
            // Test English
            var englishTranslations = await languageManager.LoadLanguageAsync("en-US");
            Console.WriteLine($"English translations loaded: {englishTranslations.Count} keys");
            if (englishTranslations.ContainsKey("common.ok"))
            {
                Console.WriteLine($"  common.ok = '{englishTranslations["common.ok"]}'");
            }

            // Test Chinese
            var chineseTranslations = await languageManager.LoadLanguageAsync("zh-CN");
            Console.WriteLine($"Chinese translations loaded: {chineseTranslations.Count} keys");
            if (chineseTranslations.ContainsKey("common.ok"))
            {
                Console.WriteLine($"  common.ok = '{chineseTranslations["common.ok"]}'");
            }

            // Test non-existent language
            Console.WriteLine("\n3. Testing error handling...");
            var nonExistentTranslations = await languageManager.LoadLanguageAsync("xx-XX");
            Console.WriteLine($"Non-existent language translations: {nonExistentTranslations.Count} keys");

            Console.WriteLine("\nLanguage Manager test completed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Test failed with error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }
    
    /// <summary>
    /// Simple test method that can be called from anywhere
    /// </summary>
    public static void RunTest()
    {
        Task.Run(async () => await TestLanguageManagerAsync()).Wait();
    }
}
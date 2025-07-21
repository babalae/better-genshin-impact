using System;
using System.Threading.Tasks;
using BetterGenshinImpact.Service;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact;

/// <summary>
/// Simple console test to verify language management system
/// </summary>
public class TestLanguageSystem
{
    public static async Task Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--test-language")
        {
            await Test_LanguageManager.TestLanguageManagerAsync();
            return;
        }
        
        Console.WriteLine("Language Management System Test");
        Console.WriteLine("Use --test-language argument to run the test");
    }
}
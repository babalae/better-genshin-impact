using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;

namespace BetterGenshinImpact.Service
{
    public class DebugLocalizationService : ILocalizationService
    {
        public string CurrentLanguage { get; private set; } = "en-US";

        public IEnumerable<LanguageInfo> AvailableLanguages => new List<LanguageInfo>
        {
            new LanguageInfo
            {
                Code = "en-US",
                DisplayName = "English",
                NativeName = "English",
                FilePath = "debug",
                Version = "1.0.0"
            }
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<LanguageChangedEventArgs>? LanguageChanged;

        public string GetString(string key, params object[] args)
        {
            // Simple implementation that returns the key if no translation is found
            return key;
        }

        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        public Task SetLanguageAsync(string languageCode)
        {
            CurrentLanguage = languageCode;
            return Task.CompletedTask;
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
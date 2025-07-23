using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notifier.Interface;
using System;

namespace BetterGenshinImpact.Service.Notifier
{
    /// <summary>
    /// Base class for notifiers that provides localization support
    /// </summary>
    public abstract class LocalizedNotifierBase : INotifier
    {
        protected readonly ILocalizationService? _localizationService;
        
        protected LocalizedNotifierBase()
        {
            _localizationService = App.GetService<ILocalizationService>();
        }
        
        /// <summary>
        /// Gets a localized string using the localization service
        /// </summary>
        /// <param name="key">The localization key</param>
        /// <param name="args">Optional format arguments</param>
        /// <returns>The localized string or the key if localization service is not available</returns>
        protected string GetLocalizedString(string key, params object[] args)
        {
            return _localizationService != null ? _localizationService.GetString(key, args) : key;
        }
        
        /// <summary>
        /// Gets a localized error message
        /// </summary>
        /// <param name="key">The error message key</param>
        /// <param name="args">Optional format arguments</param>
        /// <returns>The localized error message or the key if localization service is not available</returns>
        protected string GetLocalizedErrorMessage(string key, params object[] args)
        {
            return GetLocalizedString(key, args);
        }
        
        /// <summary>
        /// The name of the notifier, localized if possible
        /// </summary>
        public abstract string Name { get; }
        
        /// <summary>
        /// Sends a notification
        /// </summary>
        /// <param name="data">The notification data to send</param>
        public abstract System.Threading.Tasks.Task SendAsync(BaseNotificationData data);
    }
}
using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Service.Notification;

/// <summary>
///     Notification
/// </summary>
[Serializable]
public partial class NotificationConfig : ObservableObject
{
    /// <summary>
    ///
    /// </summary>
    [ObservableProperty]
    private bool _webhookEnabled;

    /// <summary>
    ///
    /// </summary>
    [ObservableProperty]
    private string _webhookEndpoint = string.Empty;
}

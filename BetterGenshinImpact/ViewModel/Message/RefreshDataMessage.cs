using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BetterGenshinImpact.ViewModel.Message;

public class RefreshDataMessage(string value) : ValueChangedMessage<string>(value);

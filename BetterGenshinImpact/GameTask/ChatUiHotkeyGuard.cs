using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using System;
using System.Windows.Forms;

namespace BetterGenshinImpact.GameTask;

public static class ChatUiHotkeyGuard
{
    private const int StableFrameThreshold = 2;
    private static readonly TimeSpan ChatKeyPrimeDuration = TimeSpan.FromMilliseconds(280);
    private static readonly object Locker = new();

    private static ChatUiState _chatUiState = ChatUiState.Closed;
    private static int _enterFrameCount;
    private static int _exitFrameCount;
    private static DateTime _chatKeyPrimeUntilUtc = DateTime.MinValue;

    public static void UpdateVisualState(ChatUiDetectionResult detectionResult)
    {
        var visualState = detectionResult.State;

        lock (Locker)
        {
            if (visualState == ChatUiState.Closed)
            {
                _enterFrameCount = 0;
                if (_chatUiState == ChatUiState.Closed)
                {
                    _exitFrameCount = 0;
                }
                else if (++_exitFrameCount >= StableFrameThreshold)
                {
                    SetState(ChatUiState.Closed);
                    _exitFrameCount = 0;
                }
            }
            else
            {
                _exitFrameCount = 0;
                _chatKeyPrimeUntilUtc = DateTime.MinValue;
                if (_chatUiState == ChatUiState.Closed)
                {
                    if (++_enterFrameCount >= StableFrameThreshold)
                    {
                        SetState(visualState);
                        _enterFrameCount = 0;
                    }
                }
                else
                {
                    _enterFrameCount = 0;
                    SetState(visualState);
                }
            }

            if (_chatKeyPrimeUntilUtc <= DateTime.UtcNow)
            {
                _chatKeyPrimeUntilUtc = DateTime.MinValue;
            }
        }
    }

    public static void PrimeFromChatKey(Keys keyCode)
    {
        if (keyCode != TaskContext.Instance().Config.KeyBindingsConfig.OpenChatScreen.ToWinFormKeys())
        {
            return;
        }

        lock (Locker)
        {
            if (_chatUiState != ChatUiState.Closed)
            {
                return;
            }

            _chatKeyPrimeUntilUtc = DateTime.UtcNow + ChatKeyPrimeDuration;
        }
    }

    public static bool ShouldBlockHotkey(string? configPropertyName)
    {
        if (string.Equals(configPropertyName, nameof(HotKeyConfig.BgiEnabledHotkey), StringComparison.Ordinal))
        {
            return false;
        }

        lock (Locker)
        {
            if (_chatUiState != ChatUiState.Closed)
            {
                return true;
            }

            return _chatKeyPrimeUntilUtc > DateTime.UtcNow;
        }
    }

    public static void Reset()
    {
        lock (Locker)
        {
            _chatUiState = ChatUiState.Closed;
            _enterFrameCount = 0;
            _exitFrameCount = 0;
            _chatKeyPrimeUntilUtc = DateTime.MinValue;
        }
    }

    private static void SetState(ChatUiState nextState)
    {
        if (_chatUiState == nextState)
        {
            return;
        }

        _chatUiState = nextState;
    }
}

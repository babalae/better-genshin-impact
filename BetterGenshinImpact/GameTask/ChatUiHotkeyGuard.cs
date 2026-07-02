using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using System;
using System.Windows.Forms;
using BetterGenshinImpact.GameTask.Session;

namespace BetterGenshinImpact.GameTask;

public static class ChatUiHotkeyGuard
{
    private const int StableFrameThreshold = 2;
    private static readonly TimeSpan ChatKeyPrimeDuration = TimeSpan.FromMilliseconds(280);
    private static readonly ChatUiHotkeyGuardState LegacyState = new();

    private static ChatUiHotkeyGuardState State =>
        GameSessionContext.Current?.ChatUiHotkeyGuardState ?? LegacyState;

    public static void UpdateVisualState(ChatUiDetectionResult detectionResult)
    {
        var visualState = detectionResult.State;

        var state = State;
        lock (state.Locker)
        {
            if (visualState == ChatUiState.Closed)
            {
                state.EnterFrameCount = 0;
                if (state.ChatUiState == ChatUiState.Closed)
                {
                    state.ExitFrameCount = 0;
                }
                else if (++state.ExitFrameCount >= StableFrameThreshold)
                {
                    SetState(state, ChatUiState.Closed);
                    state.ExitFrameCount = 0;
                }
            }
            else
            {
                state.ExitFrameCount = 0;
                state.ChatKeyPrimeUntilUtc = DateTime.MinValue;
                if (state.ChatUiState == ChatUiState.Closed)
                {
                    if (++state.EnterFrameCount >= StableFrameThreshold)
                    {
                        SetState(state, visualState);
                        state.EnterFrameCount = 0;
                    }
                }
                else
                {
                    state.EnterFrameCount = 0;
                    SetState(state, visualState);
                }
            }

            if (state.ChatKeyPrimeUntilUtc <= DateTime.UtcNow)
            {
                state.ChatKeyPrimeUntilUtc = DateTime.MinValue;
            }
        }
    }

    public static void PrimeFromChatKey(Keys keyCode)
    {
        if (keyCode != TaskContext.Instance().Config.KeyBindingsConfig.OpenChatScreen.ToWinFormKeys())
        {
            return;
        }

        var state = State;
        lock (state.Locker)
        {
            if (state.ChatUiState != ChatUiState.Closed)
            {
                return;
            }

            state.ChatKeyPrimeUntilUtc = DateTime.UtcNow + ChatKeyPrimeDuration;
        }
    }

    public static bool ShouldBlockHotkey(string? configPropertyName)
    {
        if (string.Equals(configPropertyName, nameof(HotKeyConfig.BgiEnabledHotkey), StringComparison.Ordinal))
        {
            return false;
        }

        var state = State;
        lock (state.Locker)
        {
            if (state.ChatUiState != ChatUiState.Closed)
            {
                return true;
            }

            return state.ChatKeyPrimeUntilUtc > DateTime.UtcNow;
        }
    }

    public static void Reset()
    {
        var state = State;
        lock (state.Locker)
        {
            state.ChatUiState = ChatUiState.Closed;
            state.EnterFrameCount = 0;
            state.ExitFrameCount = 0;
            state.ChatKeyPrimeUntilUtc = DateTime.MinValue;
        }
    }

    private static void SetState(ChatUiHotkeyGuardState state, ChatUiState nextState)
    {
        if (state.ChatUiState == nextState)
        {
            return;
        }

        state.ChatUiState = nextState;
    }
}

internal sealed class ChatUiHotkeyGuardState
{
    public object Locker { get; } = new();

    public ChatUiState ChatUiState { get; set; } = ChatUiState.Closed;

    public int EnterFrameCount { get; set; }

    public int ExitFrameCount { get; set; }

    public DateTime ChatKeyPrimeUntilUtc { get; set; } = DateTime.MinValue;
}

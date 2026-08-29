using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Music.Model;
using Fischless.WindowsInput;
using System;
using System.Collections.Generic;
using System.Linq;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.Music.Service;

public abstract class KeyInputTransportBase : IKeyInputTransport
{
    private readonly object _syncRoot = new();
    private readonly HashSet<char> _pressedKeys = [];

    public abstract MusicInputMode Mode { get; }

    public void KeyDown(char key)
    {
        key = char.ToUpperInvariant(key);
        lock (_syncRoot)
        {
            if (!_pressedKeys.Add(key))
            {
                return;
            }

            SendKeyDown(ToVirtualKey(key));
        }
    }

    public void KeyUp(char key)
    {
        key = char.ToUpperInvariant(key);
        lock (_syncRoot)
        {
            if (!_pressedKeys.Remove(key))
            {
                return;
            }

            SendKeyUp(ToVirtualKey(key));
        }
    }

    public void ReleaseAll()
    {
        lock (_syncRoot)
        {
            foreach (var key in _pressedKeys.ToArray())
            {
                SendKeyUp(ToVirtualKey(key));
            }

            _pressedKeys.Clear();
        }
    }

    protected abstract void SendKeyDown(User32.VK key);

    protected abstract void SendKeyUp(User32.VK key);

    private static User32.VK ToVirtualKey(char key)
    {
        return key switch
        {
            'A' => User32.VK.VK_A,
            'B' => User32.VK.VK_B,
            'C' => User32.VK.VK_C,
            'D' => User32.VK.VK_D,
            'E' => User32.VK.VK_E,
            'F' => User32.VK.VK_F,
            'G' => User32.VK.VK_G,
            'H' => User32.VK.VK_H,
            'I' => User32.VK.VK_I,
            'J' => User32.VK.VK_J,
            'K' => User32.VK.VK_K,
            'L' => User32.VK.VK_L,
            'M' => User32.VK.VK_M,
            'N' => User32.VK.VK_N,
            'O' => User32.VK.VK_O,
            'P' => User32.VK.VK_P,
            'Q' => User32.VK.VK_Q,
            'R' => User32.VK.VK_R,
            'S' => User32.VK.VK_S,
            'T' => User32.VK.VK_T,
            'U' => User32.VK.VK_U,
            'V' => User32.VK.VK_V,
            'W' => User32.VK.VK_W,
            'X' => User32.VK.VK_X,
            'Y' => User32.VK.VK_Y,
            'Z' => User32.VK.VK_Z,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "不支持的原琴按键")
        };
    }
}

public sealed class PostMessageKeyInputTransport : KeyInputTransportBase
{
    public override MusicInputMode Mode => MusicInputMode.BackgroundPostMessage;

    protected override void SendKeyDown(User32.VK key)
    {
        TaskContext.Instance().PostMessageSimulator.KeyDownBackground(key);
    }

    protected override void SendKeyUp(User32.VK key)
    {
        TaskContext.Instance().PostMessageSimulator.KeyUpBackground(key);
    }
}

public sealed class SendInputKeyInputTransport : KeyInputTransportBase
{
    public override MusicInputMode Mode => MusicInputMode.ForegroundSendInput;

    protected override void SendKeyDown(User32.VK key)
    {
        if (InputBuilder.IsExtendedKey(key))
        {
            Simulation.SendInput.Keyboard.KeyDown(false, key);
        }
        else
        {
            Simulation.SendInput.Keyboard.KeyDown(key);
        }
    }

    protected override void SendKeyUp(User32.VK key)
    {
        if (InputBuilder.IsExtendedKey(key))
        {
            Simulation.SendInput.Keyboard.KeyUp(false, key);
        }
        else
        {
            Simulation.SendInput.Keyboard.KeyUp(key);
        }
    }
}

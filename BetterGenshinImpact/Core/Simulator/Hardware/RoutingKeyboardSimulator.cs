using Fischless.WindowsInput;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Simulator.Hardware;

internal sealed class RoutingKeyboardSimulator : IKeyboardSimulator
{
    private IInputSimulator? _owner;
    private KeyboardSimulator? _virtualKeyboard;

    public IMouseSimulator Mouse => _owner?.Mouse ?? throw new InvalidOperationException("Routing keyboard simulator is not initialized.");

    public void Initialize(IInputSimulator owner)
    {
        _owner = owner;
        _virtualKeyboard = new KeyboardSimulator(owner);
    }

    public IKeyboardSimulator KeyDown(User32.VK keyCode)
    {
        if (TryUseHardware(backend =>
            {
                if (HardwareKeyMapper.TryGetHidKey(keyCode, out var hidCode))
                {
                    backend.KeyDown(hidCode);
                    return true;
                }

                return false;
            }))
        {
            return this;
        }

        Virtual.KeyDown(keyCode);
        return this;
    }

    public IKeyboardSimulator KeyDown(bool? isExtendedKey, User32.VK keyCode) => KeyDown(keyCode);

    public IKeyboardSimulator KeyPress(User32.VK keyCode)
    {
        if (TryUseHardware(backend =>
            {
                if (HardwareKeyMapper.TryGetHidKey(keyCode, out var hidCode))
                {
                    backend.KeyPress(hidCode);
                    return true;
                }

                return false;
            }))
        {
            return this;
        }

        Virtual.KeyPress(keyCode);
        return this;
    }

    public IKeyboardSimulator KeyPress(bool? isExtendedKey, User32.VK keyCode) => KeyPress(keyCode);

    public IKeyboardSimulator KeyPress(params User32.VK[] keyCodes)
    {
        foreach (var keyCode in keyCodes)
        {
            KeyPress(keyCode);
        }

        return this;
    }

    public IKeyboardSimulator KeyPress(bool? isExtendedKey, params User32.VK[] keyCodes) => KeyPress(keyCodes);

    public IKeyboardSimulator KeyUp(User32.VK keyCode)
    {
        if (TryUseHardware(backend =>
            {
                if (HardwareKeyMapper.TryGetHidKey(keyCode, out var hidCode))
                {
                    backend.KeyUp(hidCode);
                    return true;
                }

                return false;
            }))
        {
            return this;
        }

        Virtual.KeyUp(keyCode);
        return this;
    }

    public IKeyboardSimulator KeyUp(bool? isExtendedKey, User32.VK keyCode) => KeyUp(keyCode);

    public IKeyboardSimulator ModifiedKeyStroke(IEnumerable<User32.VK> modifierKeyCodes, IEnumerable<User32.VK> keyCodes)
    {
        var modifiers = modifierKeyCodes?.ToArray() ?? [];
        var keys = keyCodes?.ToArray() ?? [];

        foreach (var modifier in modifiers)
        {
            KeyDown(modifier);
        }

        foreach (var key in keys)
        {
            KeyPress(key);
        }

        for (var i = modifiers.Length - 1; i >= 0; i--)
        {
            KeyUp(modifiers[i]);
        }

        return this;
    }

    public IKeyboardSimulator ModifiedKeyStroke(IEnumerable<User32.VK> modifierKeyCodes, User32.VK keyCode)
        => ModifiedKeyStroke(modifierKeyCodes, [keyCode]);

    public IKeyboardSimulator ModifiedKeyStroke(User32.VK modifierKey, IEnumerable<User32.VK> keyCodes)
        => ModifiedKeyStroke([modifierKey], keyCodes);

    public IKeyboardSimulator ModifiedKeyStroke(User32.VK modifierKeyCode, User32.VK keyCode)
        => ModifiedKeyStroke([modifierKeyCode], [keyCode]);

    public IKeyboardSimulator TextEntry(string text)
    {
        var backend = HardwareInputRouter.Instance.GetKeyboardBackend();
        if (backend == null)
        {
            Virtual.TextEntry(text);
            return this;
        }

        foreach (var (hidCode, withShift) in HardwareKeyMapper.EnumerateText(text))
        {
            if (withShift)
            {
                backend.KeyDown(225);
            }

            backend.KeyPress(hidCode);

            if (withShift)
            {
                backend.KeyUp(225);
            }
        }

        return this;
    }

    public IKeyboardSimulator TextEntry(char character) => TextEntry(character.ToString());

    public IKeyboardSimulator Sleep(int millsecondsTimeout)
    {
        Thread.Sleep(millsecondsTimeout);
        return this;
    }

    public IKeyboardSimulator Sleep(TimeSpan timeout)
    {
        Thread.Sleep(timeout);
        return this;
    }

    private KeyboardSimulator Virtual => _virtualKeyboard ?? throw new InvalidOperationException("Routing keyboard simulator is not initialized.");

    private static bool TryUseHardware(Func<IHardwareKeyboardBackend, bool> action)
    {
        var backend = HardwareInputRouter.Instance.GetKeyboardBackend();
        return backend != null && action(backend);
    }
}

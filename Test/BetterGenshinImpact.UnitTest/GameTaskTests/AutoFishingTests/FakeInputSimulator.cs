using Fischless.WindowsInput;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vanara.PInvoke;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFishingTests
{
    internal class FakeInputSimulator : IInputSimulator
    {
        public IKeyboardSimulator Keyboard => new FakeKeyboardSimulator();

        public IMouseSimulator Mouse => new FakeMouseSimulator();

        public IInputDeviceStateAdaptor InputDeviceState => throw new NotImplementedException();
    }

    internal class FakeKeyboardSimulator : IKeyboardSimulator
    {
        public IMouseSimulator Mouse => throw new NotImplementedException();

        public IKeyboardSimulator KeyDown(User32.VK keyCode) => this;

        public IKeyboardSimulator KeyDown(bool? isExtendedKey, User32.VK keyCode) => this;

        public IKeyboardSimulator KeyPress(User32.VK keyCode) => this;

        public IKeyboardSimulator KeyPress(bool? isExtendedKey, User32.VK keyCode) => this;

        public IKeyboardSimulator KeyPress(params User32.VK[] keyCodes) => this;

        public IKeyboardSimulator KeyPress(bool? isExtendedKey, params User32.VK[] keyCodes) => this;

        public IKeyboardSimulator KeyUp(User32.VK keyCode) => this;

        public IKeyboardSimulator KeyUp(bool? isExtendedKey, User32.VK keyCode) => this;

        public IKeyboardSimulator ModifiedKeyStroke(IEnumerable<User32.VK> modifierKeyCodes, IEnumerable<User32.VK> keyCodes) => this;

        public IKeyboardSimulator ModifiedKeyStroke(IEnumerable<User32.VK> modifierKeyCodes, User32.VK keyCode) => this;

        public IKeyboardSimulator ModifiedKeyStroke(User32.VK modifierKey, IEnumerable<User32.VK> keyCodes) => this;

        public IKeyboardSimulator ModifiedKeyStroke(User32.VK modifierKeyCode, User32.VK keyCode) => this;

        public IKeyboardSimulator Sleep(int millsecondsTimeout) => this;

        public IKeyboardSimulator Sleep(TimeSpan timeout) => this;

        public IKeyboardSimulator TextEntry(string text) => this;

        public IKeyboardSimulator TextEntry(char character) => this;
    }

    internal class FakeMouseSimulator : IMouseSimulator
    {
        public IKeyboardSimulator Keyboard => throw new NotImplementedException();

        public IMouseSimulator HorizontalScroll(int scrollAmountInClicks) => this;

        public IMouseSimulator LeftButtonClick() => this;

        public IMouseSimulator LeftButtonDoubleClick() => this;

        public IMouseSimulator LeftButtonDown() => this;

        public IMouseSimulator LeftButtonUp() => this;

        public IMouseSimulator MiddleButtonClick() => this;

        public IMouseSimulator MiddleButtonDoubleClick() => this;

        public IMouseSimulator MiddleButtonDown() => this;

        public IMouseSimulator MiddleButtonUp() => this;

        public IMouseSimulator MoveMouseBy(int pixelDeltaX, int pixelDeltaY) => this;

        public IMouseSimulator MoveMouseTo(double absoluteX, double absoluteY) => this;

        public IMouseSimulator MoveMouseToPositionOnVirtualDesktop(double absoluteX, double absoluteY) => this;

        public IMouseSimulator RightButtonClick() => this;

        public IMouseSimulator RightButtonDoubleClick() => this;

        public IMouseSimulator RightButtonDown() => this;

        public IMouseSimulator RightButtonUp() => this;

        public IMouseSimulator Sleep(int millsecondsTimeout) => this;

        public IMouseSimulator Sleep(TimeSpan timeout) => this;

        public IMouseSimulator VerticalScroll(int scrollAmountInClicks) => this;

        public IMouseSimulator XButtonClick(int buttonId) => this;

        public IMouseSimulator XButtonDoubleClick(int buttonId) => this;

        public IMouseSimulator XButtonDown(int buttonId) => this;

        public IMouseSimulator XButtonUp(int buttonId) => this;
    }
}

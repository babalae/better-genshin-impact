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
        private readonly FakeMouseSimulator _mouse = new();
        private readonly FakeKeyboardSimulator _keyboard = new();

        public IKeyboardSimulator Keyboard => _keyboard;

        public IMouseSimulator Mouse => _mouse;

        /// <summary>
        /// 模拟输入设备状态：默认视为左键已按下（对应 ThrowRod 举起鱼竿后校验左键状态的场景）。
        /// </summary>
        public IInputDeviceStateAdaptor InputDeviceState => new FakeInputDeviceStateAdaptor(() => _mouse.IsLeftButtonDown);

        /// <summary>
        /// 暴露键盘模拟器以断言按键次数（如 ESC）。
        /// </summary>
        public FakeKeyboardSimulator FakeKeyboard => _keyboard;
    }

    internal class FakeInputDeviceStateAdaptor : IInputDeviceStateAdaptor
    {
        private readonly Func<bool> _isLeftButtonDown;

        public FakeInputDeviceStateAdaptor(Func<bool> isLeftButtonDown)
        {
            _isLeftButtonDown = isLeftButtonDown;
        }

        public bool IsKeyDown(User32.VK keyCode) => keyCode == User32.VK.VK_LBUTTON && _isLeftButtonDown();

        public bool IsKeyUp(User32.VK keyCode) => !IsKeyDown(keyCode);

        public bool IsHardwareKeyDown(User32.VK keyCode) => IsKeyDown(keyCode);

        public bool IsHardwareKeyUp(User32.VK keyCode) => IsKeyUp(keyCode);

        public bool IsTogglingKeyInEffect(User32.VK keyCode) => false;
    }

    internal class FakeKeyboardSimulator : IKeyboardSimulator
    {
        /// <summary>
        /// 记录 ESC 键按下次数，供测试断言弹窗关闭/退出动作。
        /// </summary>
        public int EscapeKeyPressCount { get; private set; }

        public IMouseSimulator Mouse => throw new NotImplementedException();

        public IKeyboardSimulator KeyDown(User32.VK keyCode) => this;

        public IKeyboardSimulator KeyDown(bool? isExtendedKey, User32.VK keyCode) => this;

        public IKeyboardSimulator KeyPress(User32.VK keyCode)
        {
            if (keyCode == User32.VK.VK_ESCAPE)
            {
                EscapeKeyPressCount++;
            }
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
        /// <summary>
        /// 模拟左键是否处于按下状态（默认按下，对应抛竿流程中左键长按举起鱼竿）
        /// </summary>
        public bool IsLeftButtonDown { get; private set; } = true;

        public IKeyboardSimulator Keyboard => throw new NotImplementedException();

        public IMouseSimulator HorizontalScroll(int scrollAmountInClicks) => this;

        public IMouseSimulator LeftButtonClick() { IsLeftButtonDown = false; return this; }

        public IMouseSimulator LeftButtonDoubleClick() => this;

        public IMouseSimulator LeftButtonDown() { IsLeftButtonDown = true; return this; }

        public IMouseSimulator LeftButtonUp() { IsLeftButtonDown = false; return this; }

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

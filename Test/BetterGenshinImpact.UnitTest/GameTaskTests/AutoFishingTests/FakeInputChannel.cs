using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Simulator.Extensions;
using Vanara.PInvoke;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFishingTests
{
    /// <summary>
    /// 不产生任何实际输入的通道，所有操作直接返回自身
    /// </summary>
    internal class FakeInputChannel : IInputChannel, IKeyboardInput, IMouseInput
    {
        public IKeyboardInput Keyboard => this;

        public IMouseInput Mouse => this;

        public IInputChannel SimulateAction(GIActions action, KeyType type = KeyType.KeyPress) => this;

        public bool IsKeyDown(User32.VK key) => false;

        IInputChannel IInputChannel.Sleep(int ms) => this;

        public void ReleaseAll()
        {
        }

        public IKeyboardInput KeyDown(User32.VK key) => this;

        public IKeyboardInput KeyUp(User32.VK key) => this;

        public IKeyboardInput KeyPress(User32.VK key) => this;

        IKeyboardInput IKeyboardInput.Sleep(int ms) => this;

        public IMouseInput MoveMouseBy(int dx, int dy) => this;

        public IMouseInput MoveMouseTo(double absX, double absY) => this;

        public IMouseInput LeftButtonDown() => this;

        public IMouseInput LeftButtonUp() => this;

        public IMouseInput LeftButtonClick() => this;

        public IMouseInput RightButtonDown() => this;

        public IMouseInput RightButtonUp() => this;

        public IMouseInput RightButtonClick() => this;

        public IMouseInput MiddleButtonDown() => this;

        public IMouseInput MiddleButtonUp() => this;

        public IMouseInput MiddleButtonClick() => this;

        public IMouseInput XButtonDown(int buttonId) => this;

        public IMouseInput XButtonUp(int buttonId) => this;

        public IMouseInput XButtonClick(int buttonId) => this;

        public IMouseInput VerticalScroll(int scrollAmountInClicks) => this;

        IMouseInput IMouseInput.Sleep(int ms) => this;
    }
}

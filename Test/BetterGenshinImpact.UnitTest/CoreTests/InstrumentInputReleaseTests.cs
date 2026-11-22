using BetterGenshinImpact.Core.Input;
using Microsoft.Extensions.Logging.Abstractions;
using Vanara.PInvoke;

namespace BetterGenshinImpact.UnitTest.CoreTests;

public class InstrumentInputReleaseTests
{
    private const User32.VK Note = User32.VK.VK_H;
    private const User32.VK Mouse = User32.VK.VK_LBUTTON;

    [Theory]
    [InlineData(Note)]
    [InlineData(Mouse)]
    public void FailedExplicitUpCanBeRetriedByChannelCleanup(User32.VK key)
    {
        var channel = new RecordingChannel();
        channel.Keyboard.KeyDown(key);
        channel.FailNextRelease = key;
        Assert.Throws<InvalidOperationException>(() => channel.Keyboard.KeyUp(key));
        Assert.True(channel.IsKeyDown(key));

        channel.ReleasePressedKeys();
        Assert.False(channel.IsKeyDown(key));
        Assert.Equal(new[] { key, key }, channel.Releases);
    }

    [Theory]
    [InlineData(Note, Mouse)]
    [InlineData(Mouse, Note)]
    public void CleanupRetainsOnlyTheFailedInput(User32.VK failing, User32.VK other)
    {
        var channel = new RecordingChannel();
        channel.Keyboard.KeyDown(Note);
        channel.Mouse.LeftButtonDown();
        channel.FailNextRelease = failing;
        Assert.False(channel.TryReleasePressedKeys(NullLogger.Instance));

        Assert.True(channel.IsKeyDown(failing));
        Assert.False(channel.IsKeyDown(other));
        Assert.Contains(failing, channel.Releases);
        Assert.Contains(other, channel.Releases);
        channel.Releases.Clear();
        Assert.True(channel.TryReleasePressedKeys());
        channel.ReleasePressedKeys();
        Assert.False(channel.IsKeyDown(failing));
        Assert.Equal(new[] { failing }, channel.Releases);
    }

    [Fact]
    public async Task NewPressDuringCleanupRemainsHeldAfterBothOperations()
    {
        var channel = new RecordingChannel();
        using var releasing = new ManualResetEventSlim();
        using var finishRelease = new ManualResetEventSlim();
        using var pressing = new ManualResetEventSlim();
        channel.Keyboard.KeyDown(Note);
        channel.BeforeRelease = () =>
        {
            releasing.Set();
            Assert.True(finishRelease.Wait(TimeSpan.FromSeconds(5)));
        };
        var cleanup = Task.Run(channel.ReleasePressedKeys);
        Assert.True(releasing.Wait(TimeSpan.FromSeconds(5)));
        var press = Task.Run(() =>
        {
            pressing.Set();
            channel.Keyboard.KeyDown(Note);
        });
        try
        {
            Assert.True(pressing.Wait(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            finishRelease.Set();
        }
        await Task.WhenAll(cleanup, press);
        Assert.True(channel.IsKeyDown(Note));
        Assert.True(channel.NoteHeldByBackend);
    }

    private sealed class RecordingChannel : InputChannelBase
    {
        public User32.VK? FailNextRelease { get; set; }
        public bool NoteHeldByBackend { get; private set; }
        public Action? BeforeRelease { get; set; }
        public List<User32.VK> Releases { get; } = [];

        protected override void OnKeyDown(User32.VK key) => NoteHeldByBackend = true;
        protected override void OnKeyUp(User32.VK key)
        {
            Release(key);
            NoteHeldByBackend = false;
        }

        protected override void OnMouseButton(InputMouseButton button, bool down)
        {
            if (!down) Release(ToVirtualKey(button));
        }

        private void Release(User32.VK key)
        {
            BeforeRelease?.Invoke();
            Releases.Add(key);
            if (FailNextRelease != key) return;
            FailNextRelease = null;
            throw new InvalidOperationException("Simulated release failure");
        }

        protected override void OnMoveBy(int dx, int dy) => throw new NotSupportedException();
        protected override void OnMoveTo(double absX, double absY) => throw new NotSupportedException();
        protected override void OnScroll(int clicks) => throw new NotSupportedException();
    }
}

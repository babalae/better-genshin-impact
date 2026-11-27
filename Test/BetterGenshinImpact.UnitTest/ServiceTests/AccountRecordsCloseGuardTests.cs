using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BetterGenshinImpact.View.Behavior;

namespace BetterGenshinImpact.UnitTest.ServiceTests;

/// <summary>独立 STA 验证透明圆形命中与抬起/按下的消费规则，不启动主程序。</summary>
public sealed class AccountRecordsCloseGuardTests
{
    /// <summary>圆外透传、圆内命中，关闭抬起不消费保护，下一次按下消费。</summary>
    [Fact]
    public void CircleBlocksDownButReleaseDoesNotDismiss()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var dismissed = 0;
                var host = new Grid();
                var guard = new AccountRecordsCloseGuardAdorner(host, new Point(50, 50), () => dismissed++);
                guard.Measure(new Size(100, 100));
                guard.Arrange(new Rect(0, 0, 100, 100));
                guard.IsHitTestVisible = true;
                Assert.NotNull(VisualTreeHelper.HitTest(guard, new Point(50, 50)));
                Assert.Null(VisualTreeHelper.HitTest(guard, new Point(75, 75)));
                var up = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent };
                guard.RaiseEvent(up);
                Assert.True(up.Handled);
                Assert.Equal(0, dismissed);
                var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent };
                guard.RaiseEvent(down);
                Assert.True(down.Handled);
                Assert.Equal(1, dismissed);
            }
            catch (Exception e) { error = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Documents;
using System.Reflection;
using BetterGenshinImpact.View.Behavior;

namespace BetterGenshinImpact.UnitTest.ServiceTests;

/// <summary>独立 STA 验证透明圆形命中与抬起/按下的消费规则，不启动主程序。</summary>
public sealed class AccountRecordsCloseGuardTests
{
    /// <summary>键盘 Click 不建立鼠标遮罩，鼠标预按下后的 Click 仍建立保护。</summary>
    [Fact]
    public void KeyboardClickDoesNotCreateMouseGuard()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            var behavior = new AccountRecordsNoticeCloseGuardBehavior();
            try
            {
                var button = new Button { Content = "关闭", Width = 40, Height = 30 };
                var host = new UserControl { Content = button };
                var decorator = new AdornerDecorator { Child = host };
                window = new Window { Content = decorator };
                decorator.Measure(new Size(200, 100));
                decorator.Arrange(new Rect(0, 0, 200, 100));
                decorator.UpdateLayout();
                behavior.Attach(button);
                var field = typeof(AccountRecordsNoticeCloseGuardBehavior).GetField("_guard", BindingFlags.Instance | BindingFlags.NonPublic)!;
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Null(field.GetValue(behavior));
                button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(Assert.IsType<AccountRecordsCloseGuardAdorner>(field.GetValue(behavior)).IsHitTestVisible);
            }
            catch (Exception ex) { failure = ex; }
            finally { behavior.Detach(); window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
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

using System.Windows;
using System.Windows.Interop;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Controls;

namespace BetterGenshinImpact.UnitTest.ServiceTests;

/// <summary>独立 STA 验证分类选择及窗口实例化，不启动主程序。</summary>
[Collection("ScriptExecution")]
public sealed class InventoryIconCleanupTests
{
    /// <summary>仅提供表内分类，父子选择独立于页面，布局可实际构造。</summary>
    [Fact]
    public Task CategoriesOnlyContainCurrentRowsAndDialogCanLayout()
    {
        var completion = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            // 主题助手依赖全局配置；只在内存中补齐测试上下文，结束后还原，不读取用户配置。
            var property = typeof(ConfigService).GetProperty(nameof(ConfigService.Config))!;
            var previous = ConfigService.Config;
            try
            {
                property.SetValue(null, previous ?? new AllConfig());
                var model = new AccountRecordsIconCleanupViewModel([
                    new() { Name = "甲", Page = "材料", MaterialType = "食材" },
                    new() { Name = "乙", Page = "材料", MaterialType = "矿石" }]);
                Assert.Single(model.Categories);
                Assert.Equal(2, model.Categories[0].Children.Count);
                Assert.False(model.HasSelection);
                model.Categories[0].Children.Single(child => child.Category == "食材").IsSelected = true;
                Assert.Equal("甲", Assert.Single(model.SelectedNames));
                Assert.Null(model.Categories[0].IsSelected);
                var window = new AccountRecordsIconCleanupDialog(model);
                // 创建真实窗口句柄但不显示窗口，覆盖 FluentWindow 的背景初始化约束。
                Assert.True(window.ExtendsContentIntoTitleBar);
                Assert.NotEqual(IntPtr.Zero, new WindowInteropHelper(window).EnsureHandle());
                window.Measure(new Size(440, 520));
                window.Arrange(new Rect(0, 0, 440, 520));
                window.UpdateLayout();
                model.SelectAllCommand.Execute(null);
                Assert.Equal(2, model.SelectedNames.Length);
                model.CancelCommand.Execute(null);
                Assert.False(model.Accepted);
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
            finally { property.SetValue(null, previous); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}

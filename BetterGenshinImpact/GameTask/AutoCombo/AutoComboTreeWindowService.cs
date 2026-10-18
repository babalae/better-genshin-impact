using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.View.Windows;

namespace BetterGenshinImpact.GameTask.AutoCombo;

/// <summary>
/// 自动连招行为树浮窗显隐服务：持有浮窗单例并管理其生命周期（重建、Show/Hide）
/// </summary>
public class AutoComboTreeWindowService : Singleton<AutoComboTreeWindowService>
{
    private AutoComboTreeWindow? _window;

    /// <summary>显示行为树浮窗（重复调用安全）</summary>
    public void Show()
    {
        // 独立任务在后台线程调用，统一调度到 UI 线程
        UIDispatcherHelper.Invoke(() =>
        {
            if (_window is null)
            {
                _window = new AutoComboTreeWindow();
                _window.Closed += (_, _) => _window = null;
            }

            _window.Show();
        });
    }

    /// <summary>隐藏行为树浮窗（窗口未创建/已关闭时安全）</summary>
    public void Hide()
    {
        UIDispatcherHelper.Invoke(() =>
        {
            try
            {
                _window?.Hide();
            }
            catch
            {
                // 窗口已关闭或释放时忽略
            }
        });
    }
}

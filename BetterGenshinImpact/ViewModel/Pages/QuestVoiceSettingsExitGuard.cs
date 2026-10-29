using System.Threading.Tasks;
using BetterGenshinImpact.View.Windows;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>宿主正常关闭前提交延迟绑定及有效路由草稿，失败保持窗口可编辑。</summary>
internal static class QuestVoiceSettingsExitGuard
{
    /// <summary>等待文本绑定防抖完成；不强制结束输入法组合，不在成功时弹窗。</summary>
    public static async Task<bool> PrepareAsync()
    {
        // 页面文本最多 600ms 延迟，关闭前留出一次 Dispatcher 提交机会。
        await Task.Delay(700);
        var owner = App.GetService<QuestVoicePageViewModel>();
        if (owner is null || await owner.FlushSettingsAsync()) return true;
        await ThemedMessageBox.WarningAsync("自动配音配置仍有未保存草稿。请修正输入、处理文件冲突，或导出草稿后明确放弃并重新加载，再退出。", "配置尚未保存");
        return false;
    }
}

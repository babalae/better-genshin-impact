using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Service.I18n;
using BetterGenshinImpact.View.Windows;

namespace BetterGenshinImpact.ViewModel.Pages.View;

public interface IInferenceSettingsInteraction
{
    Task<bool> ConfirmDownloadAsync(IReadOnlyList<RuntimeDownloadPackage> packages, bool dependencies);
    Task<bool> ConfirmRestartAsync(string pending, string validationHint, string currentEngine, bool interruptsTask);
    void Restart();
}

/// <summary>设置页交互与安装、验证流程分离，测试不打开窗口、不重启程序。</summary>
public sealed class InferenceSettingsInteraction : IInferenceSettingsInteraction
{
    public async Task<bool> ConfirmDownloadAsync(IReadOnlyList<RuntimeDownloadPackage> packages, bool dependencies)
    {
        var components = string.Join("\n", packages.Select(p => $"• {p.Name}：{p.DownloadBytes / 1048576d:F0} MB"));
        var required = packages.Sum(p => p.DownloadBytes + p.InstalledBytes) / 1073741824d;
        return await ThemedMessageBox.ShowAsync(string.Format(
                I18nService.Instance.Translate("{0}\n\n预计下载：{1:F0} MB\n预计所需空间（含暂存）：最多 {2:F1} GB\n已有下载可续传；安装不会自动切换引擎。\n是否下载？"),
                components, packages.Sum(p => p.DownloadBytes) / 1048576d, required),
            dependencies ? I18nService.Instance.Translate("补齐配套依赖") : I18nService.Instance.Translate("下载运行库"), MessageBoxButton.YesNo,
            ThemedMessageBox.MessageBoxIcon.Question) == MessageBoxResult.Yes;
    }

    public async Task<bool> ConfirmRestartAsync(string pending, string validationHint, string currentEngine, bool interruptsTask)
    {
        var warning = interruptsTask ? I18nService.Instance.Translate("当前任务会被中断。") + "\n" : "";
        return await ThemedMessageBox.ShowAsync(warning + string.Format(
                I18nService.Instance.Translate("设置已保存：{0}\n是否立即重启软件？\n{1}\n选择“否”可稍后重启，当前进程继续使用 {2}。"),
                pending, validationHint, currentEngine),
            I18nService.Instance.Translate("推理设置"), MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Question) == MessageBoxResult.Yes;
    }

    public void Restart() => SystemControl.RestartApplication([], graceful: true);
}

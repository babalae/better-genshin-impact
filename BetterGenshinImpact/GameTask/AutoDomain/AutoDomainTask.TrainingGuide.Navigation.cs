using BetterGenshinImpact.Core.Input;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Job;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoDomain;

// 冒险之证培养指南的页面导航与秘境名称规范化。
public partial class AutoDomainTask
{
    /// <summary>秘境下拉中“根据提升指南选择秘境”的选项值。</summary>
    public const string TrainingGuideOption = "根据提升指南选择秘境";

    /// <summary>本轮由提升指南选出的秘境名称；为 null 时使用参数中的秘境。</summary>
    private string? _guideDomainName;

    /// <summary>去除空白并修正已知 OCR 误识别的秘境名称。</summary>
    /// <param name="text">OCR 原文。</param>
    /// <returns>规范化后的名称。</returns>
    private static string NormalizeGuideDomainName(string text)
    {
        var name = Regex.Replace(text, @"\s+", "");
        // 菫/堇及OCR误识别的董，只在完整秘境名称中修正，避免宽泛模糊匹配刷错本。
        return name.Replace("菫色之庭", "堇色之庭").Replace("董色之庭", "堇色之庭");
    }

    // 提升指南目前按简体中文界面识别；失败时停止，不回退到另一个秘境。
    private async Task ClickGuideText(string text, double x, double y, double w, double h)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            _ct.ThrowIfCancellationRequested();
            using var screen = _runtime.CaptureToRectArea();
            var matches = screen.FindMulti(RecognitionObject.Ocr(
                screen.Width * x, screen.Height * y, screen.Width * w, screen.Height * h));
            try
            {
                var match = matches.FirstOrDefault(r => r.Text.Replace(" ", "") == text);
                if (match != null)
                {
                    match.Click();
                    await _runtime.Delay(700, _ct);
                    return;
                }
            }
            finally
            {
                foreach (var match in matches) match.Dispose();
            }
            await _runtime.Delay(300, _ct);
        }
        throw new InvalidOperationException($"提升指南：未找到{text}，请检查游戏语言和界面");
    }

    /// <summary>返回主界面后打开冒险之证的秘境提升指南页。</summary>
    private async Task OpenTrainingGuide()
    {
        await new ReturnMainUiTask(_runtime).Start(_ct);
        InputHub.Foreground.SimulateAction(GIActions.OpenAdventurerHandbook);
        await _runtime.Delay(1000, _ct);
        await ClickGuideText("秘境", 0.1, 0.2, 0.1, 0.6);
        await ClickGuideText("提升指南", 0.2, 0.17, 0.18, 0.15);
    }

}

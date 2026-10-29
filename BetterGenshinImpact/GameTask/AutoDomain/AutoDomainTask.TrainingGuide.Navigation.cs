using BetterGenshinImpact.Core.Input;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.Common.Job;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoDomain;

// 冒险之证培养指南的页面导航与秘境名称规范化。
public partial class AutoDomainTask
{
    public const string TrainingGuideOption = "根据提升指南选择秘境";
    private string? _guideDomainName;

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
            using var screen = CaptureToRectArea();
            var matches = screen.FindMulti(RecognitionObject.Ocr(
                screen.Width * x, screen.Height * y, screen.Width * w, screen.Height * h));
            try
            {
                var match = matches.FirstOrDefault(r => r.Text.Replace(" ", "") == text);
                if (match != null)
                {
                    match.Click();
                    await Delay(700, _ct);
                    return;
                }
            }
            finally
            {
                foreach (var match in matches) match.Dispose();
            }
            await Delay(300, _ct);
        }
        throw new InvalidOperationException($"提升指南：未找到{text}，请检查游戏语言和界面");
    }

    private async Task OpenTrainingGuide()
    {
        await new ReturnMainUiTask().Start(_ct);
        InputHub.Foreground.SimulateAction(GIActions.OpenAdventurerHandbook);
        await Delay(1000, _ct);
        await ClickGuideText("秘境", 0.1, 0.2, 0.1, 0.6);
        await ClickGuideText("提升指南", 0.2, 0.17, 0.18, 0.15);
    }

}

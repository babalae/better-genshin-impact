using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BetterGenshinImpact.View.Windows;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace BetterGenshinImpact.GameTask.UseRedeemCode;

public class RedeemCodeManager
{
    public static async Task ImportFromClipboard(string clipboardText)
    {
        var codes = ExtractAllCodes(clipboardText);
        if (codes.Count == 0)
        {
            return;
        }

        var multilineTextBox = new TextBox
        {
            TextWrapping = TextWrapping.Wrap,
            Height = 340,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Text = codes.Aggregate("", (current, code) => current + $"{code}\n"),
        };
        var p = new PromptDialog(
            "从剪切版中获取到下面的兑换码，是否自动使用？",
            "自动使用兑换码",
            multilineTextBox,
            null);
        p.Height = 500;
        p.ShowDialog();

        if (p.DialogResult != true)
        {
            return;
        }

        Clipboard.Clear();
        await new TaskRunner().RunSoloTaskAsync(new UseRedemptionCodeTask(codes));
    }

    public static List<string> ExtractAllCodes(string clipboardText)
    {
        if (string.IsNullOrEmpty(clipboardText))
        {
            return [];
        }

        var regex = new Regex(@"(?<![A-Z0-9])(?=[A-Z0-9]*[A-Z])[A-Z0-9]{12}(?![A-Z0-9])");
        return regex.Matches(clipboardText)
            .Select(m => m.Value)
            .ToList();
    }
}
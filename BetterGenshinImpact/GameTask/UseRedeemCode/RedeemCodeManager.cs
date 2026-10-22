using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers.Security;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.View.Windows;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace BetterGenshinImpact.GameTask.UseRedeemCode;

public class RedeemCodeManager
{
    /// <summary>
    /// 配置根。静态方法没有创建方可以传参，暂时读配置根（见 task-context-removal.md 第 5 节）
    /// </summary>
    private static AllConfig Config => ConfigService.Config!;

    public static HashSet<string> CancelClipboardHash { get; } = [];

    private static readonly Regex UrlStripRegex =
        new(@"https?://\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    public static void AddNotDetectClipboardText(string clipboardText)
    {
        var md5Hash = MD5Helper.ComputeMD5(clipboardText);
        CancelClipboardHash.Add(md5Hash);
    }
    
    public static async Task ImportFromClipboard(string clipboardText)
    {
        if (!Config.AutoRedeemCodeConfig.ClipboardListenerEnabled)
        {
            return;
        }
        
        var md5Hash = MD5Helper.ComputeMD5(clipboardText);
        if (CancelClipboardHash.Contains(md5Hash))
        {
            return;
        }

        var cleanedText = UrlStripRegex.Replace(clipboardText, "");  
        var codes = ExtractAllCodes(cleanedText);
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
            if (CancelClipboardHash.Count > 10)
            {
                CancelClipboardHash.Clear();
            }
            CancelClipboardHash.Add(md5Hash);
            return;
        }

        Clipboard.Clear();
        // 任务在拿到本轮运行环境之后才构造
        await new TaskRunner().RunSoloTaskAsync(rt => new UseRedemptionCodeTask(rt), new UseRedemptionCodeParam { Codes = codes });
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

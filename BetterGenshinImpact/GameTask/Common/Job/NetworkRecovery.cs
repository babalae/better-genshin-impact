using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using System.Linq;
using BetterGenshinImpact.Core.Recognition;
using System.Text.RegularExpressions;


namespace BetterGenshinImpact.GameTask.Common.Job;

public class NetworkRecovery
{
    public string Name => "断网恢复";
    
    private static RecognitionObject GetConfirmRa(bool isOcrMatch = false,params string[] targetText)
    {
        var screenArea = CaptureToRectArea();
        var x = (int)(screenArea.Width * 0.3);
        var y = (int)(screenArea.Height * 0.3);
        var width = (int)(screenArea.Width * 0.5);
        var height = (int)(screenArea.Height * 0.5);
        
        return isOcrMatch ? RecognitionObject.OcrMatch(x, y, width, height, targetText) : 
            RecognitionObject.Ocr(x, y, width, height);
    }
    
    public static async Task Start(CancellationToken ct)
    {
        var fightAssets = AutoFightAssets.Instance;
        
        await NewRetry.WaitForElementDisappear(
            GetConfirmRa(true,"连接超时","连接已断开","网络错误","无法登录服务器","提示","通知"),
            screen => { 
                var confirm =
                    screen.FindMulti(GetConfirmRa());
                var confirmDone = confirm.LastOrDefault(t =>
                    Regex.IsMatch(t.Text, "确认"));
                if (confirmDone != null)
                {
                    confirmDone.Click();
                    confirmDone.Dispose();
                }
            },
            ct,
            10,
            1000
        );
        
        await Task.Delay(3000, ct);
        
        await NewRetry.WaitForElementDisappear(
            GetConfirmRa(true,"连接超时","连接已断开","网络错误","无法登录服务器","提示","通知"),
            screen => { 
                var confirm =
                    screen.FindMulti(GetConfirmRa());
                var confirmDone = confirm.LastOrDefault(t =>
                    Regex.IsMatch(t.Text, "确认"));
                if (confirmDone != null)
                {
                    confirmDone.Click();
                    confirmDone.Dispose();
                }
            },
            ct,
            5,
            1000
        );
        
        await NewRetry.WaitForElementAppear(
            ElementAssets.Instance.PaimonMenuRo,
            ()  => {  
                using var ra = CaptureToRectArea();
                //Enter出现
                var enter =
                    ra.FindMulti(GetConfirmRa());
                var enterDone = enter.LastOrDefault(t =>
                    Regex.IsMatch(t.Text, "确认"));
                if (enterDone != null)
                {
                    enterDone.Click();
                    enterDone.Dispose();
                }
                if (ra.Find(ElementAssets.Instance.PaimonMenuRo).IsEmpty())
                {
                    GameCaptureRegion.GameRegion1080PPosClick(1100, 755); 
                    GameCaptureRegion.GameRegion1080PPosClick(1200, 630); 
                }
            },
            ct,
            60,
            1000
        );
        
        await new ReturnMainUiTask().Start(ct);
        
    }
}

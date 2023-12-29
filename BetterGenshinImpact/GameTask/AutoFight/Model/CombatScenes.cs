using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.GameTask.AutoFight.Config;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Sdcb.PaddleOCR;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoFight.Model;

/// <summary>
/// 战斗场景
/// </summary>
public class CombatScenes
{
    /// <summary>
    /// 当前配队
    /// </summary>
    public Character[] Characters { get; set; } = new Character[6];

    /// <summary>
    /// 通过OCR识别队伍内角色
    /// </summary>
    /// <param name="content">完整游戏画面的捕获截图</param>
    public void InitializeTeam(CaptureContent content)
    {
        var captureRect = TaskContext.Instance().SystemInfo.CaptureAreaRect;
        var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
        // 剪裁出队伍区域
       var teamRa = content.CaptureRectArea.Crop(new Rect(captureRect.Width - (int)(355 * assetScale), (int)(220 * assetScale),
            (int)(355 * assetScale), (int)(465 * assetScale)));

       // 识别队伍内角色
       var result = OcrFactory.Paddle.OcrResult(teamRa.SrcGreyMat);
       ParseTeamOcrResult(result);
    }

    private void ParseTeamOcrResult(PaddleOcrResult result)
    {
        foreach (var item in result.Regions)
        {
            if (DefaultAutoFightConfig.CombatAvatarNames.Contains(item.Text))
            {
                Logger.LogInformation("识别到角色：{Text}", item.Text);
            }
        }
    }
}
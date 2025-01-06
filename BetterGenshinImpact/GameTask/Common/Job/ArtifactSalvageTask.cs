using BetterGenshinImpact.GameTask.Common.BgiVision;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.Helpers.Extensions;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.Core.Simulator.Extensions;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 圣遗物自动分解
/// </summary>
public class ArtifactSalvageTask
{
    private readonly ReturnMainUiTask _returnMainUiTask = new();

    public async Task Start(int star, CancellationToken ct)
    {
        await _returnMainUiTask.Start(ct);

        // B键打开背包
        Simulation.SendInput.SimulateAction(GIActions.OpenInventory);
        await Delay(1000, ct);

        var openBagSuccess = await NewRetry.WaitForAction(() =>
        {
            // 选择圣遗物
            using var ra = CaptureToRectArea();
            using var artifactBtn = ra.Find(ElementAssets.Instance.BagArtifactChecked);
            if (artifactBtn.IsEmpty())
            {
                using var artifactBtn2 = ra.Find(ElementAssets.Instance.BagArtifactUnchecked);
                if (artifactBtn2.IsExist())
                {
                    artifactBtn2.Click();
                    return true;
                }
            }
            else
            {
                return true;
            }

            // 如果还在主界面就尝试再按下B键打开背包
            if (Bv.IsInMainUi(ra))
            {
                Debug.WriteLine("背包打开失败,再次尝试打开背包");
                Simulation.SendInput.SimulateAction(GIActions.OpenInventory);
            }

            return false;
        }, ct, 5);

        if (!openBagSuccess)
        {
            Logger.LogError("未找到背包中圣遗物菜单按钮,打开背包失败");
            return;
        }


        await Delay(800, ct);

        // 点击分解按钮打开分解界面
        using var ra2 = CaptureToRectArea();
        using var salvageBtn = ra2.Find(ElementAssets.Instance.BtnArtifactSalvage);
        if (salvageBtn.IsExist())
        {
            salvageBtn.Click();
            await Delay(1000, ct);
        }
        else
        {
            Logger.LogError("未找到圣遗物分解按钮");
            return;
        }

        // 快速选择
        using var ra3 = CaptureToRectArea();
        var ocrList = ra3.FindMulti(RecognitionObject.Ocr(ra3.ToRect().CutLeftBottom(0.25, 0.1)));
        foreach (var ocr in ocrList)
        {
            if (ocr.Text.Contains("快速选择"))
            {
                ocr.Click();
                await Delay(500, ct);
                break;
            }
        }

        // 确认选择
        using var ra4 = CaptureToRectArea();
        var ocrList2 = ra4.FindMulti(RecognitionObject.Ocr(ra3.ToRect().CutLeft(0.35)));
        for (var i = 1; i <= star; i++)
        {
            foreach (var ocr in ocrList2)
            {
                if (ocr.Text.Contains(i + "星"))
                {
                    ocr.Click();
                    await Delay(500, ct);
                    break;
                }
            }
        }

        Bv.ClickWhiteConfirmButton(ra4);
        await Delay(500, ct);

        // 点击分解
        using var ra5 = CaptureToRectArea();
        var salvageBtnConfirm = ra5.Find(ElementAssets.Instance.BtnArtifactSalvageConfirm);
        if (salvageBtnConfirm.IsExist())
        {
            salvageBtnConfirm.Click();
            await Delay(800, ct);
        }
        else
        {
            Logger.LogInformation("未找到圣遗物分解按钮，可能已经没有圣遗物需要分解");
            await _returnMainUiTask.Start(ct);
            return;
        }

        // 点击确认
        using var ra6 = CaptureToRectArea();
        Bv.ClickBlackConfirmButton(ra6);
        Logger.LogInformation("完成{Star}星圣遗物分解", star);
        await Delay(400, ct);

        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);

        await _returnMainUiTask.Start(ct);
    }
}
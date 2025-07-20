using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Core.BgiVision;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.UseRedeemCode.Model;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Extensions;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using Rect = OpenCvSharp.Rect;

namespace BetterGenshinImpact.GameTask.UseRedeemCode;

public class UseRedemptionCodeTask : ISoloTask
{
    private static readonly ILogger _logger = App.GetLogger<UseRedemptionCodeTask>();


    private readonly List<RedeemCode> _list;

    public UseRedemptionCodeTask(List<RedeemCode> list)
    {
        this._list = list;
    }
    
    public UseRedemptionCodeTask(List<string> strList)
    {
        _list = strList
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => new RedeemCode(code, null))
            .ToList();
    }

    public string Name => "使用兑换码";

    public async Task Start(CancellationToken ct)
    {
        InitLog(_list);

        try
        {
            Rect captureRect = TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect;

            await new ReturnMainUiTask().Start(ct);

            var page = new BvPage(ct);

            _logger.LogInformation("使用兑换码: {Msg}", "打开设置");
            // 按ESC键打开菜单
            page.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
            // 等待ESC后菜单出现
            await page.Locator(new BvImage("UseRedeemCode:esc_return_button.png")).WaitFor();
            // 点击设置按钮
            page.Click(45, 825);
            await page.Wait(1000);

            // 点击账户
            _logger.LogInformation("使用兑换码: {Msg}", "点击账户 —— 前往兑换");
            await page.GetByText("账户").WithRoi(captureRect.CutLeft(0.2)).Click();
            await page.Wait(300);

            // 点击前往兑换
            await page.GetByText("前往兑换").WithRoi(captureRect.CutRight(0.3)).Click();

            // 等待兑换码输入框出现
            await page.GetByText("兑换奖励").WaitFor();


            foreach (var redeemCode in _list)
            {
                if (string.IsNullOrEmpty(redeemCode.Code))
                {
                    continue;
                }

                await UseRedeemCode(redeemCode, page);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("使用兑换码时发生错误: {Message}", ex.Message);
            _logger.LogDebug(ex, "使用兑换码时发生错误");
        }
        finally
        {
            // 清空剪贴板
            UIDispatcherHelper.Invoke(Clipboard.Clear);
            // 返回主界面
            await new ReturnMainUiTask().Start(ct);
            
        }
    }

    private async Task UseRedeemCode(RedeemCode redeemCode, BvPage page)
    {
        Rect captureRect = TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect;
        
        _logger.LogInformation("输入兑换码: {Code}", redeemCode.Code);
        // 将要输入的文本复制到剪贴板
        UIDispatcherHelper.Invoke(() => Clipboard.SetDataObject(redeemCode.Code!));
        // 粘贴兑换码
        await page.GetByText("粘贴").WithRoi(captureRect.CutRight(0.5)).Click();
        // 点击兑换
        await page.Locator(ElementAssets.Instance.BtnWhiteConfirm).Click();

        // 兑换成功
        var list = await page.GetByText("兑换成功").TryWaitFor(1000);
        if (list.Count > 0)
        {
            _logger.LogInformation("兑换码 {Code} 兑换成功", redeemCode.Code);
            // 点击确认
            await page.Locator(ElementAssets.Instance.BtnBlackConfirm).Click();
            await page.Wait(5100);
        }
        else
        {
            _logger.LogWarning("兑换码 {Code} 兑换失败，可能是过期、错误或已被使用", redeemCode.Code);
            // 点击清除
            await page.GetByText("清除").WithRoi(captureRect.CutRight(0.5)).Click();
        }
    }


    private static void InitLog(List<RedeemCode> list)
    {
        _logger.LogInformation("开始使用兑换码:");
        foreach (var redeemCode in list)
        {
            if (string.IsNullOrEmpty(redeemCode.Items))
            {
                _logger.LogInformation("{Code}", redeemCode.Code);
            }
            else
            {
                _logger.LogInformation("{Code} - {Msg}", redeemCode.Code, redeemCode.Items);
            }
        }
    }
}
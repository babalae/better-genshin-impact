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
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.GameTask.UseRedeemCode.Model;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Extensions;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using Rect = OpenCvSharp.Rect;

namespace BetterGenshinImpact.GameTask.UseRedeemCode;

/// <summary>
/// 自动使用兑换码：打开设置中的兑换界面，逐个粘贴兑换码并识别兑换结果。
/// </summary>
[SoloTaskInfo(TaskId, "自动使用兑换码", Group = "工具", Order = 420)]
public class UseRedemptionCodeTask : ISoloTask<UseRedemptionCodeParam, NoData>
{
    /// <summary>
    /// 任务标识。
    /// </summary>
    public const string TaskId = "redeem_code";

    /// <summary>
    /// 原因码：有兑换码被游戏拒绝（过期、错误或已被使用）。
    /// </summary>
    public const string CodeRejectedReason = "redeem_code_rejected";

    private static readonly ILogger _logger = App.GetLogger<UseRedemptionCodeTask>();

    /// <summary>
    /// 本次运行使用的运行环境
    /// </summary>
    private readonly GameRuntime _runtime;

    /// <summary>
    /// 本次运行要使用的兑换码，在 RunAsync 开头由参数转换得到
    /// </summary>
    private List<RedeemCode> _list = [];

    /// <summary>
    /// 创建自动使用兑换码任务
    /// </summary>
    /// <param name="runtime">本次运行使用的运行环境</param>
    public UseRedemptionCodeTask(GameRuntime runtime)
    {
        _runtime = runtime;
    }

    /// <summary>
    /// 依次使用参数中的兑换码，并按游戏返回的兑换结果汇总任务结果。
    /// </summary>
    /// <param name="param">兑换码参数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>全部兑换成功返回 Success；部分成功返回 Partial；全部失败或未进入兑换界面返回 Failed。</returns>
    public async Task<SoloTaskResult<NoData>> RunAsync(UseRedemptionCodeParam param, CancellationToken ct)
    {
        // 文本兑换码转换为内部模型，忽略空白项
        _list = (param.Codes ?? [])
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => new RedeemCode(code, null))
            .ToList();
        if (_list.Count == 0)
        {
            return SoloTaskResult.Failed(SoloTaskReasons.InvalidParameter, "没有可使用的兑换码");
        }

        InitLog(_list);

        // 记录每个兑换码的兑换结果，用于最终汇总
        int successCount = 0;
        List<string> rejectedCodes = [];
        try
        {
            Rect captureRect = _runtime.SystemInfo.ScaleMax1080PCaptureRect;

            await new ReturnMainUiTask(_runtime).Start(ct);

            var page = new BvPage(_runtime, ct);

            _logger.LogInformation("使用兑换码: {Msg}", "打开设置");
            // 按ESC键打开菜单
            page.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
            // 等待ESC后菜单出现
            await page.Locator(new BvImage(_runtime, "UseRedeemCode:esc_return_button.png")).WaitFor();
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

            // 逐个使用兑换码
            foreach (var redeemCode in _list)
            {
                if (await UseRedeemCode(redeemCode, page))
                {
                    successCount++;
                }
                else
                {
                    rejectedCodes.Add(redeemCode.Code);
                }
            }
        }
        catch (TimeoutException ex)
        {
            // 识别元素超时说明没有进入预期界面；取消和运行环境停止不在此处捕获，继续向上抛出
            _logger.LogError("使用兑换码时发生错误: {Message}", ex.Message);
            _logger.LogDebug(ex, "使用兑换码时发生错误");
            return SoloTaskResult.Failed(SoloTaskReasons.PageNotReached,
                $"使用兑换码时未识别到目标界面（已兑换成功 {successCount} 个）：{ex.Message}");
        }
        finally
        {
            // 清空剪贴板
            UIDispatcherHelper.Invoke(Clipboard.Clear);
            // 返回主界面
            await new ReturnMainUiTask(_runtime).Start(ct);
        }

        // 按兑换结果汇总
        if (rejectedCodes.Count == 0)
        {
            return SoloTaskResult.Success($"{successCount} 个兑换码全部兑换成功");
        }

        string rejectedText = string.Join("、", rejectedCodes);
        if (successCount == 0)
        {
            return SoloTaskResult.Failed(CodeRejectedReason,
                $"{rejectedCodes.Count} 个兑换码均兑换失败，可能是过期、错误或已被使用：{rejectedText}");
        }

        return SoloTaskResult.Partial(CodeRejectedReason,
            $"兑换成功 {successCount} 个，失败 {rejectedCodes.Count} 个，失败的兑换码可能是过期、错误或已被使用：{rejectedText}");
    }

    /// <summary>
    /// 在兑换界面使用一个兑换码
    /// </summary>
    /// <param name="redeemCode">要使用的兑换码</param>
    /// <param name="page">当前运行环境的页面操作对象</param>
    /// <returns>识别到“兑换成功”时返回 true，否则返回 false</returns>
    private async Task<bool> UseRedeemCode(RedeemCode redeemCode, BvPage page)
    {
        Rect captureRect = _runtime.SystemInfo.ScaleMax1080PCaptureRect;
        
        _logger.LogInformation("输入兑换码: {Code}", redeemCode.Code);
        // 将要输入的文本复制到剪贴板
        UIDispatcherHelper.Invoke(() => Clipboard.SetDataObject(redeemCode.Code));
        // 粘贴兑换码
        await page.GetByText("粘贴").WithRoi(captureRect.CutRight(0.5)).Click();
        // 点击兑换
        await page.Locator(ElementRecognition.Get("BtnWhiteConfirm", _runtime.SystemInfo)).Click();

        // 兑换成功
        var list = await page.GetByText("兑换成功").TryWaitFor(1000);
        if (list.Count > 0)
        {
            _logger.LogInformation("兑换码 {Code} 兑换成功", redeemCode.Code);
            // 点击确认
            await page.Locator(ElementRecognition.Get("BtnBlackConfirm", _runtime.SystemInfo)).Click();
            await page.Wait(5100);
            return true;
        }

        _logger.LogWarning("兑换码 {Code} 兑换失败，可能是过期、错误或已被使用", redeemCode.Code);
        // 点击清除
        await page.GetByText("清除").WithRoi(captureRect.CutRight(0.5)).Click();
        return false;
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

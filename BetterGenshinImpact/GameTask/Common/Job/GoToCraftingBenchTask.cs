using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Runtime;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator.Extensions;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using Microsoft.Extensions.Localization;
using System.Globalization;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.View.Pages.SoloTaskSettings;
using BetterGenshinImpact.ViewModel.SoloTaskSettings;


namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 前往合成台：走到指定国家的合成台并进入合成界面；作为独立任务运行时按保留树脂数合成浓缩树脂。
/// </summary>
[SoloTaskInfo(TaskId, "合成浓缩树脂", Group = "日常奖励", Order = 120, OneDragonOrder = 2)]
[SoloTaskSettingsView(typeof(CraftCondensedResinSettingsView), typeof(CraftCondensedResinSettingsViewModel))]
public class GoToCraftingBenchTask : ISoloTask<CraftCondensedResinParam, NoData>
{
    /// <summary>
    /// 任务标识。
    /// </summary>
    public const string TaskId = "craft_condensed_resin";

    /// <summary>
    /// 原因码：重试全部失败，未能完成前往合成台并合成浓缩树脂的流程。
    /// </summary>
    public const string CraftFailedReason = "craft_failed";

    /// <summary>
    /// 日志中使用的任务名
    /// </summary>
    private const string TaskName = "前往合成台";

    /// <summary>
    /// 整个流程的最大尝试次数
    /// </summary>
    private readonly int _retryTimes = 2;

    /// <summary>
    /// 本次运行使用的运行环境
    /// </summary>
    private readonly GameRuntime _runtime;

    /// <summary>
    /// 配置根
    /// </summary>
    private readonly AllConfig _config;

    /// <summary>
    /// 持续对话并选择选项子任务
    /// </summary>
    private readonly ChooseTalkOptionTask _chooseTalkOptionTask;
    
    private readonly string craftLocalizedString;

    /// <summary>
    /// 创建前往合成台任务
    /// </summary>
    /// <param name="runtime">本次运行使用的运行环境</param>
    /// <param name="config">配置根</param>
    public GoToCraftingBenchTask(GameRuntime runtime, AllConfig config)
    {
        _runtime = runtime;
        _config = config;
        _chooseTalkOptionTask = new ChooseTalkOptionTask(runtime);
        IStringLocalizer<GoToCraftingBenchTask> stringLocalizer = App.GetService<IStringLocalizer<GoToCraftingBenchTask>>() ?? throw new NullReferenceException();
        CultureInfo cultureInfo = new CultureInfo(config.OtherConfig.GameCultureInfoName);
        this.craftLocalizedString = stringLocalizer.WithCultureGet(cultureInfo, "合成");
    }
    
    /// <summary>
    /// 前往合成台合成浓缩树脂，失败时重试整个流程。
    /// </summary>
    /// <param name="param">合成浓缩树脂参数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>参数无效返回 Failed(invalid_parameter)；重试全部失败返回 Failed(craft_failed)；流程正常结束返回 Unverified。取消与运行环境停止向外抛出。</returns>
    public async Task<SoloTaskResult<NoData>> RunAsync(CraftCondensedResinParam param, CancellationToken ct)
    {
        // 国家决定合成台路线文件，为空时无法执行
        if (string.IsNullOrWhiteSpace(param.Country))
        {
            return SoloTaskResult.Failed(SoloTaskReasons.InvalidParameter, "未指定前往合成台的国家。");
        }

        Logger.LogInformation("→ {Name} 开始", TaskName);
        for (int i = 0; i < _retryTimes; i++)
        {
            try
            {
                var message = await CraftResinOnce(param, ct);
                Logger.LogInformation("→ {Name} 结束", TaskName);
                return SoloTaskResult.Unverified(message);
            }
            catch (Exception e) when (e is not NormalEndException and not OperationCanceledException)
            {
                // 取消与运行环境停止（NormalEndException）不参与重试，直接向外抛出
                Logger.LogError("前往合成台领取奖励执行异常：" + e.Message);
                if (i == _retryTimes - 1)
                {
                    // 重试全部失败，以业务失败结果通知调用方
                    return SoloTaskResult.Failed(CraftFailedReason, $"前往合成台合成浓缩树脂失败：{e.Message}");
                }

                await _runtime.Delay(1000, ct);
                Logger.LogInformation("重试前往合成台领取奖励");
            }
        }

        // 循环内每次迭代都会返回，这里只在重试次数配置为 0 时到达
        return SoloTaskResult.Failed(CraftFailedReason, "前往合成台合成浓缩树脂失败：未执行任何尝试。");
    }

    /// <summary>
    /// 执行一次“前往合成台并合成浓缩树脂”的完整流程，结束后返回主界面。
    /// </summary>
    /// <param name="param">合成浓缩树脂参数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>本次流程的结果摘要</returns>
    private async Task<string> CraftResinOnce(CraftCondensedResinParam param, CancellationToken ct)
    {
        string message;
         // 1. 走到合成台并交互
        await GoToCraftingBenchOnce(param.Country, ct);

        // 判断浓缩树脂是否存在
        // TODO 满的情况是怎么样子的
        using var ra = _runtime.CaptureToRectArea();
        var resin = ra.Find(ElementRecognition.Get("CraftCondensedResin", ra));
        
        if (resin.IsExist())
        {
            // 3. 点击合成树脂
            if (param.MinResinToKeep > 0){//开关判断，填写的数量大于0时启用 param.MinResinToKeep
                var fragileResinCount = 0;
                var condensedResinCount = 0;
                var fragileResinCountRa = ra.Find(ElementRecognition.Get("fragileResinCount", ra));
                if (!fragileResinCountRa.IsEmpty())
                {
                    // 图像下方就是脆弱树脂数量
                    var countArea = ra.DeriveCrop(fragileResinCountRa.X, fragileResinCountRa.Y + fragileResinCountRa.Height,
                        fragileResinCountRa.Width, fragileResinCountRa.Height);
                    var count = OcrFactory.Paddle.OcrWithoutDetector(countArea.SrcMat);
                    // Logger.LogInformation("识别原粹树脂数量：{Count}", count);
                    var match = System.Text.RegularExpressions.Regex.Match(count, @"(\d+)\s*[/17]\s*(6|60)");
                    if (match.Success)
                    {
                        var numericPart = match.Groups[1].Value;
                        fragileResinCount = StringUtils.TryParseInt(numericPart);
                        Logger.LogInformation("提取到的原粹树脂数量：{fragileResinCount}", fragileResinCount);
                    }
                }
                
                //浓缩纠缠重试
                var condensed =await NewRetry.WaitForAction(_runtime, () =>
                {
                    var condensedResinCountRa = ra.Find(ElementRecognition.Get("CondensedResinCount", ra));
                    if (!condensedResinCountRa.IsEmpty())
                    {
                        // 图像右侧就是浓缩树脂数量
                        var countArea = ra.DeriveCrop(condensedResinCountRa.X + condensedResinCountRa.Width,
                            condensedResinCountRa.Y, condensedResinCountRa.Width*5/3, condensedResinCountRa.Height);
                        var count = OcrFactory.Paddle.OcrWithoutDetector(countArea.CacheGreyMat);
                        condensedResinCount = StringUtils.TryParseInt(count);
                    }
                    return condensedResinCount >= 0 && condensedResinCount <=5;
                },ct,3,200); 
                if (!condensed)
                {
                    InputHub.Foreground.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
                    await new ReturnMainUiTask(_runtime).Start(ct);
                    throw new Exception($"识别浓缩树脂数量失败: {condensedResinCount}");
                }
                
                // 每次合成消耗的数量
                const int resinConsumedPerCraft = 60;
                // 需要保留的最小数量
                 int minResinToKeep = param.MinResinToKeep;
                // 可以用来合成的树脂数量
                int resinAvailableForCrafting = fragileResinCount - minResinToKeep;
                // 最大可合成次数
                int maxCraftsPossible = 5 - condensedResinCount;
                // 计算需要合成的次数
                int craftsNeeded = resinAvailableForCrafting / resinConsumedPerCraft;
                if (craftsNeeded < 0)
                {
                    craftsNeeded = 0;
                }
                // 计算最大合成次数
                craftsNeeded = Math.Min(maxCraftsPossible, craftsNeeded);
                Logger.LogInformation("原粹树脂: {FragileResinCount}，浓缩树脂: {CondensedResinCount}，最大可合成次数为: {maxCraftsPossible}", fragileResinCount,
                    condensedResinCount, maxCraftsPossible);
                Logger.LogInformation("保留 {MinResinToKeep} 原粹树脂需要合成次数： {craftsNeeded}",minResinToKeep,craftsNeeded);
                if (craftsNeeded > 0)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        Bv.ClickReduceButton(ra);
                        await _runtime.Delay(150, ct);
                    }
                    await _runtime.Delay(300, ct);
                    for (int i = 0; i < craftsNeeded-1; i++)
                    {
                        Bv.ClickAddButton(ra);
                        await _runtime.Delay(150, ct);
                    }
                    await _runtime.Delay(200, ct);
                    //await Delay(100000, ct);//调试延时=========
                    Bv.ClickWhiteConfirmButton(ra);
                    Logger.LogInformation("合成{Text}", "浓缩树脂");
                    await _runtime.Delay(300, ct);
                    using var confirmCapture = _runtime.CaptureToRectArea();
                    Bv.ClickBlackConfirmButton(confirmCapture);
                    message = $"已合成浓缩树脂 {craftsNeeded} 次。";
                }
                else
                {
                    Logger.LogInformation("无需合成浓缩树脂");
                    message = "无需合成浓缩树脂。";
                }
            }
            else
            {
                //await Delay(100000, ct);//调试延时=========
                Bv.ClickWhiteConfirmButton(ra);
                Logger.LogInformation("合成{Text}", "浓缩树脂");
                await _runtime.Delay(300, ct);
                using var confirmCapture = _runtime.CaptureToRectArea();
                Bv.ClickBlackConfirmButton(confirmCapture);
                message = "已合成浓缩树脂。";
            }
            await _runtime.Delay(1300, ct);
            // 直接ESC退出即可
            InputHub.Foreground.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
        }
        else
        {
            Logger.LogInformation("无需合成浓缩树脂");
            message = "无需合成浓缩树脂。";
        }

        await new ReturnMainUiTask(_runtime).Start(ct);
        return message;
    }

    /// <summary>
    /// 前往合成台并进入合成界面，失败时重试。不执行合成。
    /// </summary>
    /// <param name="country">前往哪个国家的合成台</param>
    /// <param name="ct">取消令牌</param>
    /// <remarks>重试全部失败时抛出最后一次的异常。</remarks>
    public async Task GoToCraftingBench(string country, CancellationToken ct)
    {
        Logger.LogInformation("→ {Name} 开始", TaskName);
        for (int i = 0; i < _retryTimes; i++)
        {
            try
            {
                await GoToCraftingBenchOnce(country, ct);
                break;
            }
            catch (Exception e)
            {
                Logger.LogError("前往合成台执行异常：" + e.Message);
                if (i == _retryTimes - 1)
                {
                    throw;
                }

                await _runtime.Delay(1000, ct);
                Logger.LogInformation("重试前往合成台");
            }
        }

        Logger.LogInformation("→ {Name} 结束", TaskName);
    }

    /// <summary>
    /// 执行一次前往合成台并进入合成界面，不重试。
    /// </summary>
    /// <param name="country">前往哪个国家的合成台</param>
    /// <param name="ct">取消令牌</param>
    public async Task GoToCraftingBenchOnce(string country, CancellationToken ct)
    {
        var task = PathingTask.BuildFromFilePath(Global.Absolute(@$"GameTask\Common\Element\Assets\Json\合成台_{country}.json"));
        if (task == null)
        {
            throw new Exception("地图追踪文件加载失败");
        }

        var pathingTask = new PathExecutor(_runtime, _config, ct)
        {
            PartyConfig = new PathingPartyConfig
            {
                Enabled = true,
                AutoSkipEnabled = true,
                AutoRunEnabled = country != "枫丹",
            },
            EndAction = region => Bv.FindFAndPress(region, text: this.craftLocalizedString)
        };
        // 找到合成台时允许 EndAction 提前结束路线，随后仍核验合成台交互界面。
        _ = await pathingTask.Pathing(task);

        await _runtime.Delay(700, ct);
        
        // 多种尝试 责任链
        if (!IsInCraftingTalkUi())
        {
            // 直接重试
            await TryPressCrafting(ct);
            
            if (!IsInCraftingTalkUi())
            {
                // 往回走一步重试
                InputHub.Foreground.SimulateAction(GIActions.MoveBackward, KeyType.KeyDown);
                await _runtime.Delay(200, ct);
                InputHub.Foreground.SimulateAction(GIActions.MoveBackward, KeyType.KeyUp);
                
                await TryPressCrafting(ct);
            
                // 最后 check
                if (!IsInCraftingTalkUi())
                {
                    throw new Exception("未进入和合成台交互对话界面");
                }
            
            }
        }

        // 等待进入合成界面
        await _chooseTalkOptionTask.SelectLastOptionUntilEnd(ct,
            region => region.Find(ElementRecognition.Get("BtnWhiteConfirm", region)).IsExist()
        );
        await _runtime.Delay(800, ct);
    }


    private bool IsInCraftingTalkUi()
    {
        using var ra = _runtime.CaptureToRectArea();
        return Bv.IsInTalkUi(ra);
    }
    
    private async Task<bool> TryPressCrafting( CancellationToken ct)
    {
        using var ra1 = _runtime.CaptureToRectArea();
        var res = Bv.FindFAndPress(ra1, text: this.craftLocalizedString);
        if (res)
        {
            await _runtime.Delay(1000, ct);
        }
        return res;
    }
}

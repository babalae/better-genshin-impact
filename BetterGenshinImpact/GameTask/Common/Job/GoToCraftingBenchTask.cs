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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Newtonsoft.Json;


namespace BetterGenshinImpact.GameTask.Common.Job;

public class GoToCraftingBenchTask
{
    private static readonly string OneDragonFlowConfigFolder = Global.Absolute(@"User\OneDragon");
    
    public string Name => "前往合成台";

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
    
    private  OneDragonFlowConfig? SelectedConfig;
    private ObservableCollection<OneDragonFlowConfig> ConfigList = [];
    
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
    
    public async Task GoCraftResin(string country, CancellationToken ct)
    {
        Logger.LogInformation("→ {Name} 开始", Name);
        for (int i = 0; i < _retryTimes; i++)
        {
            try
            {
                await GoCraftResinOnce(country, ct);
                break;
            }
            catch (Exception e)
            {
                Logger.LogError("前往合成台领取奖励执行异常：" + e.Message);
                if (i == _retryTimes - 1)
                {
                    // 通知失败
                    throw;
                }
                else
                {
                    await _runtime.Delay(1000, ct);
                    Logger.LogInformation("重试前往合成台领取奖励");
                }
            }
        }

        Logger.LogInformation("→ {Name} 结束", Name);
    }

    public async Task GoCraftResinOnce(string country, CancellationToken ct)
    {
         // 1. 走到合成台并交互
        await GoToCraftingBenchOnce(country, ct);

        // 判断浓缩树脂是否存在
        // TODO 满的情况是怎么样子的
        using var ra = _runtime.CaptureToRectArea();
        var resin = ra.Find(ElementRecognition.Get("CraftCondensedResin", ra));
        
        if (resin.IsExist())
        {
            InitConfigList();
            // 3. 点击合成树脂
            if (SelectedConfig?.MinResinToKeep > 0){//开关判断，填写的数量大于0时启用 SelectedConfig.MinResinToKeep
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
                 int minResinToKeep = SelectedConfig.MinResinToKeep;
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
                }
                else
                {
                    Logger.LogInformation("无需合成浓缩树脂");
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
            }
            await _runtime.Delay(1300, ct);
            // 直接ESC退出即可
            InputHub.Foreground.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
        }
        else
        {
            Logger.LogInformation("无需合成浓缩树脂");
        }

        await new ReturnMainUiTask(_runtime).Start(ct);
    }

    /// <summary>
    /// 前往合成台
    /// </summary>
    /// <param name="country"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task GoToCraftingBench(string country, CancellationToken ct)
    {
        Logger.LogInformation("→ {Name} 开始", Name);
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

        Logger.LogInformation("→ {Name} 结束", Name);
    }

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
    
    private void InitConfigList()
    {
        Directory.CreateDirectory(OneDragonFlowConfigFolder);
        // 读取文件夹内所有json配置，按创建时间正序
        var configFiles = Directory.GetFiles(OneDragonFlowConfigFolder, "*.json");
        var configs = new List<OneDragonFlowConfig>();

        OneDragonFlowConfig? selected = null;
        foreach (var configFile in configFiles)
        {
            var json = File.ReadAllText(configFile);
            var config = JsonConvert.DeserializeObject<OneDragonFlowConfig>(json);
            if (config != null)
            {
                configs.Add(config);
                if (config.Name == _config.SelectedOneDragonFlowConfigName)
                {
                    selected = config;
                }
            }
        }

        if (selected == null)
        {
            if (configs.Count > 0)
            {
                selected = configs[0];
            }
            else
            {
                selected = new OneDragonFlowConfig
                {
                    Name = "默认配置"
                };
                configs.Add(selected);
            }
        }

        ConfigList.Clear();
        foreach (var config in configs)
        {
            ConfigList.Add(config);
        }

        SelectedConfig = selected;
    }
}

using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
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

    private readonly ChooseTalkOptionTask _chooseTalkOptionTask = new();
    
    private  OneDragonFlowConfig? SelectedConfig;
    private ObservableCollection<OneDragonFlowConfig> ConfigList = [];
    
    private readonly string craftLocalizedString;

    public GoToCraftingBenchTask()
    {
        IStringLocalizer<GoToCraftingBenchTask> stringLocalizer = App.GetService<IStringLocalizer<GoToCraftingBenchTask>>() ?? throw new NullReferenceException();
        CultureInfo cultureInfo = new CultureInfo(TaskContext.Instance().Config.OtherConfig.GameCultureInfoName);
        this.craftLocalizedString = stringLocalizer.WithCultureGet(cultureInfo, "合成");
    }
    
    public async Task Start(string country, CancellationToken ct)
    {
        Logger.LogInformation("→ {Name} 开始", Name);
        for (int i = 0; i < _retryTimes; i++)
        {
            try
            {
                await DoOnce(country, ct);
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
                    await Delay(1000, ct);
                    Logger.LogInformation("重试前往合成台领取奖励");
                }
            }
        }

        Logger.LogInformation("→ {Name} 结束", Name);
    }

    public async Task DoOnce(string country, CancellationToken ct)
    {
         // 1. 走到合成台并交互
        await GoToCraftingBench(country, ct);

        // 2. 等待合成界面
        await _chooseTalkOptionTask.SelectLastOptionUntilEnd(ct,
            region => region.Find(ElementAssets.Instance.BtnWhiteConfirm).IsExist()
        );
        await Delay(800, ct);
        
        // 判断浓缩树脂是否存在
        // TODO 满的情况是怎么样子的
        var ra = CaptureToRectArea();
        var resin = ra.Find(ElementAssets.Instance.CraftCondensedResin);
        
        if (resin.IsExist())
        {
            InitConfigList();
            // 3. 点击合成树脂
            if (SelectedConfig.MinResinToKeep > 0){//开关判断，填写的数量大于0时启用 SelectedConfig.MinResinToKeep
                var fragileResinCount = 0;
                var condensedResinCount = 0;
                var fragileResinCountRa = ra.Find(ElementAssets.Instance.fragileResinCount);
                if (!fragileResinCountRa.IsEmpty())
                {
                    // 图像下方就是脆弱树脂数量
                    var countArea = ra.DeriveCrop(fragileResinCountRa.X, fragileResinCountRa.Y + fragileResinCountRa.Height,
                        fragileResinCountRa.Width, fragileResinCountRa.Height/3);
                    var count = OcrFactory.Paddle.Ocr(countArea.CacheGreyMat);
                    fragileResinCount = StringUtils.TryParseInt(count);
                }
                var condensedResinCountRa = ra.Find(ElementAssets.Instance.CondensedResinCount);
                if (!condensedResinCountRa.IsEmpty())
                {
                    // 图像右侧就是浓缩树脂数量
                    var countArea = ra.DeriveCrop(condensedResinCountRa.X + condensedResinCountRa.Width,
                        condensedResinCountRa.Y, condensedResinCountRa.Width*5/3, condensedResinCountRa.Height);
                    var count = OcrFactory.Paddle.OcrWithoutDetector(countArea.CacheGreyMat);
                    condensedResinCount = StringUtils.TryParseInt(count);
                }
                //todo 可加纠错机制判断树脂数量是否正确
                // 每次合成消耗的数量
                const int resinConsumedPerCraft = 40;
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
                        await Delay(150, ct);
                    }
                    await Delay(300, ct);
                    for (int i = 0; i < craftsNeeded-1; i++)
                    {
                        Bv.ClickAddButton(ra);
                        await Delay(150, ct);
                    }
                    await Delay(200, ct);
                    //await Delay(100000, ct);//调试延时=========
                    Bv.ClickWhiteConfirmButton(ra);
                    Logger.LogInformation("合成{Text}", "浓缩树脂");
                    await Delay(300, ct);
                    Bv.ClickBlackConfirmButton(CaptureToRectArea());
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
                await Delay(300, ct);
                Bv.ClickBlackConfirmButton(CaptureToRectArea());
            }
            await Delay(1300, ct);
            // 直接ESC退出即可
            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
        }
        else
        {
            Logger.LogInformation("无需合成浓缩树脂");
        }

        await new ReturnMainUiTask().Start(ct);
    }

    /// <summary>
    /// 前往合成台
    /// </summary>
    /// <param name="country"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task GoToCraftingBench(string country, CancellationToken ct)
    {
        var task = PathingTask.BuildFromFilePath(Global.Absolute(@$"GameTask\Common\Element\Assets\Json\合成台_{country}.json"));

        var pathingTask = new PathExecutor(ct)
        {
            PartyConfig = new PathingPartyConfig
            {
                Enabled = true,
                AutoSkipEnabled = true,
                AutoRunEnabled = country != "枫丹",
            },
            EndAction = region => Bv.FindFAndPress(region, text: this.craftLocalizedString)
        };
        await pathingTask.Pathing(task);

        await Delay(700, ct);
        
        // 多种尝试 责任链
        if (!IsInCraftingTalkUi())
        {
            // 直接重试
            await TryPressCrafting(ct);
            
            if (!IsInCraftingTalkUi())
            {
                // 往回走一步重试
                Simulation.SendInput.SimulateAction(GIActions.MoveBackward, KeyType.KeyDown);
                await Delay(200, ct);
                Simulation.SendInput.SimulateAction(GIActions.MoveBackward, KeyType.KeyUp);
                
                await TryPressCrafting(ct);
            
                // 最后 check
                if (!IsInCraftingTalkUi())
                {
                    throw new Exception("未进入和合成台交互对话界面");
                }
            
            }
        }
    }


    private bool IsInCraftingTalkUi()
    {
        using var ra = CaptureToRectArea();
        return Bv.IsInTalkUi(ra);
    }
    
    private async Task<bool> TryPressCrafting( CancellationToken ct)
    {
        using var ra1 = CaptureToRectArea();
        var res = Bv.FindFAndPress(ra1, text: this.craftLocalizedString);
        if (res)
        {
            await Delay(1000, ct);
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
                if (config.Name == TaskContext.Instance().Config.SelectedOneDragonFlowConfigName)
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
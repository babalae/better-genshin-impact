using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoWood.Assets;
using BetterGenshinImpact.GameTask.AutoWood.Utils;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Genshin.Settings;
using BetterGenshinImpact.View.Drawable;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator.Extensions;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using static Vanara.PInvoke.User32;
using GC = System.GC;

namespace BetterGenshinImpact.GameTask.AutoWood;

/// <summary>
/// 自动伐木
/// </summary>
public partial class AutoWoodTask : ISoloTask
{
    public string Name => "自动伐木";

    private AutoWoodAssets _assets;

    private bool _first = true;

    private WoodStatisticsPrinter _printer;

    private readonly Login3rdParty _login3rdParty;

    // private VK _zKey = VK.VK_Z;

    private readonly WoodTaskParam _taskParam;

    private CancellationToken _ct;

    public AutoWoodTask(WoodTaskParam taskParam)
    {
        this._taskParam = taskParam;
        _login3rdParty = new();
        AutoWoodAssets.DestroyInstance();
    }

    public Task Start(CancellationToken ct)
    {
        _assets = AutoWoodAssets.Instance;
        _printer = new WoodStatisticsPrinter(_assets);
        var runTimeWatch = new Stopwatch();
        _ct = ct;
        _printer.Ct = _ct;

        try
        {
            Kernel32.SetThreadExecutionState(Kernel32.EXECUTION_STATE.ES_CONTINUOUS | Kernel32.EXECUTION_STATE.ES_SYSTEM_REQUIRED | Kernel32.EXECUTION_STATE.ES_DISPLAY_REQUIRED);
            Logger.LogInformation("→ {Text} 设置伐木总次数：{Cnt}，设置木材数量上限：{MaxCnt}", "自动伐木，启动！", _taskParam.WoodRoundNum, _taskParam.WoodDailyMaxCount);

            _login3rdParty.RefreshAvailabled();
            if (_login3rdParty.Type == Login3rdParty.The3rdPartyType.Bilibili)
            {
                Logger.LogInformation("自动伐木启用B服模式");
            }

            // SettingsContainer settingsContainer = new();
            //
            // if (settingsContainer.OverrideController?.KeyboardMap?.ActionElementMap.Where(item => item.ActionId == ActionId.Gadget).FirstOrDefault()?.ElementIdentifierId is ElementIdentifierId key)
            // {
            //     if (key != ElementIdentifierId.Z)
            //     {
            //         _zKey = key.ToVK();
            //         Logger.LogInformation($"自动伐木检测到用户改键 {ElementIdentifierId.Z.ToName()} 改为 {key.ToName()}");
            //         if (key == ElementIdentifierId.LeftShift || key == ElementIdentifierId.RightShift)
            //         {
            //             Logger.LogInformation($"用户改键 {key.ToName()} 可能不受模拟支持，若使用正常则忽略");
            //         }
            //     }
            // }

            SystemControl.ActivateWindow();
            // 伐木开始计时
            runTimeWatch.Start();
            for (var i = 0; i < _taskParam.WoodRoundNum; i++)
            {
                if (TaskContext.Instance().Config.AutoWoodConfig.WoodCountOcrEnabled)
                {
                    if (_printer.WoodStatisticsAlwaysEmpty())
                    {
                        Logger.LogInformation("连续{Cnt}次获取木材数量为0。判定附近没有能响应「王树瑞佑」的树木！或者已达每日数量上限", _printer.NothingCount);
                        break;
                    }

                    if (_printer.ReachedWoodMaxCount)
                    {
                        Logger.LogInformation("{Names}已达到设置的上限：{MaxCnt}", _printer.WoodTotalDict.Keys, _taskParam.WoodDailyMaxCount);
                        break;
                    }
                }

                Logger.LogInformation("第{Cnt}次伐木", i + 1);
                if (_ct.IsCancellationRequested)
                {
                    break;
                }

                Felling(_taskParam, i + 1 == _taskParam.WoodRoundNum);
                VisionContext.Instance().DrawContent.ClearAll();
                Sleep(500, _ct);
            }

            return Task.CompletedTask;
        }
        finally
        {
            // 伐木结束计时
            runTimeWatch.Stop();
            Kernel32.SetThreadExecutionState(Kernel32.EXECUTION_STATE.ES_CONTINUOUS);
            var elapsedTime = runTimeWatch.Elapsed;
            Logger.LogInformation(@"本次伐木总耗时：{Time:hh\:mm\:ss}", elapsedTime);
        }
    }

    private partial class WoodStatisticsPrinter(AutoWoodAssets assert)
    {
        public bool ReachedWoodMaxCount;
        public int NothingCount;
        public readonly ConcurrentDictionary<string, int> WoodTotalDict = new();

        private bool _firstWoodOcr = true;
        private string _firstWoodOcrText = "";
        private readonly Dictionary<string, int> _woodMetricsDict = [];
        private readonly Dictionary<string, bool> _woodNotPrintDict = [];

        // from:https://api-static.mihoyo.com/common/blackboard/ys_obc/v1/home/content/list?app_sn=ys_obc&channel_id=13
        private static readonly List<string> ExistWoods =
        [
            "悬铃木", "白梣木", "炬木", "椴木", "香柏木", "刺葵木", "柽木", "辉木", "业果木", "证悟木", "枫木", "垂香木",
            "杉木", "竹节", "却砂木", "松木", "萃华木", "桦木", "孔雀木", "梦见木", "御伽木",
            "燃爆木", "桃椰子木", "灰灰楼林木", "白栗栎木"
        ];

        public CancellationToken Ct { get; set; }

        [GeneratedRegex("([^\\d\\n]+)[×x](\\d+)")]
        private static partial Regex _parseWoodStatisticsRegex();

        public bool WoodStatisticsAlwaysEmpty()
        {
            return NothingCount >= 3;
        }

        public void PrintWoodStatistics(WoodTaskParam taskParam)
        {
            var woodStatisticsText = GetWoodStatisticsText(taskParam);
            if (string.IsNullOrEmpty(woodStatisticsText))
            {
                NothingCount++;
                Logger.LogWarning("未能识别到伐木的统计数据");
                if (_woodMetricsDict.Count == 0)
                {
                    TaskContext.Instance().Config.AutoWoodConfig.WoodCountOcrEnabled = false;
                    throw new NormalEndException("首次伐木就未识别到木材数据，已经自动关闭【OCR识别并累计木材数】的功能，请重新启动【自动伐木】功能！");
                }

                return;
            }

            ParseWoodStatisticsText(taskParam, woodStatisticsText);
            CheckAndPrintWoodQuantities(taskParam);
        }

        private string GetWoodStatisticsText(WoodTaskParam taskParam)
        {
            var firstOcrResultList = new List<string>();
            // 创建一个计时器，循环识别文本，直到超时
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < 3500)
            {
                // OCR识别木材文本
                var recognizedText = WoodTextAreaOcr();
                if (_firstWoodOcr)
                {
                    // 首次时会重复OCR识别，然后找到最好的OCR结果（即最长的那个）
                    var isFound = HasDetectedWoodText(recognizedText);
                    if (isFound) firstOcrResultList.Add(recognizedText);
                    if (firstOcrResultList.Count != 0 && !isFound) break;
                    SleepDurationBetweenOcrs(taskParam);
                }
                else
                {
                    var isFound = HasDetectedWoodText(recognizedText);
                    if (!isFound)
                    {
                        SleepDurationBetweenOcrs(taskParam);
                        continue;
                    }

                    NothingCount = 0;
                    // 等待伐木的木材数量显示全，再次OCR识别。
                    // SleepDurationBetweenOcrs(taskParam);
                    // return WoodTextAreaOcr();

                    // 直接返回首次的识别结果
                    return _firstWoodOcrText;
                }
            }

            stopwatch.Stop(); // 停止计时
            _firstWoodOcrText = FindBestOcrResult(firstOcrResultList);
            return _firstWoodOcrText;
        }

        private void SleepDurationBetweenOcrs(WoodTaskParam taskParam)
        {
            Sleep(_firstWoodOcr ? 300 : 100, Ct);
        }

        private string WoodTextAreaOcr()
        {
            // OCR识别文本区域
            var woodCountRect = CaptureToRectArea().DeriveCrop(assert.WoodCountUpperRect);
            return OcrFactory.Paddle.Ocr(woodCountRect.SrcMat);
        }

        private bool HasDetectedWoodText(string recognizedText)
        {
            if (!_firstWoodOcr)
            {
                return !string.IsNullOrEmpty(recognizedText) &&
                       recognizedText.Contains("获得");
            }

            return !string.IsNullOrEmpty(recognizedText) &&
                   recognizedText.Contains("获得") &&
                   (recognizedText.Contains('×') || recognizedText.Contains('x'));
        }

        private void ParseWoodStatisticsText(WoodTaskParam taskParam, string text)
        {
            // 从识别的文本中提取木材名称和数量
            // 格式示例："获得\n竹节×30\n杉木×20"
            if (!text.Contains('×') && !text.Contains('X'))
            {
                Logger.LogWarning("未能正确解析木材信息格式：{woodText}", text);
                return;
            }

            // 匹配模式 "名称×数量"
            var matches = _parseWoodStatisticsRegex().Matches(text);

            // 如果OCR识别木材的种类小于等于首次保存的一样时，直接使用首次的木材数量。
            if (!_firstWoodOcr && 1 <= matches.Count && matches.Count <= _woodMetricsDict.Count)
            {
                foreach (var entry in _woodMetricsDict.Where(entry => entry.Value <= taskParam.WoodDailyMaxCount))
                {
                    UpdateWoodCount(entry.Key, entry.Value);
                }
            }
            else
            {
                foreach (Match match in matches)
                {
                    if (match.Success)
                    {
                        var materialName = match.Groups[1].Value.Trim();
                        var quantityStr = match.Groups[2].Value.Trim();
                        var quantity = int.Parse(quantityStr);
                        Debug.WriteLine($"首次获取木材的名称：{materialName}, 数量：{quantity}");
                        UpdateWoodCount(materialName, quantity);
                    }
                    else
                    {
                        Logger.LogWarning("识别到的数量不是有效的整数：{woodText}", text);
                    }
                }

                // 所有数据都保存一遍后，首次OCR识别结束
                _firstWoodOcr = false;
            }
        }

        private void UpdateWoodCount(string materialName, int quantity)
        {
            // 检查字典中是否已包含这种木材名称
            if (!ExistWoods.Contains(materialName))
            {
                Logger.LogWarning("未知的木材名：{woodName}，数量{Cnt}", materialName, quantity);
                return;
            }

            WoodTotalDict.AddOrUpdate(
                key: materialName,
                addValue: quantity,
                updateValueFactory: (_, existingValue) => existingValue + quantity
            );
            if (_firstWoodOcr)
            {
                // 记录木材单次获取的值
                _woodMetricsDict.TryAdd(materialName, quantity);
            }
        }

        private static string FindBestOcrResult(List<string> firstOcrResultList)
        {
            // return firstOcrResultList.Count == 0 ? "" : firstOcrResultList.OrderByDescending(s => s.Length).First();
            if (firstOcrResultList.Count == 0) return "";

            // 先排序再查找
            var sortedOcrResults = firstOcrResultList.OrderByDescending(s => s.Length).ToList();
            int? targetLength = null;

            foreach (var ocrResult in sortedOcrResults)
            {
                if (targetLength == null)
                {
                    targetLength = ocrResult.Length;
                }
                else if (ocrResult.Length != targetLength)
                {
                    // 如果当前结果长度与第一个匹配项的长度不同，则跳过
                    continue;
                }

                // 分解 OCR 结果中的多个条目
                var matches = _parseWoodStatisticsRegex().Matches(ocrResult);
                var isFound = true;
                foreach (Match match in matches)
                {
                    if (!match.Success)
                    {
                        isFound = false;
                        continue;
                    }

                    var materialName = match.Groups[1].Value.Trim();
                    Debug.WriteLine($"第一次获取的木材名称：{materialName}");
                    if (!ExistWoods.Contains(materialName))
                    {
                        isFound = false;
                    }
                }

                if (isFound) return ocrResult;
            }

            // 如果没有找到匹配的结果
            return "";
        }

        private void CheckAndPrintWoodQuantities(WoodTaskParam taskParam)
        {
            if (WoodTotalDict.IsEmpty)
            {
                ReachedWoodMaxCount = false;
                NothingCount++;
                return;
            }

            foreach (var entry in WoodTotalDict)
            {
                if (_woodNotPrintDict.GetValueOrDefault(entry.Key)) continue;
                // 打印每个条目的键（木材名称）和值（数量）
                Logger.LogInformation("木材{woodName}累积获取数量：{Cnt}", entry.Key, entry.Value);

                // 检查木材是否超过上限
                if (entry.Value < taskParam.WoodDailyMaxCount) continue;
                Logger.LogInformation("木材{Name}已达到数量设置的上限：{Count}", entry.Key, taskParam.WoodDailyMaxCount);
                _woodNotPrintDict.TryAdd(entry.Key, true);
            }

            // 如果木材统计的最小值都大于设置的上限，则停止伐木
            ReachedWoodMaxCount = WoodTotalDict.Values.Min() >= taskParam.WoodDailyMaxCount;
        }
    }

    private void Felling(WoodTaskParam taskParam, bool isLast = false)
    {
        // 1. 按 z 触发「王树瑞佑」
        PressZ(taskParam);

        if (isLast)
        {
            return;
        }

        // 打印伐木的统计数据（可选）
        if (TaskContext.Instance().Config.AutoWoodConfig.WoodCountOcrEnabled)
        {
            _printer.PrintWoodStatistics(taskParam);
            if (_printer.WoodStatisticsAlwaysEmpty() || _printer.ReachedWoodMaxCount) return;
        }

        // 2. 按下 ESC 打开菜单 并退出游戏
        PressEsc(taskParam);

        // 3. 等待进入游戏
        EnterGame(taskParam);

        // 手动 GC
        GC.Collect();
    }

    private void PressZ(WoodTaskParam taskParam)
    {
        // IMPORTANT: MUST try focus before press Z
        SystemControl.FocusWindow(TaskContext.Instance().GameHandle);

        if (_first)
        {
            using var contentRegion = CaptureToRectArea();
            using var ra = contentRegion.Find(_assets.TheBoonOfTheElderTreeRo);
            if (ra.IsEmpty())
            {
#if !TEST_WITHOUT_Z_ITEM
                throw new NormalEndException("请先装备小道具「王树瑞佑」！如果已经装备仍旧出现此提示，请重新仔细阅读文档中的《快速上手》！");
#else
                System.Threading.Thread.Sleep(2000);
                Simulation.SendInput.SimulateAction(GIActions.QuickUseGadget);
                Debug.WriteLine("[AutoWood] Z");
                _first = false;
#endif
            }
            else
            {
                Simulation.SendInput.SimulateAction(GIActions.QuickUseGadget);
                Debug.WriteLine("[AutoWood] Z");
                _first = false;
            }
        }
        else
        {
            NewRetry.Do(() =>
            {
                Sleep(1, _ct);
                using var contentRegion = CaptureToRectArea();
                using var ra = contentRegion.Find(_assets.TheBoonOfTheElderTreeRo);
                if (ra.IsEmpty())
                {
#if !TEST_WITHOUT_Z_ITEM
                    throw new RetryException("未找到「王树瑞佑」");
#else
                    System.Threading.Thread.Sleep(15000);
#endif
                }

                Simulation.SendInput.SimulateAction(GIActions.QuickUseGadget);
                Debug.WriteLine("[AutoWood] Z");
                Sleep(500, _ct);
            }, TimeSpan.FromSeconds(1), 120);
        }

        Sleep(300, _ct);
        Sleep(TaskContext.Instance().Config.AutoWoodConfig.AfterZSleepDelay, _ct);
    }

    private void PressEsc(WoodTaskParam taskParam)
    {
        SystemControl.FocusWindow(TaskContext.Instance().GameHandle);
        Simulation.SendInput.Keyboard.KeyPress(VK.VK_ESCAPE);
        // if (TaskContext.Instance().Config.AutoWoodConfig.PressTwoEscEnabled)
        // {
        //     Sleep(1500, _cts);
        //     Simulation.SendInput.Keyboard.KeyPress(VK.VK_ESCAPE);
        // }
        Debug.WriteLine("[AutoWood] Esc");
        Sleep(800, _ct);
        // 确认在菜单界面
        try
        {
            NewRetry.Do(() =>
            {
                Sleep(1, _ct);
                using var contentRegion = CaptureToRectArea();
                using var ra = contentRegion.Find(_assets.MenuBagRo);
                if (ra.IsEmpty())
                {
                    Simulation.SendInput.Keyboard.KeyPress(VK.VK_ESCAPE);
                    throw new RetryException("未检测到弹出菜单");
                }
            }, TimeSpan.FromSeconds(1.2), 5);
        }
        catch (Exception e)
        {
            Logger.LogInformation(e.Message);
            Logger.LogInformation("仍旧点击退出按钮");
        }

        // 点击退出
        GameCaptureRegion.GameRegionClick((size, scale) => (50 * scale, size.Height - 50 * scale));

        Debug.WriteLine("[AutoWood] Click exit button");

        Sleep(500, _ct);

        // 点击退出到主界面确认
        using var contentRegion = CaptureToRectArea();
        contentRegion.Find(_assets.ConfirmRo, ra =>
        {
            ra.Click();
            Debug.WriteLine("[AutoWood] Click confirm button");
            ra.Dispose();
        });
    }

    private void EnterGame(WoodTaskParam taskParam)
    {
        if (_login3rdParty.IsAvailabled)
        {
            Sleep(1, _ct);
            _login3rdParty.Login(_ct);
        }

        var clickCnt = 0;
        for (var i = 0; i < 50; i++)
        {
            Sleep(1, _ct);

            using var contentRegion = CaptureToRectArea();
            using var ra = contentRegion.Find(_assets.EnterGameRo);
            if (!ra.IsEmpty())
            {
                clickCnt++;
                GameCaptureRegion.GameRegion1080PPosClick(955, 666);
                Debug.WriteLine("[AutoWood] Click entry");
            }
            else
            {
                if (clickCnt > 2)
                {
                    Sleep(5000, _ct);
                    break;
                }
            }

            Sleep(1000, _ct);
        }

        if (clickCnt == 0)
        {
            throw new RetryException("未检测进入游戏界面");
        }
    }
}
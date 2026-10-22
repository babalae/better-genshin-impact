using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoWood.Utils;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Genshin.Settings;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
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
[SoloTaskInfo(TaskId, "自动伐木", Group = "采集与生活", Order = 310)]
public partial class AutoWoodTask : ISoloTask<WoodTaskParam, NoData>
{
    /// <summary>
    /// 稳定任务标识
    /// </summary>
    public const string TaskId = "auto_wood";

    /// <summary>
    /// 本次运行使用的运行环境
    /// </summary>
    private readonly GameRuntime _runtime;

    /// <summary>
    /// 配置根
    /// </summary>
    private readonly AllConfig _config;

    private bool _first = true;

    /// <summary>
    /// 木材统计，在 <see cref="RunAsync"/> 开头创建
    /// </summary>
    private WoodStatisticsPrinter _printer = null!;

    private readonly Login3rdParty _login3rdParty;

    // private VK _zKey = VK.VK_Z;

    /// <summary>
    /// 本次运行的伐木参数，在 <see cref="RunAsync"/> 开头赋值
    /// </summary>
    private WoodTaskParam _taskParam = null!;

    private CancellationToken _ct;

    /// <summary>
    /// 进出千星奇域刷新 CD 的作业，在 <see cref="RunAsync"/> 开头创建
    /// </summary>
    private EnterAndExitWonderlandJob _enterAndExitWonderlandJob = null!;

    /// <summary>
    /// 创建自动伐木任务
    /// </summary>
    /// <param name="runtime">本次运行使用的运行环境</param>
    /// <param name="config">配置根</param>
    public AutoWoodTask(GameRuntime runtime, AllConfig config)
    {
        _runtime = runtime;
        _config = config;
        _login3rdParty = new(runtime);
    }

    private static RecognitionObject GetRecognitionObject(string objectName, Region region)
    {
        return RecognitionAssets.Get("AutoWood", objectName, region);
    }

    /// <summary>
    /// 运行自动伐木。未装备「王树瑞佑」、首次伐木未识别到木材时抛出携带失败结果的 <see cref="SoloTaskAbortException"/>；
    /// 取消时抛出异常
    /// </summary>
    /// <param name="param">伐木参数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>任务结果：木材达到上限返回成功，其余正常结束返回未确认</returns>
    public async Task<SoloTaskResult<NoData>> RunAsync(WoodTaskParam param, CancellationToken ct)
    {
        _taskParam = param;
        _printer = new WoodStatisticsPrinter(_runtime, _config);
        _enterAndExitWonderlandJob = new EnterAndExitWonderlandJob(_runtime);
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

            _runtime.Window.Activate();
            // 伐木开始计时
            runTimeWatch.Start();
            for (var i = 0; i < _taskParam.WoodRoundNum; i++)
            {
                if (_config.AutoWoodConfig.WoodCountOcrEnabled)
                {
                    if (_printer.WoodStatisticsAlwaysEmpty())
                    {
                        Logger.LogInformation("连续{Cnt}次获取木材数量为0。判定附近没有能响应「王树瑞佑」的树木！或者已达每日数量上限", _printer.NothingCount);
                        return SoloTaskResult.Unverified($"连续{_printer.NothingCount}次获取木材数量为0，结束伐木");
                    }

                    if (_printer.ReachedWoodMaxCount)
                    {
                        // OCR 累计的木材数量已确认达到上限，视为完成
                        Logger.LogInformation("{Names}已达到设置的上限：{MaxCnt}", _printer.WoodTotalDict.Keys, _taskParam.WoodDailyMaxCount);
                        return SoloTaskResult.Success($"木材已达到设置的上限：{_taskParam.WoodDailyMaxCount}");
                    }
                }

                Logger.LogInformation("第{Cnt}次伐木", i + 1);
                // 取消时向上抛出，不再静默结束
                _ct.ThrowIfCancellationRequested();

                await Felling(_taskParam, i + 1 == _taskParam.WoodRoundNum);
                _runtime.MaskWindowDrawingBoard.ClearAll();
                _runtime.Sleep(500, _ct);
            }

            // 跑完设定轮数，没有确认实际获得的木材
            return SoloTaskResult.Unverified($"已完成设定的 {_taskParam.WoodRoundNum} 次伐木");
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

    /// <summary>
    /// 伐木统计（OCR 识别获得的木材数量并累计）
    /// </summary>
    /// <param name="runtime">本次运行使用的运行环境</param>
    /// <param name="config">配置根</param>
    private partial class WoodStatisticsPrinter(GameRuntime runtime, AllConfig config)
    {
        /// <summary>
        /// 本次运行使用的运行环境
        /// </summary>
        private readonly GameRuntime _runtime = runtime;

        /// <summary>
        /// 配置根
        /// </summary>
        private readonly AllConfig _config = config;

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
                    _config.AutoWoodConfig.WoodCountOcrEnabled = false;
                    throw new SoloTaskAbortException(SoloTaskResult.Failed("wood_count_not_recognized",
                        "首次伐木就未识别到木材数据，已经自动关闭【OCR识别并累计木材数】的功能，请重新启动【自动伐木】功能！"));
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
            _runtime.Sleep(_firstWoodOcr ? 300 : 100, Ct);
        }

        private string WoodTextAreaOcr()
        {
            // OCR识别文本区域
            using var gameCaptureRegion = _runtime.CaptureToRectArea();
            var assetScale = Math.Min(gameCaptureRegion.Width / 1920d, 1d);
            var woodCountRect = new Rect(
                (int)(100 * assetScale),
                (int)(450 * assetScale),
                (int)(300 * assetScale),
                (int)(250 * assetScale));
            using var woodCountRegion = gameCaptureRegion.DeriveCrop(woodCountRect);
            return OcrFactory.Paddle.Ocr(woodCountRegion.SrcMat);
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

    private async Task Felling(WoodTaskParam taskParam, bool isLast = false)
    {
        // 1. 按 z 触发「王树瑞佑」
        PressZ(taskParam);

        if (isLast)
        {
            return;
        }

        // 打印伐木的统计数据（可选）
        if (_config.AutoWoodConfig.WoodCountOcrEnabled)
        {
            _printer.PrintWoodStatistics(taskParam);
            if (_printer.WoodStatisticsAlwaysEmpty() || _printer.ReachedWoodMaxCount) return;
        }

        if (_config.AutoWoodConfig.UseWonderlandRefresh)
        {
            // 使用进出千星奇域刷新CD
            await _enterAndExitWonderlandJob.Start(_ct);
        }
        else
        {
            // 2. 按下 ESC 打开菜单 并退出游戏
            PressEsc(taskParam);

            // 3. 等待进入游戏
            EnterGame(taskParam);
        }

        // 手动 GC
        GC.Collect();
    }

    private void PressZ(WoodTaskParam taskParam)
    {
        // IMPORTANT: MUST try focus before press Z
        SystemControl.FocusWindow(_runtime.Window.Handle);

        if (_first)
        {
            using var contentRegion = _runtime.CaptureToRectArea();
            using var ra = contentRegion.Find(GetRecognitionObject("TheBoonOfTheElderTree", contentRegion));
            if (ra.IsEmpty())
            {
#if !TEST_WITHOUT_Z_ITEM
                throw new SoloTaskAbortException(SoloTaskResult.Failed("gadget_not_equipped",
                    "请先装备小道具「王树瑞佑」！如果已经装备仍旧出现此提示，请重新仔细阅读文档中的《快速上手》！"));
#else
                System.Threading.Thread.Sleep(2000);
                InputHub.Foreground.SimulateAction(GIActions.QuickUseGadget);
                Debug.WriteLine("[AutoWood] Z");
                _first = false;
#endif
            }
            else
            {
                InputHub.Foreground.SimulateAction(GIActions.QuickUseGadget);
                Debug.WriteLine("[AutoWood] Z");
                _first = false;
            }
        }
        else
        {
            NewRetry.Do(() =>
            {
                _runtime.Sleep(1, _ct);
                using var contentRegion = _runtime.CaptureToRectArea();
                using var ra = contentRegion.Find(GetRecognitionObject("TheBoonOfTheElderTree", contentRegion));
                if (ra.IsEmpty())
                {
#if !TEST_WITHOUT_Z_ITEM
                    throw new RetryException("未找到「王树瑞佑」");
#else
                    System.Threading.Thread.Sleep(15000);
#endif
                }

                InputHub.Foreground.SimulateAction(GIActions.QuickUseGadget);
                Debug.WriteLine("[AutoWood] Z");
                _runtime.Sleep(500, _ct);
            }, TimeSpan.FromSeconds(1), 120);
        }

        _runtime.Sleep(300, _ct);
        _runtime.Sleep(_config.AutoWoodConfig.AfterZSleepDelay, _ct);
    }

    private void PressEsc(WoodTaskParam taskParam)
    {
        SystemControl.FocusWindow(_runtime.Window.Handle);
        InputHub.Foreground.Keyboard.KeyPress(VK.VK_ESCAPE);
        // if (_config.AutoWoodConfig.PressTwoEscEnabled)
        // {
        //     Sleep(1500, _cts);
        //     InputHub.Foreground.Keyboard.KeyPress(VK.VK_ESCAPE);
        // }
        Debug.WriteLine("[AutoWood] Esc");
        _runtime.Sleep(800, _ct);
        // 确认在菜单界面
        try
        {
            NewRetry.Do(() =>
            {
                _runtime.Sleep(1, _ct);
                using var contentRegion = _runtime.CaptureToRectArea();
                using var ra = contentRegion.Find(GetRecognitionObject("MenuBag", contentRegion));
                if (ra.IsEmpty())
                {
                    InputHub.Foreground.Keyboard.KeyPress(VK.VK_ESCAPE);
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
        _runtime.GameRegionClick((size, scale) => (50 * scale, size.Height - 50 * scale));

        Debug.WriteLine("[AutoWood] Click exit button");

        _runtime.Sleep(500, _ct);

        // 点击退出到主界面确认
        using var contentRegion = _runtime.CaptureToRectArea();
        contentRegion.Find(GetRecognitionObject("Confirm", contentRegion), ra =>
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
            _runtime.Sleep(1, _ct);
            _login3rdParty.Login(_ct);
        }

        var clickCnt = 0;
        for (var i = 0; i < 50; i++)
        {
            _runtime.Sleep(1, _ct);

            using var contentRegion = _runtime.CaptureToRectArea();
            using var ra = contentRegion.Find(GetRecognitionObject("EnterGame", contentRegion));
            if (!ra.IsEmpty())
            {
                clickCnt++;
                _runtime.GameRegion1080PPosClick(960, 630);
                Debug.WriteLine("[AutoWood] Click entry");
            }
            else
            {
                if (clickCnt > 2)
                {
                    _runtime.Sleep(5000, _ct);
                    break;
                }
            }

            _runtime.Sleep(1000, _ct);
        }

        if (clickCnt == 0)
        {
            throw new RetryException("未检测进入游戏界面");
        }
    }
}

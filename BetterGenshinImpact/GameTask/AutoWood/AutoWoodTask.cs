using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoWood.Assets;
using BetterGenshinImpact.GameTask.AutoWood.Utils;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Genshin.Settings;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.View.Drawable;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using static Vanara.PInvoke.User32;
using GC = System.GC;
using BetterGenshinImpact.Core.Recognition.OCR;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;

namespace BetterGenshinImpact.GameTask.AutoWood;

/// <summary>
/// 自动伐木
/// </summary>
public class AutoWoodTask
{
    private readonly AutoWoodAssets _assets;

    private bool _first = true;
    
    private bool _shouldContinue = true;
    private int _nothingCount;
    private bool _firstWoodOcr = true;
    private readonly ConcurrentDictionary<string, int> _woodTotalDict;
    private readonly Dictionary<string, int> _enumWoodDict;

    private readonly Login3rdParty _login3rdParty;

    private VK _zKey = VK.VK_Z;

    public AutoWoodTask()
    {
        _login3rdParty = new();
        AutoWoodAssets.DestroyInstance();
        _assets = AutoWoodAssets.Instance;
        _woodTotalDict = new ConcurrentDictionary<string, int>();
        _enumWoodDict = new Dictionary<string, int>();
    }

    public void Start(WoodTaskParam taskParam)
    {
        var hasLock = false;
        try
        {
            hasLock = TaskSemaphore.Wait(0);
            if (!hasLock)
            {
                Logger.LogError("启动自动伐木功能失败：当前存在正在运行中的独立任务，请不要重复执行任务！");
                return;
            }

            TaskTriggerDispatcher.Instance().StopTimer();
            Logger.LogInformation("→ {Text} 设置伐木总次数：{Cnt}", "自动伐木，启动！", taskParam.WoodRoundNum);

            _login3rdParty.RefreshAvailabled();
            if (_login3rdParty.Type == Login3rdParty.The3rdPartyType.Bilibili)
            {
                Logger.LogInformation("自动伐木启用B服模式");
            }

            SettingsContainer settingsContainer = new();

            if (settingsContainer.OverrideController?.KeyboardMap?.ActionElementMap.Where(item => item.ActionId == ActionId.Gadget).FirstOrDefault()?.ElementIdentifierId is ElementIdentifierId key)
            {
                if (key != ElementIdentifierId.Z)
                {
                    _zKey = key.ToVK();
                    Logger.LogInformation($"自动伐木检测到用户改键 {ElementIdentifierId.Z.ToName()} 改为 {key.ToName()}");
                    if (key == ElementIdentifierId.LeftShift || key == ElementIdentifierId.RightShift)
                    {
                        Logger.LogInformation($"用户改键 {key.ToName()} 可能不受模拟支持，若使用正常则忽略");
                    }
                }
            }

            SystemControl.ActivateWindow();
            for (var i = 0; i < taskParam.WoodRoundNum; i++)
            {
                if (_nothingCount >= 3)
                {
                    Logger.LogInformation("连续{Cnt}次获取木材数量为0。已达每日上限或者当前地图中没有树木", _nothingCount);
                    break;
                }
                Logger.LogInformation("第{Cnt}次伐木", i + 1);
                if (taskParam.Cts.IsCancellationRequested || !_shouldContinue)
                {
                    break;
                }

                Felling(taskParam, i + 1 == taskParam.WoodRoundNum);
                VisionContext.Instance().DrawContent.ClearAll();
                Sleep(500, taskParam.Cts);
            }
        }
        catch (NormalEndException e)
        {
            Logger.LogInformation(e.Message);
        }
        catch (Exception e)
        {
            Logger.LogError(e.Message);
            Logger.LogDebug(e.StackTrace);
            System.Windows.MessageBox.Show("自动伐木时异常：" + e.Source + "\r\n--" + Environment.NewLine + e.StackTrace + "\r\n---" + Environment.NewLine + e.Message);
        }
        finally
        {
            VisionContext.Instance().DrawContent.ClearAll();
            TaskSettingsPageViewModel.SetSwitchAutoWoodButtonText(false);
            Logger.LogInformation("← {Text}", "退出自动伐木");
            TaskTriggerDispatcher.Instance().StartTimer();

            if (hasLock)
            {
                TaskSemaphore.Release();
            }
        }
    }
    
    private void RecognizeWoodCount(WoodTaskParam taskParam)
    {
        var firstTextFound = false;
        var recognizedText = "";
        var firstOcrResultList = new List<string>();
        
        // 创建一个计时器，循环识别文本，直到超时
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < 3000) // 3秒超时
        {
            // OCR识别木材文本
            recognizedText = PerformOcr(taskParam, firstTextFound, firstOcrResultList);
            if (firstTextFound)
            {
                _nothingCount = 0;
                break;
            }

            firstTextFound = StartRecognizedText(recognizedText);
        }
        stopwatch.Stop(); // 停止计时

        ProcessRecognizedText(taskParam, recognizedText, firstOcrResultList);
    }
    
    private string PerformOcr(WoodTaskParam taskParam, bool firstTextFound, List<string> firstOcrResultList)
    {
        if (_firstWoodOcr)
        {
            var firstWoodCountRect = CaptureToRectArea().DeriveCrop(_assets.WoodCountUpperRect);
            var recognizedText = OcrFactory.Paddle.Ocr(firstWoodCountRect.SrcGreyMat);
            firstOcrResultList.Add(recognizedText);
            Sleep(500, taskParam.Cts);
            return recognizedText;
        }

        if (firstTextFound)
        {
            // 多个木材时休眠下，然后重新OCR识别所有的文本。因为多个时木材文本是依次出现的，有延迟
            Sleep(200 * _woodTotalDict.Keys.Count, taskParam.Cts);
        }

        var woodCountRect = CaptureToRectArea().DeriveCrop(_assets.WoodCountUpperRect);
        return OcrFactory.Paddle.Ocr(woodCountRect.SrcGreyMat);
    }
    
    private static bool StartRecognizedText(string recognizedText)
    {
        return !string.IsNullOrEmpty(recognizedText) && 
               recognizedText.Contains("获得") &&
               (recognizedText.Contains('×') || recognizedText.Contains('x'));
    }
    
    private void ProcessRecognizedText(WoodTaskParam taskParam, string recognizedText, List<string> firstOcrResultList) 
    {
        if (_firstWoodOcr)
        {
            // 首次识别时，找到最长的OCR结果
            recognizedText = FindLongestOcrResult(firstOcrResultList, recognizedText);
        }

        if (!string.IsNullOrEmpty(recognizedText))
        {
            ParseWoodCount(taskParam, recognizedText);
        }
        else
        {
            _nothingCount++;
            Logger.LogWarning("未能识别到伐木数量");
            return;
        }

        CheckWoodQuantitiesAndContinue(taskParam);
    }
    
    private void ParseWoodCount(WoodTaskParam taskParam, string text)
    {
        // 从识别的文本中提取木材名称和数量
        // 格式示例："获得\n竹节×30\n杉木×20"
        int index = text.IndexOf('×');
        if (index == -1)
        {
            index = text.IndexOf('X');
        }
        
        if (index != -1)
        {
            // 匹配模式 "名称×数量"，其中名称可能包含中文或字母，数量为数字
            var matches = Regex.Matches(text, @"([^\d\n]+)[×x](\d+)");
            
            // 如果OCR识别木材的种类小于等于最初保存的一样时，直接使用最初的木材数量。
            if (!_firstWoodOcr && 1 <= matches.Count && matches.Count <= _enumWoodDict.Count)
            {
                foreach (var entry in _enumWoodDict.Where(entry => entry.Value <= taskParam.WoodDailyMaxCount))
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
        else
        {
            Logger.LogWarning("未能正确解析木材信息格式：{woodText}", text);
        }
    }
    
    private void UpdateWoodCount(string materialName, int quantity)
    {
        // 检查字典中是否已包含这种木材名称
        if (!_firstWoodOcr && !_woodTotalDict.ContainsKey(materialName))
        {
            Logger.LogWarning("未知的木材名：{woodName}，数量{Cnt}", materialName, quantity);
        }
        _woodTotalDict.AddOrUpdate(
            key: materialName,
            addValue: quantity,
            updateValueFactory: (_, existingValue) => existingValue + quantity
        );
        if (_firstWoodOcr)
        {
            // 保存木材单次获取的值
            _enumWoodDict.Add(materialName, quantity);
        }
    }

    private static string FindLongestOcrResult(List<string> firstOcrResultList, string recognizedText)
    {
        foreach (var str in firstOcrResultList.Where(str => str.Length > recognizedText.Length))
        {
            recognizedText = str;
        }

        return recognizedText;
    }
    
    private void CheckWoodQuantitiesAndContinue(WoodTaskParam taskParam)
    {
        var allMax = true;
        foreach (var entry in _woodTotalDict)
        {
            // 打印每个条目的键（木材名称）和值（数量）
            Logger.LogInformation("木材{woodName}累积获取数量：{Cnt}", entry.Key, entry.Value);
            // 检查木材是否超过每日上限
            if (entry.Value < taskParam.WoodDailyMaxCount) allMax = false;
            else Logger.LogInformation("木材{Name}已达到每日数量上限：{Count}", entry.Key, entry.Value);
        }

        _shouldContinue = !_woodTotalDict.IsEmpty || !allMax;
    }

    private void Felling(WoodTaskParam taskParam, bool isLast = false)
    {
        // 1. 按 z 触发「王树瑞佑」
        PressZ(taskParam);

        if (isLast)
        {
            return;
        }
        
        // 计算木材数量
        RecognizeWoodCount(taskParam);

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
        SystemControl.Focus(TaskContext.Instance().GameHandle);

        if (_first)
        {
            using var contentRegion = CaptureToRectArea();
            using var ra = contentRegion.Find(_assets.TheBoonOfTheElderTreeRo);
            if (ra.IsEmpty())
            {
#if !TEST_WITHOUT_Z_ITEM
                throw new NormalEndException("请先装备小道具「王树瑞佑」！");
#else
                Thread.Sleep(2000);
                Simulation.SendInputEx.Keyboard.KeyPress(_zKey);
                Debug.WriteLine("[AutoWood] Z");
                _first = false;
#endif
            }
            else
            {
                Simulation.SendInput.Keyboard.KeyPress(_zKey);
                Debug.WriteLine("[AutoWood] Z");
                _first = false;
            }
        }
        else
        {
            NewRetry.Do(() =>
            {
                Sleep(1, taskParam.Cts);
                using var contentRegion = CaptureToRectArea();
                using var ra = contentRegion.Find(_assets.TheBoonOfTheElderTreeRo);
                if (ra.IsEmpty())
                {
#if !TEST_WITHOUT_Z_ITEM
                    throw new RetryException("未找到「王树瑞佑」");
#else
                    Thread.Sleep(15000);
#endif
                }

                Simulation.SendInput.Keyboard.KeyPress(_zKey);
                Debug.WriteLine("[AutoWood] Z");
                Sleep(500, taskParam.Cts);
            }, TimeSpan.FromSeconds(1), 120);
        }

        Sleep(300, taskParam.Cts);
        Sleep(TaskContext.Instance().Config.AutoWoodConfig.AfterZSleepDelay, taskParam.Cts);
    }

    private void PressEsc(WoodTaskParam taskParam)
    {
        SystemControl.Focus(TaskContext.Instance().GameHandle);
        Simulation.SendInput.Keyboard.KeyPress(VK.VK_ESCAPE);
        Debug.WriteLine("[AutoWood] Esc");
        Sleep(800, taskParam.Cts);
        // 确认在菜单界面
        try
        {
            NewRetry.Do(() =>
            {
                Sleep(1, taskParam.Cts);
                using var contentRegion = CaptureToRectArea();
                using var ra = contentRegion.Find(_assets.MenuBagRo);
                if (ra.IsEmpty())
                {
                    throw new RetryException("未检测到弹出菜单");
                }
            }, TimeSpan.FromSeconds(1), 3);
        }
        catch (Exception e)
        {
            Logger.LogInformation(e.Message);
            Logger.LogInformation("仍旧点击退出按钮");
        }

        // 点击退出
        GameCaptureRegion.GameRegionClick((size, scale) => (50 * scale, size.Height - 50 * scale));

        Debug.WriteLine("[AutoWood] Click exit button");

        Sleep(500, taskParam.Cts);

        // 点击确认
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
            Sleep(1, taskParam.Cts);
            _login3rdParty.Login(taskParam.Cts);
        }

        var clickCnt = 0;
        for (var i = 0; i < 50; i++)
        {
            Sleep(1, taskParam.Cts);

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
                    Sleep(5000, taskParam.Cts);
                    break;
                }
            }

            Sleep(1000, taskParam.Cts);
        }

        if (clickCnt == 0)
        {
            throw new RetryException("未检测进入游戏界面");
        }
    }
}

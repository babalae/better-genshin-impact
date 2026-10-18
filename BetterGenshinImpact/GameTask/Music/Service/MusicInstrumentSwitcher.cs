using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Model.GameUI;
using BetterGenshinImpact.GameTask.Runtime;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Music.Service;

public sealed class MusicInstrumentSwitcher : IMusicInstrumentSwitcher
{
    private readonly ILogger<MusicInstrumentSwitcher> _logger = App.GetLogger<MusicInstrumentSwitcher>();

    /// <summary>
    /// 游戏运行环境服务。进程级单例不持有运行环境，每次切换乐器时读取 Current
    /// </summary>
    private readonly GameRuntimeService _runtimeService;

    /// <summary>
    /// 由容器创建
    /// </summary>
    /// <param name="runtimeService">游戏运行环境服务</param>
    public MusicInstrumentSwitcher(GameRuntimeService runtimeService)
    {
        _runtimeService = runtimeService;
    }

    public async Task<bool> SwitchToAsync(string instrumentName, CancellationToken cancellationToken)
    {
        instrumentName = NormalizeInstrumentName(instrumentName);
        if (string.IsNullOrWhiteSpace(instrumentName))
        {
            return false;
        }

        // 本次切换只使用开始时的运行环境，中途换绑时后续截图与等待按正常中断处理
        if (_runtimeService.Current is not { } runtime)
        {
            _logger.LogWarning("截图器未启动，无法自动切换乐器：{InstrumentName}", instrumentName);
            return false;
        }

        var keepInstrumentUiOpen = false;
        var uiInteractionStarted = false;
        try
        {
            using var instrumentTemplate = GameTaskManager.LoadAssetImage(
                "Music",
                Path.Combine("Instruments", $"{instrumentName}.png"),
                runtime.SystemInfo);
            uiInteractionStarted = true;
            await new ReturnMainUiTask(runtime).Start(cancellationToken);
            await AutoArtifactSalvageTask.OpenInventory(
                runtime,
                GridScreenName.Gadget,
                InputHub.Foreground,
                _logger,
                cancellationToken);

            var recognitionObject = new RecognitionObject
            {
                Name = instrumentName,
                RecognitionType = RecognitionTypes.TemplateMatch,
                TemplateImageMat = instrumentTemplate,
                Threshold = 0.75,
                Use3Channels = true
            };

            if (!await SelectInstrumentAsync(runtime, recognitionObject, cancellationToken))
            {
                _logger.LogError("未在背包中找到乐器：{InstrumentName}", instrumentName);
                return false;
            }

            var buttonText = await WaitForEquipButtonTextAsync(runtime, cancellationToken);
            if (buttonText.Contains("替换", StringComparison.Ordinal))
            {
                ClickEquipButton(runtime);
                await runtime.Delay(500, cancellationToken);
            }
            else if (!buttonText.Contains("卸下", StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "无法确认乐器 {InstrumentName} 的装备按钮，识别结果：{ButtonText}",
                    instrumentName,
                    buttonText);
                return false;
            }

            await new ReturnMainUiTask(runtime).Start(cancellationToken);
            _logger.LogInformation("乐器已就绪：{InstrumentName}，即将开始演奏", instrumentName);
            await runtime.Delay(1000, cancellationToken);
            InputHub.Foreground.SimulateAction(GIActions.QuickUseGadget);
            await runtime.Delay(2000, cancellationToken);
            keepInstrumentUiOpen = true;
            return true;
        }
        catch (FileNotFoundException)
        {
            _logger.LogWarning("自动换乐器暂不支持该乐器档案：{InstrumentName}", instrumentName);
            return false;
        }
        finally
        {
            if (uiInteractionStarted
                && !keepInstrumentUiOpen
                && !cancellationToken.IsCancellationRequested)
            {
                await new ReturnMainUiTask(runtime).Start(cancellationToken);
            }
        }
    }

    /// <summary>
    /// 在背包小道具页逐格查找并点击目标乐器
    /// </summary>
    /// <param name="runtime">本次切换使用的运行环境</param>
    /// <param name="recognitionObject">乐器图标的识别对象</param>
    /// <param name="cancellationToken">取消令牌</param>
    private async Task<bool> SelectInstrumentAsync(
        GameRuntime runtime,
        RecognitionObject recognitionObject,
        CancellationToken cancellationToken)
    {
        var gridScreen = new GridScreen(
            runtime,
            GridParams.Templates[GridScreenName.Gadget],
            _logger,
            cancellationToken);
        await foreach (var (page, rect) in gridScreen.WithCancellation(cancellationToken))
        {
            using var itemRegion = page.DeriveCrop(rect);
            using var result = itemRegion.Find(recognitionObject);
            if (!result.IsExist())
            {
                continue;
            }

            itemRegion.Click();
            await runtime.Delay(500, cancellationToken);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 等待装备按钮出现"替换"或"卸下"文字，返回最后一次识别结果
    /// </summary>
    /// <param name="runtime">本次切换使用的运行环境</param>
    /// <param name="cancellationToken">取消令牌</param>
    private static async Task<string> WaitForEquipButtonTextAsync(GameRuntime runtime, CancellationToken cancellationToken)
    {
        var result = string.Empty;
        for (var i = 0; i < 6; i++)
        {
            using var capture = runtime.CaptureToRectArea();
            using var buttonRegion = capture.DeriveCrop(GetEquipButtonRect(runtime));
            result = OcrFactory.Paddle.Ocr(buttonRegion.SrcMat);
            if (result.Contains("替换", StringComparison.Ordinal)
                || result.Contains("卸下", StringComparison.Ordinal))
            {
                return result;
            }

            await runtime.Delay(300, cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// 点击装备按钮
    /// </summary>
    /// <param name="runtime">本次切换使用的运行环境</param>
    private static void ClickEquipButton(GameRuntime runtime)
    {
        using var capture = runtime.CaptureToRectArea();
        using var buttonRegion = capture.DeriveCrop(GetEquipButtonRect(runtime));
        buttonRegion.Click();
    }

    /// <summary>
    /// 装备按钮在截图区域中的位置
    /// </summary>
    /// <param name="runtime">本次切换使用的运行环境</param>
    private static Rect GetEquipButtonRect(GameRuntime runtime)
    {
        var scale = runtime.SystemInfo.AssetScale;
        return new Rect(1600, 965, 260, 90).Multiply(scale);
    }

    private static string NormalizeInstrumentName(string instrumentName)
    {
        return instrumentName
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;
    }
}

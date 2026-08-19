using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Model.GameUI;
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

    public async Task<bool> SwitchToAsync(string instrumentName, CancellationToken cancellationToken)
    {
        instrumentName = NormalizeInstrumentName(instrumentName);
        if (string.IsNullOrWhiteSpace(instrumentName))
        {
            return false;
        }

        var keepInstrumentUiOpen = false;
        var uiInteractionStarted = false;
        try
        {
            using var instrumentTemplate = GameTaskManager.LoadAssetImage(
                "Music",
                Path.Combine("Instruments", $"{instrumentName}.png"));
            uiInteractionStarted = true;
            await new ReturnMainUiTask().Start(cancellationToken);
            await AutoArtifactSalvageTask.OpenInventory(
                GridScreenName.Gadget,
                Simulation.SendInput,
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

            if (!await SelectInstrumentAsync(recognitionObject, cancellationToken))
            {
                _logger.LogError("未在背包中找到乐器：{InstrumentName}", instrumentName);
                return false;
            }

            var buttonText = await WaitForEquipButtonTextAsync(cancellationToken);
            if (buttonText.Contains("替换", StringComparison.Ordinal))
            {
                ClickEquipButton();
                await Delay(500, cancellationToken);
            }
            else if (!buttonText.Contains("卸下", StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "无法确认乐器 {InstrumentName} 的装备按钮，识别结果：{ButtonText}",
                    instrumentName,
                    buttonText);
                return false;
            }

            await new ReturnMainUiTask().Start(cancellationToken);
            _logger.LogInformation("乐器已就绪：{InstrumentName}，即将开始演奏", instrumentName);
            await Delay(1000, cancellationToken);
            Simulation.SendInput.SimulateAction(GIActions.QuickUseGadget);
            await Delay(2000, cancellationToken);
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
                await new ReturnMainUiTask().Start(cancellationToken);
            }
        }
    }

    private async Task<bool> SelectInstrumentAsync(
        RecognitionObject recognitionObject,
        CancellationToken cancellationToken)
    {
        var gridScreen = new GridScreen(
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
            await Delay(500, cancellationToken);
            return true;
        }

        return false;
    }

    private static async Task<string> WaitForEquipButtonTextAsync(CancellationToken cancellationToken)
    {
        var result = string.Empty;
        for (var i = 0; i < 6; i++)
        {
            using var capture = CaptureToRectArea(forceNew: true);
            using var buttonRegion = capture.DeriveCrop(GetEquipButtonRect());
            result = OcrFactory.Paddle.Ocr(buttonRegion.SrcMat);
            if (result.Contains("替换", StringComparison.Ordinal)
                || result.Contains("卸下", StringComparison.Ordinal))
            {
                return result;
            }

            await Delay(300, cancellationToken);
        }

        return result;
    }

    private static void ClickEquipButton()
    {
        using var capture = CaptureToRectArea(forceNew: true);
        using var buttonRegion = capture.DeriveCrop(GetEquipButtonRect());
        buttonRegion.Click();
    }

    private static Rect GetEquipButtonRect()
    {
        var scale = TaskContext.Instance().SystemInfo.AssetScale;
        return new Rect(1600, 965, 260, 90).Multiply(scale);
    }

    private static string NormalizeInstrumentName(string instrumentName)
    {
        return instrumentName
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;
    }
}

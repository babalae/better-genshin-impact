using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Model.GameUI;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;

/// <summary>
/// 从背包左下角读取摩拉数量。
/// </summary>
public sealed class MoraFromInventoryScanner
{
    private const double MatchThreshold = 0.8;

    /// <summary>本次任务的运行环境。</summary>
    private readonly GameRuntime _runtime;

    /// <summary>日志。</summary>
    private readonly ILogger _logger;

    /// <summary>
    /// 创建摩拉背包扫描器。
    /// </summary>
    /// <param name="runtime">本次任务的运行环境。</param>
    /// <param name="logger">日志。</param>
    public MoraFromInventoryScanner(GameRuntime runtime, ILogger logger)
    {
        _runtime = runtime;
        _logger = logger;
    }

    /// <summary>
    /// 摩拉图标模板路径。
    /// </summary>
    private static string TemplatePath => Global.Absolute(@"GameTask\AutoLeyLineOutcrop\Assets\1920x1080\mora_icon.png");

    /// <summary>
    /// 打开背包、模板匹配左下角摩拉图标，并按图标右侧偏移的固定区域 OCR。
    /// </summary>
    /// <returns>摩拉数量；扫描失败返回 null。</returns>
    public async Task<int?> ScanAsync(IInputChannel input, CancellationToken ct)
    {
        try
        {
            await AutoArtifactSalvageTask.OpenInventory(_runtime, GridScreenName.CharacterDevelopmentItems, input, _logger, ct);
            await Task.Delay(300, ct);

            using var capture = _runtime.CaptureToRectArea();
            using var template = Mat.FromStream(File.OpenRead(TemplatePath), ImreadModes.Color);

            var scale = capture.Width / 1920d;
            Mat? scaledTemplate = null;
            try
            {
                if (Math.Abs(scale - 1) > 0.01)
                {
                    scaledTemplate = ResizeHelper.Resize(template, scale);
                }

                var matchTemplate = scaledTemplate ?? template;
                var searchRect = new Rect(
                    0,
                    (int)Math.Round(900 * scale),
                    (int)Math.Round(600 * scale),
                    (int)Math.Round(180 * scale));
                searchRect = searchRect.Intersect(new Rect(0, 0, capture.Width, capture.Height));

                using var searchMat = new Mat(capture.SrcMat, searchRect);
                using var result = new Mat();
                Cv2.MatchTemplate(searchMat, matchTemplate, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out var maxValue, out _, out var maxLocation);
                if (maxValue < MatchThreshold)
                {
                    _logger.LogWarning("摩拉背包扫描：未匹配到摩拉图标，最高匹配度 {Score:F3}", maxValue);
                    return null;
                }

                var iconX = searchRect.X + maxLocation.X;
                var iconY = searchRect.Y + maxLocation.Y;
                var iconWidth = matchTemplate.Width;

                // OCR 区域按图标偏移：右侧 25px 起、上移 10px，宽 220、高 40（1920 基准）。
                var ocrRect = new Rect(
                    iconX + iconWidth + 25,
                    iconY - (int)Math.Round(10 * scale),
                    (int)Math.Round(220 * scale),
                    (int)Math.Round(40 * scale));
                ocrRect = ocrRect.Intersect(new Rect(0, 0, capture.Width, capture.Height));
                using var countRegion = capture.DeriveCrop(ocrRect);
                var text = OcrFactory.Paddle.Ocr(countRegion.SrcMat);
                var digits = new string((text ?? string.Empty).Where(char.IsDigit).ToArray());
                if (string.IsNullOrEmpty(digits) || !int.TryParse(digits, out var mora))
                {
                    _logger.LogWarning("摩拉背包扫描：OCR 结果无法解析：{Text}", text ?? "<空>");
                    return null;
                }

                _logger.LogInformation("摩拉背包扫描：识别到 {Mora}", mora);
                return mora;
            }
            finally
            {
                scaledTemplate?.Dispose();
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogWarning(e, "摩拉背包扫描失败");
            return null;
        }
        finally
        {
            try
            {
                await new ReturnMainUiTask(_runtime).Start(ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _logger.LogWarning(e, "摩拉背包扫描结束后返回主界面失败");
            }
        }
    }
}

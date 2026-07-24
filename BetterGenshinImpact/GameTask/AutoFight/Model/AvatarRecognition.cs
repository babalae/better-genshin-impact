using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.View.Drawable;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using AutoFightParam = BetterGenshinImpact.GameTask.AutoFight.AutoFightParam;

namespace BetterGenshinImpact.GameTask.AutoFight.Model;

/// <summary>
/// 战斗识别相关的通用工具函数
/// </summary>
public static class AvatarRecognition
{
    /// <summary>
    /// 当前战斗的 AutoFightParam（由 AutoFightTask/AutoFightJsonTask 在 Start 开头设置），
    /// 用于让 <see cref="GetVisualRecognitionConfig"/> 优先读取逐队伍配置而非全局配置。
    /// AsyncLocal 会沿 async 调用链自动传递，包括 <see cref="Task.Run"/> 创建的后台任务。
    /// </summary>
    private static readonly AsyncLocal<AutoFightParam?> _currentAutoFightParam = new();

    /// <summary>
    /// 设置当前战斗参数，后续的视觉配置读取将优先使用此参数中的值而非全局配置。
    /// 应在 Start 开头调用，并在 Start 的 finally 中调用 <see cref="ClearCurrentAutoFightParam"/> 清理。
    /// </summary>
    public static void SetCurrentAutoFightParam(AutoFightParam? param) => _currentAutoFightParam.Value = param;

    /// <summary>
    /// 清除当前战斗参数，后续视觉配置回退到全局配置。
    /// </summary>
    public static void ClearCurrentAutoFightParam() => _currentAutoFightParam.Value = null;

    /// <summary>
    /// 持续索敌跳过标记：当某角色进行独占视角操作（如重击索敌）时设为 true，
    /// 持续索敌循环将跳过本帧，避免两者争夺鼠标控制权。
    /// </summary>
    private static volatile bool _skipSeek;

    /// <summary>
    /// 开始独占视角操作。
    /// 返回的 <see cref="SkipSeekScope"/> 在 Dispose 时自动重置跳过标记。
    /// 使用方应通过 using 语句确保异常安全。
    /// </summary>
    internal static SkipSeekScope BeginExclusiveOperation()
    {
        _skipSeek = true;
        return new SkipSeekScope();
    }

    /// <summary>
    /// 独占操作作用域。Dispose 时自动重置 SkipSeek，避免遗漏恢复。
    /// </summary>
    internal readonly struct SkipSeekScope : IDisposable
    {
        public void Dispose() => _skipSeek = false;
    }

    /// <summary>
    /// 资源缩放比例
    /// </summary>
    private static double AssetScale => TaskContext.Instance().SystemInfo.AssetScale;

    /// <summary>
    /// 传奇血条动态追踪字典：2px粒度的 y → 连续出现计数。
    /// 按 y 分层使用不同阈值判定传奇：
    ///   y &lt; 100 → 连续 2 帧判传奇
    ///   y 100-200 → 连续 4 帧判传奇
    ///   y ≥ 200 → 连续 10 帧判传奇
    /// </summary>
    private static readonly Dictionary<int, int> _legendaryBarTracker = new();
    private static readonly object _legendaryBarLock = new();
    private const int LegendaryBarMaxCount = 10;

    /// <summary>
    /// 更新传奇血条动态追踪状态。
    /// 对全部 y 的血条进行帧间连续性追踪，连续出现达到对应阈值后标记为传奇。
    /// 允许1帧容错：某帧未出现时计数递减而非直接清零。
    /// </summary>
    private static void UpdateLegendaryBarTracker(IEnumerable<int> barYs)
    {
        lock (_legendaryBarLock)
        {
            var currentBins = barYs.Select(y => y / 2 * 2)
                                   .ToHashSet();

            // 存在的 y：递增（上限为最大阈值）
            foreach (var bin in currentBins)
            {
                if (_legendaryBarTracker.TryGetValue(bin, out var cnt))
                    _legendaryBarTracker[bin] = Math.Min(cnt + 1, LegendaryBarMaxCount);
                else
                    _legendaryBarTracker[bin] = 1;
            }

            // 不存在的 y：递减（1帧容错），归零则移除
            foreach (var bin in _legendaryBarTracker.Keys.ToArray())
            {
                if (!currentBins.Contains(bin))
                {
                    _legendaryBarTracker[bin]--;
                    if (_legendaryBarTracker[bin] <= 0)
                        _legendaryBarTracker.Remove(bin);
                }
            }
        }
    }

    /// <summary>
    /// 判断指定 y 坐标的血条是否为传奇血条。
    /// y &lt; 100 连续 2 帧判传奇；y 100-200 连续 4 帧判传奇；y ≥ 200 连续 10 帧判传奇。
    /// </summary>
    public static bool IsLegendaryBar(int y)
    {
        lock (_legendaryBarLock)
        {
            if (!_legendaryBarTracker.TryGetValue(y / 2 * 2, out var cnt))
                return false;

            int threshold = y < (int)(100 * AssetScale) ? 2
                          : y < (int)(200 * AssetScale) ? 4
                          : 10;
            return cnt >= threshold;
        }
    }

    /// <summary>
    /// 检测屏幕中的红色血条（连通域分析）
    /// </summary>
    public static List<(int x, int y, int width, int height)> FindBloodBars(ImageRegion? existingCapture = null)
    {
        var results = new List<(int x, int y, int width, int height)>();

        var selfCapture = existingCapture == null ? CaptureToRectArea() : null;
        using (selfCapture)
        {
            var image = existingCapture ?? selfCapture!;
            var bloodLower = new Scalar(255, 90, 90); // BGR 红色

            using var cropped = image.DeriveCrop(0, 0, (int)(1500 * AssetScale), (int)(900 * AssetScale));
            using Mat mask = OpenCvCommonHelper.Threshold(
                cropped.SrcMat, bloodLower);

            using Mat labels = new Mat();
            using Mat stats = new Mat();
            using Mat centroids = new Mat();

            int numLabels = Cv2.ConnectedComponentsWithStats(
                mask, labels, stats, centroids,
                connectivity: PixelConnectivity.Connectivity4, ltype: MatType.CV_32S);

            for (int i = 1; i < numLabels; i++)
            {
                using Mat row = stats.Row(i);
                if (row.GetArray(out int[] arr))
                {
                    int x = arr[0], y = arr[1], width = arr[2], height = arr[3];
                    if (y < (int)(50 * AssetScale))
                        continue;
                    results.Add((x, y, width, height));
                }
            }

            // 自动更新传奇血条动态追踪
            UpdateLegendaryBarTracker(results.Select(r => r.y));

            return results;
        }
    }

    /// <summary>
    /// 获取视觉识别相关配置项。
    /// 调用方通过此方法获取配置，而非直接读取全局 config，确保配置访问集中管理。
    /// </summary>
    public static (int TargetingDetectionInterval, bool DrawRecognitionResults, double LockLostWaitTime, DamageNumberRecognitionMode DamageNumberRecognitionMode) GetVisualRecognitionConfig()
    {
        var param = _currentAutoFightParam.Value;
        if (param != null)
        {
            return (param.TargetingDetectionInterval, param.DrawRecognitionResults, param.LockLostWaitTime, param.DamageNumberRecognitionMode);
        }

        var config = TaskContext.Instance().Config.AutoFightConfig;
        return (config.TargetingDetectionInterval, config.DrawRecognitionResults, config.LockLostWaitTime, config.DamageNumberRecognitionMode);
    }

    /// <summary>
    /// 根据配置的伤害数字识别模式寻找伤害数字/反应文字。
    ///   - Disabled：直接返回 null
    ///   - Ocr：使用 OCR 识别
    ///   - Color：使用颜色分析识别
    /// 配置来源：<see cref="GetVisualRecognitionConfig"/>
    /// </summary>
    public static (int centerX, int centerY, string text, int x, int y, int width, int height)? FindDamageNumber(ImageRegion? existingCapture = null)
    {
        var mode = GetVisualRecognitionConfig().DamageNumberRecognitionMode;
        switch (mode)
        {
            case DamageNumberRecognitionMode.Disabled:
                return null;
            case DamageNumberRecognitionMode.Color:
                return FindDamageNumberByColor(existingCapture);
            case DamageNumberRecognitionMode.Ocr:
            default:
                return FindDamageNumberByOcr(existingCapture);
        }
    }

    /// <summary>
    /// OCR 寻找伤害数字/反应文字作为追踪目标（备用寻敌）。
    /// 在 450,240-1600,900 区域 OCR，过滤条件：
    ///   - 有效项1：排除首位 '+'，去除非数字后纯数字 ≥4 位
    ///   - 有效项2：文本包含反应关键词（免疫/蒸发/感电/结晶/扩散/绽放/冻结/超载/融化/燃烧/超导/激化），跳过数字过滤
    /// 按 h²×文本字数 加权得到中心坐标，返回离加权中心最近的有效项。
    /// </summary>
    private static (int centerX, int centerY, string text, int x, int y, int width, int height)? FindDamageNumberByOcr(ImageRegion? existingCapture = null)
    {
        var selfCapture = existingCapture == null ? CaptureToRectArea() : null;
        using (selfCapture)
        {
            var ra = existingCapture ?? selfCapture!;
            var ocrResults = ra.FindMulti(RecognitionObject.Ocr((int)(450 * AssetScale), (int)(240 * AssetScale), (int)(1150 * AssetScale), (int)(660 * AssetScale)));

            string[] reactionKeywords = ["免疫", "蒸发", "感电", "结晶", "扩散", "绽放", "冻结", "超载", "融化", "燃烧", "超导", "激化"];
            var validItems = new List<(int cx, int cy, int area, string text, int x, int y, int w, int h)>();

            foreach (var r in ocrResults)
            {
                var text = r.Text?.Trim();
                if (string.IsNullOrEmpty(text)) continue;

                // 有效项2：反应关键词（跳过所有过滤）
                if (reactionKeywords.Any(k => text.Contains(k)))
                {
                    validItems.Add((r.X + r.Width / 2, r.Y + r.Height / 2, r.Height * r.Height * text.Length, text, r.X, r.Y, r.Width, r.Height));
                    continue;
                }

                // 有效项1：排除 '+' 开头
                if (text[0] == '+') continue;

                // 去除非数字，纯数字 ≥4 位
                var digits = new string(text.Where(char.IsDigit).ToArray());
                if (digits.Length >= 4)
                {
                    validItems.Add((r.X + r.Width / 2, r.Y + r.Height / 2, r.Height * r.Height * text.Length, text, r.X, r.Y, r.Width, r.Height));
                }
            }

            if (validItems.Count == 0) return null;

            int totalArea = validItems.Sum(i => i.area);
            if (totalArea == 0) return null;

            double avgX = (double)validItems.Sum(i => i.cx * i.area) / totalArea;
            double avgY = (double)validItems.Sum(i => i.cy * i.area) / totalArea;

            var closest = validItems.OrderBy(i => Math.Abs(i.cx - avgX) + Math.Abs(i.cy - avgY)).First();

            return (closest.cx, closest.cy, closest.text, closest.x, closest.y, closest.w, closest.h);
        }
    }

    /// <summary>
    /// 颜色分析模式：在 450,240-1600,900 区域内查找固定颜色的像素，
    /// 经连通域分析后舍弃高度小于20的区域，返回加权中心。
    /// </summary>
    private static (int centerX, int centerY, string text, int x, int y, int width, int height)? FindDamageNumberByColor(ImageRegion? existingCapture = null)
    {
        var selfCapture = existingCapture == null ? CaptureToRectArea() : null;
        using (selfCapture)
        {
            var ra = existingCapture ?? selfCapture!;

            // 目标颜色 (RGB)
            Scalar[] targetColors =
            [
                new(225, 155, 255), // 雷 #E19BFF
                new(153, 255, 255), // 冰 #99FFFF
                new(51, 204, 255),  // 水 #33CCFF
                new(102, 255, 204), // 风 #66FFCC
                new(255, 155, 0),   // 火 #FF9B00
                new(0, 234, 82),    // 草 #00EA52
                new(255, 204, 102), // 岩 #FFCC66
            ];

            int roiX = (int)(450 * AssetScale);
            int roiY = (int)(240 * AssetScale);
            int roiW = (int)(1150 * AssetScale);
            int roiH = (int)(660 * AssetScale);

            using var cropped = ra.DeriveCrop(roiX, roiY, roiW, roiH);
            using var rgbMat = new Mat();
            Cv2.CvtColor(cropped.SrcMat, rgbMat, ColorConversionCodes.BGR2RGB);

            using var combinedMask = new Mat(cropped.SrcMat.Size(), MatType.CV_8UC1, Scalar.All(0));

            foreach (var color in targetColors)
            {
                using var mask = new Mat();
                Cv2.InRange(rgbMat, color, color, mask);
                Cv2.BitwiseOr(combinedMask, mask, combinedMask);
            }

            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();
            var numLabels = Cv2.ConnectedComponentsWithStats(combinedMask, labels, stats, centroids,
                connectivity: PixelConnectivity.Connectivity4, ltype: MatType.CV_32S);

            if (numLabels <= 1) return null;

            var validItems = new List<(int cx, int cy, int area, int x, int y, int w, int h)>();
            for (int i = 1; i < numLabels; i++)
            {
                int x = stats.At<int>(i, (int)ConnectedComponentsTypes.Left);
                int y = stats.At<int>(i, (int)ConnectedComponentsTypes.Top);
                int width = stats.At<int>(i, (int)ConnectedComponentsTypes.Width);
                int height = stats.At<int>(i, (int)ConnectedComponentsTypes.Height);

                if (height < (int)(20 * AssetScale)) continue;

                int area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);
                validItems.Add((x + width / 2 + roiX, y + height / 2 + roiY, area, x + roiX, y + roiY, width, height));
            }

            if (validItems.Count == 0) return null;

            int totalArea = validItems.Sum(i => i.area);
            if (totalArea == 0) return null;

            double avgX = (double)validItems.Sum(i => i.cx * i.area) / totalArea;
            double avgY = (double)validItems.Sum(i => i.cy * i.area) / totalArea;

            var closest = validItems.OrderBy(i => Math.Abs(i.cx - avgX) + Math.Abs(i.cy - avgY)).First();

            return (closest.cx, closest.cy, "", closest.x, closest.y, closest.w, closest.h);
        }
    }

    /// <summary>
    /// 战斗中持续索敌循环：在战斗过程中持续尝试面朝敌人。
    /// 受 EnableCombatTargeting 配置项控制总开关。
    /// 当 SkipSeek 为 true 时（如部分角色重击索敌期间）跳过本帧。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <param name="isFightEnd">战斗是否已结束（外部标志，为 true 时退出循环）</param>
    public static async Task ContinuousTargetingLoopAsync(
        CancellationToken ct,
        Func<bool>? isFightEnd = null)
    {
        var dpi = TaskContext.Instance().DpiScale;
        var (frameIntervalMs, drawResults, lockLostWaitTime, _) = GetVisualRecognitionConfig();
        DateTime? lastSeenTargetTime = null;  // 最后找到目标的时间（null = 从未找到）

        try
        {
            while (!ct.IsCancellationRequested && !(isFightEnd?.Invoke() ?? false))
            {
                // SkipSeek 为 true 时跳过本轮索敌，避免视角控制冲突
                if (_skipSeek)
                {
                    await Task.Delay(frameIntervalMs, ct);
                    continue;
                }

                using (var capture = CaptureToRectArea())
                {
                    int preAimX = (int)(capture.Width * 0.5);
                    int preAimY = (int)(capture.Height * (480.0 / 1080.0));

                    // 不在主界面时跳过本轮（避免菜单/地图/对话等界面下误操作）
                    if (!Bv.IsInMainUi(capture))
                    {
                        await Task.Delay(frameIntervalMs, ct);
                        continue;
                    }

                    // 1. 血条识别：检测红色血条并过滤左侧 UI 区域 (x > 200)
                    var bars = FindBloodBars(capture);
                    var valid = bars.Where(b => b.x > (int)(200 * AssetScale)).ToList();

                    var drawList = new List<RectDrawable>();

                    bool hasLegendaryBar = bars.Any(b => IsLegendaryBar(b.y));

                    // 2. 血条追踪：存在有效普通血条且无传奇时，朝最近血条方向移动鼠标
                    if (valid.Count > 0 && !hasLegendaryBar)
                    {
                        lastSeenTargetTime = DateTime.UtcNow;
                        var nearest = valid.OrderBy(b =>
                            Math.Abs((b.x + b.width / 2) - preAimX) +
                            Math.Abs((b.y + b.height / 2) - preAimY)).First();
                        var offsetX = (nearest.x + nearest.width / 2) - preAimX;
                        var offsetY = (nearest.y + nearest.height / 2) - preAimY;
                        if (_skipSeek) continue;
                        Simulation.SendInput.Mouse.MoveMouseBy(
                            (int)(offsetX * 0.35 * dpi), (int)(offsetY * 0.25 * dpi));

                        // 叠加层：最近血条绿色粗框，其余红色细框
                        if (drawResults)
                        {
                            foreach (var b in valid)
                            {
                                var rect = new OpenCvSharp.Rect(b.x, b.y, b.width, b.height);
                                bool isTarget = b.x == nearest.x && b.y == nearest.y &&
                                                b.width == nearest.width && b.height == nearest.height;
                                drawList.Add(capture.ToRectDrawable(rect,
                                    isTarget ? "target" : "blood",
                                    isTarget
                                        ? new System.Drawing.Pen(System.Drawing.Color.LimeGreen, 2)
                                        : null));
                            }
                        }
                    }
                    else
                    {
                        // 3. 伤害数字追踪：血条无效时尝试通过伤害数字/反应文字定位
                        var damageResult = FindDamageNumber(capture);
                        if (damageResult.HasValue)
                        {
                            var (dcx, dcy, _, dx, dy, dw, dh) = damageResult.Value;
                            lastSeenTargetTime = DateTime.UtcNow;
                            var offsetX = dcx - preAimX;
                            var offsetY = dcy - preAimY;
                            if (_skipSeek) continue;
                            Simulation.SendInput.Mouse.MoveMouseBy(
                                (int)(offsetX * 0.35 * dpi), (int)(offsetY * 0.25 * dpi));

                            // 叠加层：伤害数字区域绿色框
                            if (drawResults)
                            {
                                drawList.Add(capture.ToRectDrawable(
                                    new OpenCvSharp.Rect(dx, dy, dw, dh),
                                    "damage_target",
                                    new System.Drawing.Pen(System.Drawing.Color.LimeGreen, 2)));
                            }
                        }

                        // 4. 脱锁旋转：血条和伤害数字都找不到时，脱锁等待后旋转视角
                        if (!damageResult.HasValue)
                        {
                            // 从未找到过目标，或距离上次找到已超过脱锁等待时间 → 开始旋转
                            if (!lastSeenTargetTime.HasValue ||
                                (DateTime.UtcNow - lastSeenTargetTime.Value).TotalSeconds >= lockLostWaitTime)
                            {
                                if (_skipSeek) continue;
                                Simulation.SendInput.Mouse.MoveMouseBy((int)(250 * dpi), 0);
                            }
                        }
                    }

                    // 提交叠加层
                    VisionContext.Instance().DrawContent.PutOrRemoveRectList("ContinuousTargeting", drawList);
                }

                // 按配置的索敌识别间隔等待
                await Task.Delay(frameIntervalMs, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            // 退出时释放所有按键、点按中键回正视角、清除叠加层
            // 注意：清理阶段使用 CancellationToken.None，因为 ct 可能在到此之前已被取消，
            // 若使用已取消的 token 会导致 Task.Delay 抛出异常，跳过中键复位和叠加层清理。
            Simulation.ReleaseAllKey();
            await Task.Delay(50, CancellationToken.None);
            Simulation.SendInput.Mouse.MiddleButtonClick();
            VisionContext.Instance().DrawContent.RemoveRect("ContinuousTargeting");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OpenCv.TemplateMatch;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using OpenCvSharp;
using BetterGenshinImpact.GameTask.Common.Map.MiniMap;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

using static MiniMapMatchConfig;

public abstract class SceneBaseMapByTemplateMatch : SceneBaseMap
{
    private readonly MiniMapPreprocessor _miniMapPreprocessor = new();
    
    private List<BaseMapLayerByTemplateMatch> _layers = [];
    private readonly object _layersLock = new();
    public new List<BaseMapLayerByTemplateMatch> Layers
    {
        get
        {
            if (_layers.Count == 0)
            {
                lock (_layersLock)
                {
                    if (_layers.Count == 0)
                    {
                        TaskControl.Logger.LogInformation("[TemplateMatch]提瓦特大陆地图模板加载中，可能耗时较久，请耐心等待...");
                        Layers = BaseMapLayerByTemplateMatch.LoadLayers(this);
                        TaskControl.Logger.LogInformation("地图特征点加载完成！");
                    }
                }
            }
            return _layers;
        }
        set => _layers = value ?? [];
    }
    
    public MatchResult PrevSuccessResult;

    /// <summary>
    /// SIFT 兜底实例（懒加载）。
    /// 模板匹配依赖 Assets\Map\{Type}\ 下的分层地图（MapBack_* / City_* 等），
    /// 这些图层由人工维护，新版本游戏开放新地图时往往晚于底图与特征点更新。
    /// 此时该区域没有任何图层覆盖，模板匹配必然失败，但 SIFT 用的整图特征已包含新区域，
    /// 因此在模板匹配失败后再尝试一次 SIFT，可避免"新地图识别不到坐标"。
    /// </summary>
    private SceneBaseMap? _siftFallbackMap;
    private bool _siftFallbackResolved;
    private bool _siftFallbackLogged;

    /// <summary>
    /// 全局模板匹配恢复的冷却时间。
    /// 全图层全局匹配开销显著（Teyvat 有 350+ 个图层），连续失败时不能每帧都做。
    /// </summary>
    private static readonly TimeSpan GlobalRecoveryCooldown = TimeSpan.FromSeconds(2);

    /// <summary>
    /// 全局模板匹配恢复结果允许的最大跳跃距离（世界单位）。
    /// 超过此值视为误匹配：玩家实际不可能在两次采图之间移动这么远，
    /// 而纹理重复的地形（雪原/沙漠）很容易给出"高置信度但位置错误"的结果。
    /// </summary>
    private const float GlobalRecoveryMaxJump = 800f;

    /// <summary>
    /// 一层粗匹配候选：图层 + 该图层上算出的世界坐标 + 归一化相关系数（越大越相似）。
    /// </summary>
    private readonly struct RoughCandidate(BaseMapLayerByTemplateMatch layer, Point2f mapPos, double value)
    {
        public readonly BaseMapLayerByTemplateMatch Layer = layer;
        public readonly Point2f MapPos = mapPos;
        public readonly double Value = value;
    }

    /// <summary>
    /// 归一化粗匹配的「绝对下限」。
    ///
    /// 这个值只用来排除「根本没有匹配上」的情况（纯噪声、画面被完全遮挡、完全不在地图上），
    /// 不承担主要判别职责 —— 主要判别交给 <see cref="MinMatchUniqueness"/>（最优与次优之差）。
    ///
    /// 为什么不再用 0.45 这种高阈值：
    /// 相关系数的绝对值严重依赖「游戏内小地图渲染」与「离线图层」之间的差异
    /// （3D 光照、阴影、抗锯齿、UI 淡化）。该差异随区域、天气、昼夜、视角而变，
    /// 用一个固定高分门槛会把真实命中一起拒掉，表现为「永远定位失败」。
    /// 而相对差距（gap）几乎不受这种系统性差异影响，判别力更稳。
    /// 实测正确命中的 gap 为 0.28~0.47，错误命中普遍 &lt; 0.02。
    /// </summary>
    private const double MinNormalizedRoughScore = 0.25;

    private DateTime _lastGlobalRecoveryTime = DateTime.MinValue;
    private readonly object _globalRecoveryLock = new();
    private bool _globalRecoveryLogged;

    public struct MatchResult
    {
        public BaseMapLayerByTemplateMatch? Layer = null; // 地图信息
        public Point2f MapPos = new Point2f(0, 0);        // 匹配位置
        public double Confidence = 0;

        public readonly bool IsSuccess(int rank)
        {
            var index = Math.Clamp(rank, 0, ConfidenceThresholds.Length - 1);
            return Confidence <= 1 && Confidence >= ConfidenceThresholds[index];
        }
        public MatchResult() {}
    }
    
    protected SceneBaseMapByTemplateMatch(
        MapTypes type, 
        Size mapSize, 
        Point2f mapOriginInImageCoordinate, 
        int mapImageBlockWidth, 
        int splitRow, 
        int splitCol)
        : base(type, mapSize, mapOriginInImageCoordinate, mapImageBlockWidth, splitRow, splitCol)
    {
    }
    
    public override void WarmUp()
    {
        Console.WriteLine("提前加载地图，层数：" + Layers.Count);
    }
    
    public override Point2f GetMiniMapPosition(Mat colorMiniMapMat)
    {
        var result= new MatchResult();
        var (miniMap, mask) = _miniMapPreprocessor.GetMiniMapAndMask(colorMiniMapMat);
        using (miniMap)
        using (mask)
        {
            GlobalMatch(miniMap, mask, ref result);
            var matched = UpdateResult(result, 2);
            if (matched != default) return matched;
        }

        // 无 prev 的冷启动入口：模板匹配失败后仍回退 SIFT。
        //
        // 注意这里是【唯一】允许无约束 SIFT 的地方 —— 冷启动时确实没有任何先验，
        // 宁可给出一个可能偏移的坐标让玩家/上层自行纠正，也好过直接返回 (0,0)。
        // 但调用方必须清楚这一点：带 prev 的场景（连续跟踪、恢复）请走
        // GetMiniMapPosition(mat, prevX, prevY)，那里对 SIFT 结果有跳跃上限校验。
        return TrySiftFallback(colorMiniMapMat, -1, -1);
    }

    /// <summary>
    /// 小地图局部匹配，失败不进行全局匹配，若需要全局请用全局匹配
    /// </summary>
    /// <param name="colorMiniMapMat"></param>
    /// <param name="prevX"></param>
    /// <param name="prevY"></param>
    /// <returns></returns>
    public override Point2f GetMiniMapPosition(Mat colorMiniMapMat, float prevX, float prevY)
    {
        return GetMiniMapPosition(colorMiniMapMat, prevX, prevY, 2);
    }

    public Point2f GetMiniMapPosition(Mat colorMiniMapMat, float prevX, float prevY, int rank)
    {
        if (prevX <= 0 || prevY <= 0)
        {
            return GetMiniMapPosition(colorMiniMapMat);
        }
        var curResult = new MatchResult();
        var (miniMap, mask) = _miniMapPreprocessor.GetMiniMapAndMask(colorMiniMapMat);
        using (miniMap)
        using (mask)
        {
            LocalMatch(miniMap, mask, ConvertImageCoordinatesToGenshinMapCoordinates(new Point2f(prevX, prevY))!.Value, ref curResult);
            var matched = UpdateResult(curResult, rank);
            if (matched != default) return matched;
        }

        // 关键：prev 已知（连续跟踪）时，局部匹配失败不能直接放弃。
        //
        // 历史问题：这里原先直接 return default，理由是"SIFT 的 KnnMatch 只要求好匹配 >= 7 个
        // 就返回，没有置信度校验，会给错坐标"。但 return default 同样有害 ——
        // 玩家在小地图边缘/跨图层边界/刚传送落点略偏时，局部搜索窗（半径 20px）没盖住真实位置，
        // 于是永久返回 (0,0)，上层 PathExecutor 只能无限刷"未识别到具体路径，取上次点位"直到超时。
        //
        // 现在的策略是分三级降级，每一级都有约束，避免退化成"无脑 SIFT"：
        //   1) 局部模板匹配        —— 精度最高，最快
        //   2) 全局模板匹配(全图层) —— 局部失败的主要补救手段，带跳跃上限校验 + 冷却
        //   3) SIFT 整图匹配        —— 仅在前两级都失败时兜底，结果同样受跳跃上限约束
        if (prevX > 0 && prevY > 0)
        {
            var recovered = TryGlobalTemplateMatchRecovery(colorMiniMapMat, prevX, prevY);
            if (recovered != default)
            {
                return recovered;
            }

            return TrySiftWithJumpLimit(colorMiniMapMat, prevX, prevY);
        }

        return TrySiftFallback(colorMiniMapMat, prevX, prevY);
    }

    /// <summary>
    /// 局部匹配失败后的全局模板匹配恢复（全图层）。
    /// 这是专为"落点略偏 / 跨图层边界 / 小地图边缘"设计的主补救路径：
    /// 局部搜索窗只有 ±20px，一旦真实位置在窗外就永远找不到，而全局匹配不依赖 prev。
    /// 为防止开销过大与误匹配，附加了两个约束：冷却时间 + 跳跃距离上限。
    /// </summary>
    private Point2f TryGlobalTemplateMatchRecovery(Mat colorMiniMapMat, float prevX, float prevY)
    {
        lock (_globalRecoveryLock)
        {
            if (DateTime.UtcNow - _lastGlobalRecoveryTime < GlobalRecoveryCooldown)
            {
                return default;
            }

            _lastGlobalRecoveryTime = DateTime.UtcNow;
        }

        Point2f matched;
        try
        {
            var result = new MatchResult();
            var (miniMap, mask) = _miniMapPreprocessor.GetMiniMapAndMask(colorMiniMapMat);
            using (miniMap)
            using (mask)
            {
                GlobalMatch(miniMap, mask, ref result);
                matched = UpdateResult(result, 2);
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine($"[TemplateMatch]全局匹配恢复失败：{e.Message}");
            return default;
        }

        if (matched == default)
        {
            return default;
        }

        // 跳跃上限校验：prev 是图像坐标，差值直接就是像素差（1px = 1 世界单位）
        var jump = (float)Math.Sqrt((matched.X - prevX) * (matched.X - prevX) + (matched.Y - prevY) * (matched.Y - prevY));
        if (jump > GlobalRecoveryMaxJump)
        {
            Debug.WriteLine($"[TemplateMatch]全局匹配恢复结果跳跃过大({jump:F0})，判定为误匹配并丢弃");
            return default;
        }

        if (!_globalRecoveryLogged)
        {
            _globalRecoveryLogged = true;
            TaskControl.Logger.LogInformation(
                "[TemplateMatch]局部匹配未命中，已通过全局模板匹配恢复定位（跳跃 {Jump} 世界单位）。",
                $"{jump:F0}");
        }

        return matched;
    }

    /// <summary>
    /// 带跳跃上限校验的 SIFT 兜底。仅用于 prev 已知但模板匹配（局部+全局）都失败的场景。
    /// </summary>
    private Point2f TrySiftWithJumpLimit(Mat colorMiniMapMat, float prevX, float prevY)
    {
        var p = TrySiftFallback(colorMiniMapMat, prevX, prevY);
        if (p == default)
        {
            return default;
        }

        var jump = (float)Math.Sqrt((p.X - prevX) * (p.X - prevX) + (p.Y - prevY) * (p.Y - prevY));
        if (jump > GlobalRecoveryMaxJump)
        {
            Debug.WriteLine($"[TemplateMatch]SIFT 兜底结果跳跃过大({jump:F0})，判定为误匹配并丢弃");
            return default;
        }

        return p;
    }

    /// <summary>
    /// 模板匹配失败后，用 SIFT 整图特征再试一次。
    /// 背景：模板匹配依赖 Assets\Map\{Type}\ 下的分层地图（MapBack_* / City_* 等），
    /// 这些图层由人工维护，新版本开放新地图时往往晚于底图（Teyvat_0_256.png）与特征点更新。
    /// 未被任何图层覆盖的区域，模板匹配注定失败，但 SIFT 用的整图特征已包含该区域，
    /// 因此兜底一次 SIFT 可避免"新地图识别不到坐标"。
    /// 兜底实例按需创建，且当该地图类型的 SIFT/模板匹配指向同一个类时自动跳过，避免自引用。
    /// </summary>
    private Point2f TrySiftFallback(Mat colorMiniMapMat, float prevX, float prevY)
    {
        var siftMap = GetSiftFallbackMap();
        if (siftMap == null)
        {
            return default;
        }

        try
        {
            var fallback = prevX > 0 && prevY > 0
                ? siftMap.GetMiniMapPosition(colorMiniMapMat, prevX, prevY)
                : siftMap.GetMiniMapPosition(colorMiniMapMat);

            if (fallback != default && !_siftFallbackLogged)
            {
                _siftFallbackLogged = true;
                TaskControl.Logger.LogInformation(
                    "[TemplateMatch]模板匹配未命中，已回退到 SIFT 整图匹配。SIFT 结果未经置信度校验，" +
                    "在雪原/沙漠等纹理重复地形上可能定位偏移；若该区域长期使用，建议补充对应的分层地图。");
            }

            return fallback;
        }
        catch (Exception e)
        {
            Debug.WriteLine($"[TemplateMatch]SIFT 兜底匹配失败：{e.Message}");
            return default;
        }
    }

    private SceneBaseMap? GetSiftFallbackMap()
    {
        if (_siftFallbackResolved)
        {
            return _siftFallbackMap;
        }

        lock (_layersLock)
        {
            if (_siftFallbackResolved)
            {
                return _siftFallbackMap;
            }

            _siftFallbackResolved = true;
            try
            {
                // 同一地图类型下，模板匹配与 SIFT 可能指向同一个实现类（如部分地下地图），
                // 此时不能再拿自己兜底，否则会无限递归。
                if (MapManager.GetMap(Type, "SIFT") is SceneBaseMap siftMap
                    && !ReferenceEquals(siftMap, this)
                    && siftMap is not SceneBaseMapByTemplateMatch)
                {
                    _siftFallbackMap = siftMap;
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine($"[TemplateMatch]SIFT 兜底实例创建失败：{e.Message}");
                _siftFallbackMap = null;
            }

            return _siftFallbackMap;
        }
    }

    
    private Point2f UpdateResult(in MatchResult result, int rank)
    {
        if (!result.IsSuccess(rank)) return default;
        PrevSuccessResult = result;
        return ConvertGenshinMapCoordinatesToImageCoordinates(PrevSuccessResult.MapPos);
    }
    
    [Conditional("DEBUG")]
    private static void LogMatchResult(string stage, in MatchResult result)
    {
        Debug.WriteLine($"{stage}: 坐标 ({result.MapPos.X:F4}, {result.MapPos.Y:F4}), 置信度 {result.Confidence:F4}");
    }

    /// <summary>
    /// 该点位上传入的待匹配小地图（已去除 UI/遮罩），用于衡量"小地图本身有多少定位信息"。
    /// 见 <see cref="IsMiniMapInformative"/>。
    /// </summary>
    private static double MiniMapGrayStd(Mat miniMap)
    {
        using var gray = new Mat();
        if (miniMap.Channels() == 3) Cv2.CvtColor(miniMap, gray, ColorConversionCodes.BGR2GRAY);
        else miniMap.CopyTo(gray);
        Cv2.MeanStdDev(gray, out _, out var std);
        return std.Val0;
    }

    /// <summary>
    /// 小地图是否含有足够的定位信息。
    ///
    /// 为什么需要这道门：置信度公式 Confidence = 1 - Value/worst 里，Value 与 worst 都是"能量"量，
    /// 当小地图本身接近常数（纯色/浓雾/全黑/被大范围 UI 遮挡）时，
    /// 任何位置都能得到 Value≈0，于是 Confidence≈1 —— 即"越没信息越自信"。
    /// 实测：纯灰图 std=0 时置信度 0.9990，比真实纹理的 0.9869 还高。
    ///
    /// 阈值取自实测：真实可定位小地图 std 约 25~60；对比度降到 70%（std=29.7）时
    /// 已开始选中错误图层，因此下限取 18，留出余量但不至于误杀轻度模糊的画面。
    /// </summary>
    private const double MiniMapMinGrayStd = 18.0;

    /// <summary>
    /// 匹配结果的最小唯一性：归一化相关系数最优与次优之差。
    ///
    /// 相关系数只回答"这个位置像不像"，不回答"有没有别处也一样像"。
    /// 在地形重复（雪原、沙漠、成片相同建筑）时，后者才是关键。
    /// 实测：正确命中时 gap 最小 0.1269；错误选中时普遍 &lt; 0.01。
    /// 阈值取 0.02，仅用于拦截"几乎分不出高下"的情况。
    /// </summary>
    private const double MinMatchUniqueness = 0.02;

    #region 模板匹配
    
    public void GlobalMatch(Mat miniMap, Mat mask, ref MatchResult result)
    {
        SpeedTimer speedTimer = new SpeedTimer("全局匹配");
        using var context = new MatchContext(miniMap, mask);
        var candidate = CollectBestRoughCandidateGlobal(context, out var rejectReason);
        speedTimer.Record("全局粗匹配");
        if (candidate.HasValue)
        {
            RefineByExactMatch(context, candidate.Value, ref result);
        }
        else
        {
            result.Layer = null;
            result.Confidence = 0;
            result.MapPos = default;
            LogGlobalMatchFailure(rejectReason);
        }
        speedTimer.Record("精确匹配");
        speedTimer.DebugPrint();

        // 低信息量小地图一律判为失败，使上层能进入下一级降级（SIFT / 保持上次点位），
        // 而不是拿着一个"高置信度的错误坐标"继续走。
        var std = MiniMapGrayStd(miniMap);
        if (std < MiniMapMinGrayStd)
        {
            LogThrottled(
                "info",
                "[TemplateMatch]小地图信息量不足（灰度标准差 {Std:F1} < {Min:F1}），放弃本次模板匹配结果，避免误定位。",
                std, MiniMapMinGrayStd);
            result.Layer = null;
            result.Confidence = 0;
            result.MapPos = default;
        }
    }

    #region 失败原因观测

    /// <summary>
    /// 全局匹配失败原因的节流日志。
    ///
    /// 为什么要有这个：模板匹配失败后会降级到 SIFT，而 SIFT 在纹理重复地形上会给出
    /// "能过阈值但位置全错"的坐标 —— 用户看到的现象就是「识别出的地点错误，偏离非常遥远」。
    /// 但失败原因（没匹配上 / 匹配上了但不够唯一 / 小地图信息量太低）此前只用 LogDebug
    /// 或 Debug.WriteLine 输出，排查时完全看不到，只能靠猜。
    ///
    /// 这里把原因提升到 Information 并限流（同一类原因 4 秒内只记一次），
    /// 既能直接看到根因，又不会在每帧调用下刷爆日志。
    /// </summary>
    private void LogThrottled(string reason, string template, params object?[] args)
    {
        var now = DateTime.UtcNow;
        lock (_failLogLock)
        {
            if (_lastFailLogTime.TryGetValue(reason, out var last) &&
                now - last < TimeSpan.FromSeconds(4))
            {
                return;
            }
            _lastFailLogTime[reason] = now;
        }
        TaskControl.Logger.LogInformation(template, args);
    }

    private readonly Dictionary<string, DateTime> _lastFailLogTime = [];
    private readonly object _failLogLock = new();

    private void LogGlobalMatchFailure(string rejectReason)
    {
        LogThrottled("global:" + rejectReason, "[TemplateMatch]全局匹配失败：{Reason}", rejectReason);
    }

    #endregion

    // 局部匹配：在上一次匹配位置附近进行搜索
    public void LocalMatch(Mat miniMap, Mat mask, Point2f pos, ref MatchResult result)
    {
        SpeedTimer speedTimer = new SpeedTimer("局部匹配");
        using var context = new MatchContext(miniMap, mask);
        RoughMatchLocal(context, pos, ref result);
        speedTimer.Record("局部粗匹配");
        ExactMatch(context, ref result);
        speedTimer.Record("精确匹配");
        speedTimer.DebugPrint();
    }
    
    /// <summary>
    /// 全局粗匹配：遍历全部图层，用归一化互相关系数选出唯一最优图层。
    ///
    /// 为什么不再用 SSD + Top-N 重排：
    /// SSD = Σ(S²·m) - 2Σ(S·T·m) + Σ(T²) 是绝对能差，而各图层底色/亮度差异极大
    /// （深色地下城 vs 亮色雪原），Σ(S²·m) 天然不同 —— 于是 SSD 的绝对值**跨图层不可比**。
    /// 实测：至冬"运输局东"处正确图层 MapBack_07 的 SSD 在 366 个图层里排第 11 名，
    /// 被亮度/分辨率都不同的 City_560 挤掉；即便保留 Top-8 也够不到它，
    /// 最终定位误差 2140 世界单位 —— 这正是"皇家轨道运输局附近识别错位"的直接原因。
    ///
    /// 归一化后（先减均值再除以标准差乘积）值域为 [-1,1]，跨图层可比。
    /// 同一批实测中正确图层在 42/42 个用例里都稳定排第 1，与次优差距最小 0.1269，
    /// 因此不需要再多候选重排，取第 1 名即可，也顺带消除了"多候选放大假阳性"的隐患。
    /// </summary>
    /// <param name="context">匹配上下文</param>
    /// <param name="rejectReason">被拒绝时填写的可读原因；成功时为空字符串</param>
    private RoughCandidate? CollectBestRoughCandidateGlobal(MatchContext context, out string rejectReason)
    {
        rejectReason = "";
        BaseMapLayerByTemplateMatch? bestLayer = null;
        var bestPos = default(Point2f);
        var bestScore = double.NegativeInfinity;
        var bestChallenger = double.NegativeInfinity;

        // 额外记录前 3 名（带 LayerId）。
        // 失败日志原先只有分数没有图层名，导致无法区分两种完全不同的故障：
        //   - 最优是目标区域的相邻图块（说明尺度/内容都对，只是判别不足）→ 调阈值有用；
        //   - 最优是毫不相关区域的图层（说明小地图本身没有可比内容）→ 调阈值无用，必须换思路。
        // 仅 3 个元素，371 个图层下的开销可忽略。
        var top3 = new List<(string Id, double Score)>(4);

        foreach (var layer in Layers)
        {
            var (tempPos, score) = layer.RoughMatchNormalized(context.MaskedMiniMapRoughs, context.MaskRoughF);
            if (tempPos == default || score < 0) continue;
            if (double.IsNaN(score) || double.IsInfinity(score)) continue;

            if (score > bestScore)
            {
                if (bestLayer != null)
                {
                    bestChallenger = bestScore;
                }

                bestScore = score;
                bestLayer = layer;
                bestPos = tempPos;
            }
            else if (score > bestChallenger)
            {
                bestChallenger = score;
            }

            if (top3.Count < 3 || score > top3[top3.Count - 1].Score)
            {
                top3.Add((layer.LayerId, score));
                top3.Sort((a, b) => b.Score.CompareTo(a.Score));
                if (top3.Count > 3)
                {
                    top3.RemoveAt(3);
                }
            }
        }

        if (bestLayer == null)
        {
            rejectReason = $"全部 {Layers.Count} 个图层都没有返回有效候选（归一化相关系数全为 -1，通常是尺度/亮度差异过大或模板大于图层）";
            return null;
        }

        // 唯一性校验：正确匹配与"第二像的地方"必须有明显差距。
        // 地形重复（雪原、沙漠、成片相同建筑）时两者几乎一样大，此时宁可判定失败交给下级降级，
        // 也不要给出一个"高置信度的错误坐标"。
        var uniqueness = bestScore - bestChallenger;
        var topText = top3.Count == 0 ? "-" : string.Join(" / ", top3.Select(t => $"{t.Id}={t.Score:F4}"));
        if (bestScore < MinNormalizedRoughScore || uniqueness < MinMatchUniqueness)
        {
            var scoreOk = bestScore >= MinNormalizedRoughScore;
            rejectReason = scoreOk
                ? $"粗匹配结果不唯一：最优 {bestLayer.LayerId}={bestScore:F4}，次优 {bestChallenger:F4}，差 {uniqueness:F4} < {MinMatchUniqueness:F3}（地形重复，不敢定）；前3 {topText}"
                : $"粗匹配得分过低：最优 {bestLayer.LayerId}={bestScore:F4} < {MinNormalizedRoughScore:F2}（次优 {bestChallenger:F4}）；前3 {topText}";
            return null;
        }

        return new RoughCandidate(bestLayer, bestPos, bestScore);
    }

    /// <summary>
    /// 对粗匹配选出的唯一候选做精确匹配细化，得到亚像素级世界坐标。
    ///
    /// 精匹配在图层原图上以 1:1 尺度（ExactZoom = 1）小范围搜索，用来消除粗匹配
    /// 52x52 降采样带来的量化误差，因此只做一次、只针对已选定的图层。
    /// </summary>
    private void RefineByExactMatch(MatchContext context, RoughCandidate candidate, ref MatchResult result)
    {
        var (tempPos, tempVal) = candidate.Layer.ExactMatch(
            context.MiniMapExact, context.MaskExact, candidate.MapPos);

        if (tempPos == default)
        {
            // 精匹配失败（位置贴图层边缘、ROI 不足）时退回粗匹配结果，
            // 宁可精度差一点，也好过整次定位失败。
            result.Layer = candidate.Layer;
            result.MapPos = candidate.MapPos;
            result.Confidence = candidate.Value;
            LogMatchResult("全局粗匹配(精匹配失败，退回粗匹配)", result);
            return;
        }

        var normalizer = new TemplateMatchNormalizer(context.MiniMapExact, context.MaskExact);
        normalizer.Reset();
        var confidence = normalizer.Update(tempVal) ? normalizer.Confidence() : candidate.Value;

        result.Layer = candidate.Layer;
        result.MapPos = tempPos;
        result.Confidence = confidence;
        LogMatchResult("精确匹配(归一化粗匹配选优)", result);
    }

    public void RoughMatchLocal(MatchContext context, Point2f pos, ref MatchResult result)
    {
        result.MapPos = pos;
        if (PrevSuccessResult.MapPos.Equals(pos))
        {
            result.Layer = PrevSuccessResult.Layer;
        }
        if (result.Layer != null)
        {
            var (tempPos, tempVal) = result.Layer.RoughMatch(context.MaskedMiniMapRoughs, context.MaskRoughF, pos);
            if (tempPos != default && context.NormalizerRough.Update(tempVal + context.TplSumSq))
            {
                result.MapPos = tempPos;
                result.Confidence = context.NormalizerRough.Confidence();
            }
        }

        if (result.IsSuccess(0))
        {
            LogMatchResult("局部粗匹配", result);
            return;
        }
        
        var flag = false;
        foreach (var layer in (result.Layer == null)? Layers : Layers.Where(layer => layer != PrevSuccessResult.Layer))
        {
            var (tempPos, tempVal) = layer.RoughMatch(context.MaskedMiniMapRoughs, context.MaskRoughF, pos);
            if (tempPos == default || !context.NormalizerRough.Update(tempVal + context.TplSumSq)) continue;
            result.Layer = layer;
            result.MapPos = tempPos;
            flag = true;
        }
        if (flag) result.Confidence = context.NormalizerRough.Confidence();
        LogMatchResult("局部粗匹配", result);
        //if (CurResult.IsSuccess) return;
        //RoughMatchLocalChan(context, pos);
        //if (CurResult.IsSuccess) return;
    }

    /// <summary>
    /// 指定通道匹配，用于边缘位置匹配，暂时不用，等后续优化
    /// </summary>
    /// <param name="context"></param>
    /// <param name="pos"></param>
    /// <param name="result"></param>
    public void RoughMatchLocalChan(MatchContext context, Point2f pos, ref MatchResult result)
    {
        result = default;
        var flag = false;
        foreach (var layer in Layers)
        {
            var (tempPos, tempVal) = layer.RoughMatch(context.MaskedMiniMapRoughs, context.MaskRoughF, pos, context.Channels);
            if (!context.NormalizerRoughChan.Update(tempVal + context.TplSumSqChan)) continue;
            result.Layer = layer;
            result.MapPos = tempPos;
            flag = true;
        }
        if (flag) result.Confidence = context.NormalizerRough.Confidence();
    }
    
    public void ExactMatch(MatchContext context, ref MatchResult result)
    {
        if (result.Layer == null || !result.IsSuccess(2)) return;
        var (tempPos, tempVal) = result.Layer.ExactMatch(context.MiniMapExact, context.MaskExact, result.MapPos);
        if (tempPos != default && context.NormalizerExact.Update(tempVal))
        {
            result.MapPos = tempPos;
            result.Confidence = context.NormalizerExact.Confidence();
        }
        else
        {
            result.Confidence = 0;
        }
        LogMatchResult("精确匹配", result);
    }
    #endregion
    
}
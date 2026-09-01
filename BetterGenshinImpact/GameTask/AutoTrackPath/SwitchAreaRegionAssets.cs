using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.Assets;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>
/// SwitchArea 地区菜单模板资产（switch-area-template-match spec）。
/// 加载全部地区模板，建 areaName → RecognitionObject 映射（UseMask + 3 通道 + MaskColor）。
///
/// 关于 ROI：生产路径（TpTaskFastDrag.TryMatchSwitchAreaTemplate）已改为"模板不绑定格子"的逐格轮询——
/// 菜单是固定 2×8 网格，但每次显示的地区数量可变（1~16 个）、顺序可能乱，目标地区模板可能出现在任意一格，
/// 由 MatchInAllCells 遍历 16 格、每格 clone 目标模板并覆盖 RegionOfInterest 后小区域匹配，规避整块右 1/3
/// 背景稀释导致匹配度过低掉进 OCR 的问题。
///
/// Get(areaName) 返回预构建的模板识别对象（含整块右 1/3 ROI），供逐格轮询匹配前的模板对象使用；
/// MatchInAllCells 生产路径按需覆盖 ROI 做逐格匹配。
///
/// 模板文件缺失 / 加载失败 → 该地区跳过（Get 返回 null），走 OCR 兜底，不抛异常（requirements E1/E2）。
/// 遵循 QuickTeleportAssets 的 CaptureAssetsCache 按分辨率缓存模式。
/// </summary>
internal sealed class SwitchAreaRegionAssets
{
    private static readonly CaptureAssetsCache<SwitchAreaRegionAssets> Cache = new(static size => new SwitchAreaRegionAssets(size));

    /// <summary>
    /// 全部 16 个地区名（8 大区 + 6 独立地图 + 尘歌壶/千星奇域）。
    /// 保持中文名，作为 _byAreaName / Get() / AreaNames 的 key（生产与调试调用方传中文区域名）。
    /// </summary>
    private static readonly string[] AllAreaNames =
    [
        "蒙德", "璃月", "稻妻", "须弥", "枫丹", "纳塔", "挪德卡莱", "至冬",
        "层岩巨渊", "渊下宫", "旧日之海", "远古圣山", "空之神殿", "霜月",
        "尘歌壶", "千星奇域"
    ];

    /// <summary>
    /// 中文地区名 → 英文模板文件名。
    /// 模板 PNG 已统一改为英文名（如 mengde.png）。原因：OpenCV Mat.FromStream 在运行时对中文文件名的
    /// 模板加载/识别失败（用户实测：同一张图用英文名 ly.png 能识别，改回中文名识别不了）。
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> AreaNameToTemplateFile = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["蒙德"] = "switch\\mengde.png",
        ["璃月"] = "switch\\liyue.png",
        ["稻妻"] = "switch\\daozhi.png",
        ["须弥"] = "switch\\xumi.png",
        ["枫丹"] = "switch\\fengdan.png",
        ["纳塔"] = "switch\\nata.png",
        ["挪德卡莱"] = "switch\\nuodekalai.png",
        ["至冬"] = "switch\\zhidong.png",
        ["层岩巨渊"] = "switch\\cengyanjuyuan.png",
        ["渊下宫"] = "switch\\yuanxiagong.png",
        ["旧日之海"] = "switch\\jiurizhihai.png",
        ["远古圣山"] = "switch\\yuangushanshan.png",
        ["空之神殿"] = "switch\\kongzhishendian.png",
        ["霜月"] = "switch\\shuangyue.png",
        ["尘歌壶"] = "switch\\chengehu.png",
        ["千星奇域"] = "switch\\qianxingqiyu.png",
    };

    private readonly IReadOnlyDictionary<string, RecognitionObject> _byAreaName;
    // 第二层 b 变体模板（switch\xxxb.png），见 MatchInAllCells 三层降级说明。
    private readonly IReadOnlyDictionary<string, RecognitionObject> _byAreaNameB;

    private SwitchAreaRegionAssets(CaptureSize captureSize)
    {
        int w = captureSize.Width;
        int h = captureSize.Height;
        // ROI = 右 1/3，与 SwitchArea 现有 OCR 分支一致
        Rect roi = new(w * 2 / 3, 0, w / 3, h);

        var dict = new Dictionary<string, RecognitionObject>();
        var dictB = new Dictionary<string, RecognitionObject>();
        foreach (var areaName in AllAreaNames)
        {
            // 第一层：原模板
            if (AreaNameToTemplateFile.TryGetValue(areaName, out var primaryFile)
                && TryBuildFile(areaName, primaryFile, w, h, roi) is { } ro)
            {
                dict[areaName] = ro;
            }

            // 第二层：b 变体模板（switch\xxxb.png）。缺失 → 该地区无第二层，第一层失败直接走 OCR。
            if (GetBFile(areaName) is { } bFile
                && TryBuildFile(areaName, bFile, w, h, roi) is { } roB)
            {
                dictB[areaName] = roB;
            }
        }
        _byAreaName = dict;
        _byAreaNameB = dictB;
    }

    /// <summary>
    /// 由第一层模板文件派生第二层 b 变体路径：取文件名，在 ".png" 前插入 "b"，并放到 switch\ 目录下。
    /// 例：switch\mengde.png → switch\mengdeb.png；cengyanjuyuan.png → switch\cengyanjuyuanb.png。
    /// 千星奇域未提供 b 变体（switch\qianxingqiyub.png 不存在）→ 返回 null，该地区无第二层。
    /// </summary>
    private static string? GetBFile(string areaName)
    {
        if (!AreaNameToTemplateFile.TryGetValue(areaName, out var primaryFile))
        {
            return null;
        }

        // 取纯文件名（去目录），如 mengde.png / cengyanjuyuan.png
        int idx = primaryFile.LastIndexOf('\\');
        string fileName = idx >= 0 ? primaryFile[(idx + 1)..] : primaryFile;
        string baseName = fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^4]
            : fileName;
        return $"switch\\{baseName}b.png";
    }

    /// <summary>
    /// 尝试用指定模板文件构建单个地区的模板识别对象；模板缺失 / 加载失败返回 null（不抛异常，E1/E2 → OCR 兜底）。
    /// </summary>
    private static RecognitionObject? TryBuildFile(string areaName, string templateFile, int w, int h, Rect roi)
    {
        try
        {
            var mat = GameTaskManager.LoadAssetImage("QuickTeleport", templateFile, w, h);
            return new RecognitionObject
            {
                Name = areaName,
                RecognitionType = RecognitionTypes.TemplateMatch,
                TemplateImageMat = mat,
                RegionOfInterest = roi,
                Use3Channels = true, 
                DrawOnWindow = false,   // 生产路径不画框（避免叠加层杂乱）
                Threshold = 0.8
            }.InitTemplate();
        }
        catch
        {
            // 模板缺失 / 加载失败（FileNotFoundException / 分辨率异常等）→ 该地区跳过，走 OCR 兜底。
            // 可恢复异常（地区模板是可选加速，缺失不影响正确性），失败处理在调用层（Get 返回 null → OCR），
            // 不向上抛，保证 SwitchArea 生产路径不被模板缺失中断（requirements E1/E2 / FR2）。
            return null;
        }
    }

    /// <summary>
    /// 取某地区的模板识别对象；未配置 / 模板缺失返回 null。
    /// </summary>
    public RecognitionObject? Get(string areaName) => _byAreaName.TryGetValue(areaName, out var ro) ? ro : null;

    /// <summary>
    /// 是否已为该地区加载模板。
    /// </summary>
    public bool Contains(string areaName) => _byAreaName.ContainsKey(areaName);

    /// <summary>
    /// 全部 16 个地区名（含模板缺失的），供调试方法遍历并提示缺失。
    /// </summary>
    public IReadOnlyCollection<string> AreaNames => AllAreaNames;

    public static SwitchAreaRegionAssets Get(int captureWidth, int captureHeight) => Cache.Get(captureWidth, captureHeight);

    /// <summary>
    /// 在给定截图上，用目标地区模板做一次整块右 1/3 匹配，返回目标当前所在位置的命中矩形。
    ///
    /// 实现完全复刻用户实证成功的方式（16 个模板逐一验证全部命中）：
    ///   直接 ra.Find(templateRo)，不 clone、不逐格轮询、不改 ROI——
    ///   templateRo 的 RegionOfInterest 即整块右 1/3，命中返回的 Region.X/Y 即目标地区所在格子的绝对坐标，可直接点击。
    /// 用 mask=false（绿色背景不特殊处理）+ Use3Channels=true，与用户测试代码一致。
    ///
    /// 返回 null 表示目标地区未命中（当前未显示，或低于阈值）→ 调用方走 OCR 兜底。
    /// </summary>
    /// <param name="ra">当前截图。</param>
    /// <param name="areaName">目标地区名（如"蒙德"）。</param>
    /// <returns>目标当前所在位置的命中矩形（相对截图的绝对坐标）；未命中返回 null。</returns>
    public Rect? MatchInAllCells(ImageRegion ra, string areaName)
    {
        // 三层降级：第一层原模板 → 第二层 b 变体模板 → null（OCR 兜底，由调用方处理）。
        if (TryMatch(ra, Get(areaName)) is { } hit1)
        {
            return hit1;
        }

        if (TryMatch(ra, GetB(areaName)) is { } hit2)
        {
            return hit2;
        }

        return null;   // 两层都未命中 → OCR 兜底
    }

    /// <summary>
    /// 用指定识别对象做一次整块右 1/3 匹配；命中（存在且过阈值）返回命中矩形，否则返回 null。
    /// </summary>
    private Rect? TryMatch(ImageRegion ra, RecognitionObject? templateRo)
    {
        if (templateRo == null)
        {
            return null;
        }

        using var hit = ra.Find(templateRo);
        double score = hit.MatchScore ?? 0d;
        if (hit.IsExist() && SwitchAreaTemplateMatchDecisions.IsHit(score, templateRo.Threshold))
        {
            return new Rect(hit.X, hit.Y, hit.Width, hit.Height);
        }

        return null;
    }

    /// <summary>
    /// 取某地区的第二层 b 变体模板识别对象；未配置 / 模板缺失返回 null。
    /// </summary>
    public RecognitionObject? GetB(string areaName) => _byAreaNameB.TryGetValue(areaName, out var ro) ? ro : null;
}

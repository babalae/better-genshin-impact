using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoFight.Model;

/// <summary>
/// 用于处理主界面右侧角色编号的一些方法
/// </summary>
public class PartyAvatarSideIndexHelper
{
    /// <summary>
    /// 角色编号以当前模板匹配结果的情况下的Y轴公差
    /// </summary>
    private static readonly int IndexRectDistanceY = 96;

    /// <summary>
    /// 检查当前联机状态
    /// </summary>
    /// <param name="imageRegion"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static MultiGameStatus DetectedMultiGameStatus(ImageRegion imageRegion, AutoFightAssets? autoFightAssets = null, ILogger? logger = null)
    {
        if (autoFightAssets == null)
        {
            autoFightAssets = AutoFightAssets.Instance;
        }

        if (logger == null)
        {
            logger = TaskControl.Logger;
        }

        var status = new MultiGameStatus();
        // 判断当前联机人数
        var pRaList = imageRegion.FindMulti(autoFightAssets.PRa);
        if (pRaList.Count > 0)
        {
            status.IsInMultiGame = true;
            var num = pRaList.Count + 1;
            if (num > 4)
            {
                throw new Exception("当前处于联机状态，但是队伍人数超过4人，无法识别");
            }

            status.PlayerCount = num;

            // 联机状态下判断
            var onePRa = imageRegion.Find(autoFightAssets.OnePRa);
            if (onePRa.IsExist())
            {
                logger.LogInformation("当前处于联机状态，且当前账号是房主，联机人数{Num}人", num);
                status.IsHost = true;
            }
            else
            {
                logger.LogInformation("当前处于联机状态，且在别人世界中，联机人数{Num}人", num);
            }
        }
        else
        {
            // 没有其他联机玩家的情况下，也有可能是单人房主
            var onePRa = imageRegion.Find(autoFightAssets.OnePRa);
            if (onePRa.IsExist())
            {
                logger.LogInformation("当前处于联机状态，但是没有其他玩家连入");
                status.IsInMultiGame = true;
                status.IsHost = true;
                status.PlayerCount = 1;
            }
        }

        return status;
    }

    /// <summary>
    /// 根据已知的某个角色编号位置，计算其他角色编号的位置
    /// </summary>
    /// <param name="knownIndex">已知编号</param>
    /// <param name="knownRect">已知编号矩形</param>
    /// <param name="targetIndex">目标编号</param>
    /// <returns>目标编号矩形</returns>
    public static Rect GetIndexRectFromKnownIndexRect(int knownIndex, Rect knownRect, int targetIndex)
    {
        var s = TaskContext.Instance().SystemInfo.AssetScale;

        //  y_k + (n - k) * d
        int y = knownRect.Y + (targetIndex - knownIndex) * (int)(IndexRectDistanceY * s);

        return new Rect(knownRect.X, y, knownRect.Width, knownRect.Height);
    }

    public static Rect GetIndexRectFromKnownCurrentAvatarFlag(Rect currRect)
    {
        var s = TaskContext.Instance().SystemInfo.AssetScale;
        return new Rect(currRect.X + (int)(126 * s), currRect.Y - (int)(194 * s), (int)(16 * s), (int)(17 * s));
    }

    public static (List<Rect>, List<Rect>) GetAllIndexRects(ImageRegion imageRegion, MultiGameStatus multiGameStatus, ILogger logger, ElementAssets elementAssets, ISystemInfo systemInfo)
    {
        try
        {
            // 新的动态获取角色编号位置逻辑
            return GetAllIndexRectsNew(imageRegion, multiGameStatus, logger, elementAssets, systemInfo);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "使用新方法获取角色编号位置失败");
            logger.LogWarning("使用新方法获取角色编号位置失败，原因：" + ex.Message);
            logger.LogWarning("尝试使用旧的写死位置逻辑");
            // 旧的写死位置逻辑
            return GetAllIndexRectsOld(imageRegion, multiGameStatus);
        }
    }

    private static (List<Rect>, List<Rect>) GetAllIndexRectsOld(ImageRegion imageRegion, MultiGameStatus multiGameStatus)
    {
        List<Rect> avatarSideIconRectList;
        List<Rect> avatarIndexRectList;
        if (multiGameStatus.IsInMultiGame)
        {
            var p = multiGameStatus.IsHost ? "1p" : "p";
            avatarSideIconRectList = new List<Rect>(AutoFightAssets.Instance.AvatarSideIconRectListMap[$"{p}_{multiGameStatus.PlayerCount}"]);
            avatarIndexRectList = new List<Rect>(AutoFightAssets.Instance.AvatarIndexRectListMap[$"{p}_{multiGameStatus.PlayerCount}"]);
        }
        else
        {
            avatarSideIconRectList = new List<Rect>(AutoFightAssets.Instance.AvatarSideIconRectList);
            avatarIndexRectList = new List<Rect>(AutoFightAssets.Instance.AvatarIndexRectList);
        }

        // 6.0 版本 队伍下的 草露 进度条 导致位置偏移
        AvatarSideFixOffset(imageRegion, avatarSideIconRectList, avatarIndexRectList);
        return (avatarIndexRectList, avatarSideIconRectList);
    }

    public static bool HasAnyIndexRect(ImageRegion imageRegion)
    {
        return ElementAssets.Instance.IndexList.Select(indexRo => imageRegion.Find(indexRo)).Any(indexRes => indexRes.IsExist());
    }

    public static int CountIndexRect(ImageRegion imageRegion)
    {
        return ElementAssets.Instance.IndexList.Select(indexRo => imageRegion.Find(indexRo)).Count(indexRes => indexRes.IsExist());
    }

    public static bool HasActiveAvatarArrow(ImageRegion imageRegion)
    {
        return imageRegion.Find(ElementAssets.Instance.CurrentAvatarThreshold).IsExist();
    }

    public static (List<Rect>, List<Rect>) GetAllIndexRectsNew(ImageRegion imageRegion, MultiGameStatus multiGameStatus, ILogger logger, ElementAssets elementAssets, ISystemInfo systemInfo)
    {
        // 找到编号块
        var i1 = imageRegion.Find(elementAssets.Index1);
        var i2 = imageRegion.Find(elementAssets.Index2);
        var i3 = imageRegion.Find(elementAssets.Index3);
        var i4 = imageRegion.Find(elementAssets.Index4);
        List<Rect> indexRectList = [i1.ToRect(), i2.ToRect(), i3.ToRect(), i4.ToRect()];
        int existNum = indexRectList.Count(indexRect => indexRect != default);
        if (existNum == multiGameStatus.MaxControlAvatarCount)
        {
            // 识别存在个数和当前能控制的最大角色数相等,意味者全部识别,直接返回
            var notNullIndexRectList = indexRectList.Where(r => r != default).ToList();
            return (notNullIndexRectList, GetAvatarSideIconRectFromIndexRect(notNullIndexRectList, systemInfo));
        }
        else
        {
            // 为什么这里要用箭头确认一遍？因为出战角色编号框的识别率不是100%，需要用箭头来辅助确认。这也是为了保证非满队情况下的队伍识别率
            // 非出战角色编号框识别率100%
            var curr = imageRegion.Find(elementAssets.CurrentAvatarThreshold); // 当前出战角色标识
            if (curr.IsExist())
            {
                var (knownIndex, knownRect) = GetKnownIndexAndRect(indexRectList);
                if (knownRect == default)
                {
                    // 没有已知的编号位置，这种情况下可能是单人队
                    // 直接用出战角色标识来反推
                    var oneIndexRect = GetIndexRectFromKnownCurrentAvatarFlag(curr.ToRect());
                    logger.LogInformation("当前编队中可能只存在一个角色（且角色编号未正确识别）");
                    return ([oneIndexRect], [GetAvatarSideIconRectFromIndexRect(oneIndexRect, systemInfo)]);
                }
                else
                {
                    // 有已知的编号位置，通过已知位置来推测其他位置
                    for (int i = 0; i < indexRectList.Count; i++)
                    {
                        if (indexRectList[i] == default)
                        {
                            var rect = GetIndexRectFromKnownIndexRect(knownIndex, knownRect, i + 1);
                            if (IsIntersecting(curr.Y, curr.Height, rect.Y, rect.Height))
                            {
                                // 如果和当前出战角色标识相交，说明这个位置是正确的
                                indexRectList[i] = rect;
                                logger.LogInformation("当前出战角色未正确识别，通过出战标识推测角色编号为{Index}", i + 1);
                            }
                        }
                    }

                    // 校验推测结果（编号从 1 开始必定连续）
                    if (AreNullsAtEnd(indexRectList))
                    {
                        var notNullIndexRectList = indexRectList.Where(r => r != default).ToList();
                        return (notNullIndexRectList, GetAvatarSideIconRectFromIndexRect(notNullIndexRectList, systemInfo));
                    }
                    else
                    {
                        throw new Exception("校验角色列表识别结果失败，角色编号不是连续的！");
                    }
                }
            }
            else
            {
                // 没有出战角色标识的情况下，直接抛出错误走写死逻辑
                throw new Exception("找不到出战角色编号块与当前出战角色标识！");
            }
        }
    }

    private static (int, Rect) GetKnownIndexAndRect(List<Rect> indexRectList)
    {
        for (int i = 0; i < indexRectList.Count; i++)
        {
            if (indexRectList[i] != default)
            {
                return (i + 1, indexRectList[i]);
            }
        }

        return (-1, default);
    }

    public static Rect GetAvatarSideIconRectFromIndexRect(Rect indexRect, ISystemInfo systemInfo)
    {
        var s = systemInfo.AssetScale;
        return new Rect(indexRect.X - (int)(91 * s), indexRect.Y - (int)(47 * s), (int)(82 * s), (int)(82 * s));
    }

    public static List<Rect> GetAvatarSideIconRectFromIndexRect(List<Rect> indexRect, ISystemInfo systemInfo)
    {
        return indexRect.Select(r => GetAvatarSideIconRectFromIndexRect(r, systemInfo)).ToList();
    }

    public static bool IsIntersecting(double y1, double h1, double y2, double h2)
    {
        // 计算第一个区域的结束位置
        double end1 = y1 + h1;
        // 计算第二个区域的结束位置
        double end2 = y2 + h2;
        return y1 < end2 && y2 < end1;
    }

    public static bool AreNullsAtEnd(List<Rect> list)
    {
        int firstNullIndex = list.FindIndex(x => x == default); // 找到第一个 null 的索引
        return firstNullIndex == -1 || list.Skip(firstNullIndex).All(x => x == default); // 检查从第一个 null 开始到末尾是否都是 null
    }

    /// <summary>
    /// 6.0 版本 队伍下的 草露 进度条 导致位置偏移
    /// 
    /// </summary>
    /// <param name="imageRegion"></param>
    /// <param name="avatarSideIconRectList"></param>
    /// <param name="avatarIndexRectList"></param>
    public static bool AvatarSideFixOffset(ImageRegion imageRegion, List<Rect> avatarSideIconRectList, List<Rect> avatarIndexRectList)
    {
        // 角色序号 左上角 坐标偏移（+2, -5）后存在3个白色点，则认为存在 草露 进度条
        // 存在 草露 进度条时候整体上移 14 个像素
        var whitePointCount = 0;
        foreach (var rectIndex in avatarIndexRectList)
        {
            int x = rectIndex.X + 2;
            int y = rectIndex.Y - 5;
            var color = imageRegion.SrcMat.At<Vec3b>(y, x);
            if (color is { Item0: 255, Item1: 255, Item2: 255 })
            {
                whitePointCount++;
            }
        }

        if (whitePointCount < 3)
        {
            return false;
        }

        TaskControl.Logger.LogInformation("检测到右侧队伍上偏移，进行位置偏移");

        for (var i = 0; i < avatarSideIconRectList.Count; i++)
        {
            var rect = avatarSideIconRectList[i];
            rect.Y -= 14;
            avatarSideIconRectList[i] = rect;
        }

        for (var i = 0; i < avatarIndexRectList.Count; i++)
        {
            var rect = avatarIndexRectList[i];
            rect.Y -= 14;
            avatarIndexRectList[i] = rect;
        }

        return true;
    }

    /// <summary>
    /// 识别当前出战角色编号
    /// 1. 颜色识别只要成功一次就认为成功并返回(优先级最高)
    /// 2. 出战标识识别成功，颜色识别失败，认为结果不确定，需要重试一次。2次后结果相同认为成功
    /// </summary>
    /// <param name="imageRegion"></param>
    /// <param name="rectArray"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    public static int GetAvatarIndexIsActiveWithContext(ImageRegion imageRegion, Rect[] rectArray, AvatarActiveCheckContext context)
    {
        var indexByColor = FindActiveIndexRectByColor(imageRegion, rectArray);
        if (indexByColor > 0)
        {
            context.TotalCheckFailedCount = 0;
            return indexByColor;
        }

        var indexByArrow = FindActiveIndexRectByArrow(imageRegion, rectArray);
        if (indexByArrow > 0)
        {
            // 累计识别次数
            context.ActiveIndexByArrowCount[indexByArrow - 1]++;
            if (context.ActiveIndexByArrowCount[indexByArrow - 1] >= 2)
            {
                context.TotalCheckFailedCount = 0;
                return indexByArrow;
            }

            return -2; // 重试
        }

        context.TotalCheckFailedCount++;
        return -1; // 两种方式都失败
    }

    // public static int FindDifferentRect(Mat greyMat, Rect[] rectArray)
    // {
    //     // 取其中一个矩形和另外三个矩形进行比较
    //     var one = new Mat(greyMat, rectArray[0]);
    //     for (int i = 1; i < rectArray.Length; i++)
    //     {
    //         Mat diff = new Mat();
    //         Cv2.Absdiff(one, new Mat(greyMat, rectArray[i]), diff);
    //         Scalar diffSum = Cv2.Sum(diff);
    //         double totalDiff = diffSum.Val0 + diffSum.Val1 + diffSum.Val2;
    //         totalDiff = totalDiff / (one.Width * one.Height);
    //     }
    //
    //     return 1;
    // }

    public static int FindActiveIndexRectByColor(ImageRegion imageRegion, Rect[] rectArray)
    {
        if (rectArray.Length == 1)
        {
            return 1;
        }

        Mat[] mats = new Mat[rectArray.Length];
        try
        {
            int whiteCount = 0, notWhiteRectNum = 0;
            var mat = imageRegion.CacheGreyMat;
            for (int i = 0; i < rectArray.Length; i++)
            {
                var indexMat = new Mat(mat, rectArray[i]);
                mats[i] = indexMat;
                if (IsWhiteRect(indexMat))
                {
                    whiteCount++;
                }
                else
                {
                    notWhiteRectNum = i + 1;
                }
            }

            if (whiteCount == rectArray.Length - 1)
            {
                return notWhiteRectNum;
            }
            else
            {
                // 方法2：边缘像素白色比例
                int m2 = FindActiveIndexRectByEdgeColor(mats);
                if (m2 > 0)
                {
                    return m2;
                }
                
                // 方法3：使用更加靠谱的差值识别（-1是未识别），但是不支持非满队
                if (mats.Length == 4)
                {
                    return ImageDifferenceDetector.FindMostDifferentImage(mats);
                }
                else
                {
                    return -1;
                }
            }
        }
        finally
        {
            foreach (var mat in mats)
            {
                mat?.Dispose();
            }
        }
    }

    public static bool IsWhiteRect(Mat indexMat)
    {
        var count1 = OpenCvCommonHelper.CountGrayMatColor(indexMat, 251, 255); // 白
        var count2 = OpenCvCommonHelper.CountGrayMatColor(indexMat, 50, 54); // 黑色文字
        if ((count1 + count2) * 1.0 / (indexMat.Width * indexMat.Height) > 0.35)
        {
            // Debug.WriteLine($"白色矩形占比{(count1 + count2) * 1.0 / (indexMat.Width * indexMat.Height)}");
            return true;
        }

        return false;
    }


    /// <summary>
    /// 使用出战标识识别出战
    /// </summary>
    /// <param name="imageRegion"></param>
    /// <param name="rectArray"></param>
    /// <returns></returns>
    public static int FindActiveIndexRectByArrow(ImageRegion imageRegion, Rect[] rectArray)
    {
        if (rectArray.Length == 1)
        {
            return 1;
        }

        var curr = imageRegion.Find(ElementAssets.Instance.CurrentAvatarThreshold); // 当前出战角色标识
        if (curr.IsEmpty())
        {
            return -1;
        }

        for (int i = 0; i < rectArray.Length; i++)
        {
            if (IsIntersecting(curr.Y, curr.Height, rectArray[i].Y, rectArray[i].Height))
            {
                return i + 1;
            }
        }

        return -1;
    }

    /// <summary>
    ///  通过边缘像素颜色识别出战角色编号
    /// </summary>
    /// <param name="mats"></param>
    /// <returns></returns>
    public static int FindActiveIndexRectByEdgeColor(Mat[] mats)
    {
        try
        {
            int whiteCount = 0, notWhiteRectNum = 0;
            for (int i = 0; i < mats.Length; i++)
            {
                if (CalculateWhiteEdgePixelsRatio(mats[i]) > 0.5)
                {
                    whiteCount++;
                }
                else
                {
                    notWhiteRectNum = i + 1;
                }
            }

            if (whiteCount == mats.Length - 1)
            {
                return notWhiteRectNum;
            }
            else
            {
                return -1;
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }

        return -1;
    }

    /// <summary>
    /// 计算灰度图最边缘一圈中纯白色(255)像素的占比
    /// </summary>
    /// <returns>返回纯白像素占比 (0.0 到 1.0)</returns>
    public static double CalculateWhiteEdgePixelsRatio(Mat image)
    {
        int whiteCount = 0;
        int height = image.Height;
        int width = image.Width;

        // 如果图片太小，无法获取边缘
        if (height < 1 || width < 1)
        {
            return 0.0;
        }

        // 计算总边缘像素数
        int totalCount = 2 * (width + height - 2);

        // 顶边和底边
        for (int x = 0; x < width; x++)
        {
            // 顶边
            if (image.At<byte>(0, x) >= 251)
            {
                whiteCount++;
            }

            // 底边（避免只有一行时重复计数）
            if (height > 1 && image.At<byte>(height - 1, x) >= 251)
            {
                whiteCount++;
            }
        }

        // 左边和右边（不包括四个角，因为已经在顶边和底边中计算过）
        for (int y = 1; y < height - 1; y++)
        {
            // 左边
            if (image.At<byte>(y, 0) >= 251)
            {
                whiteCount++;
            }

            // 右边（避免只有一列时重复计数）
            if (width > 1 && image.At<byte>(y, width - 1) >= 251)
            {
                whiteCount++;
            }
        }

        // 计算并返回占比
        return totalCount > 0 ? (double)whiteCount / totalCount : 0.0;
    }
}
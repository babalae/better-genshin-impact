using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using System;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.AutoFight.Model;

namespace BetterGenshinImpact.GameTask.Common.BgiVision;

/// <summary>
/// 视觉识别 - 技能状态相关
/// </summary>
public static partial class Bv
{
    /// <summary>
    /// 判断指定角色的技能是否可用（就绪）
    /// </summary>
    /// <param name="image">游戏截图</param>
    /// <param name="index">角色索引 (1-4)</param>
    /// <param name="isBurst">是否为爆发技能 (Q)</param>
    /// <returns>true 为就绪，false 为冷却中或不可用</returns>
    public static bool IsSkillReady(ImageRegion image, int index, bool isBurst)
    {
        if (image == null) return false;
        var avatarRects = AutoFightAssets.Instance.AvatarIndexRectList;
        if (index < 1 || index > avatarRects.Count) return false;

        // 1. 判断是否为当前活跃角色
        bool isActive = IsCharacterActive(image, index);

        if (isActive)
        {
            // 2. 当前活跃角色：识别底部中央 UI 技能图标状态
            // 通过检测图标区域是否存在白色连通域（数字或蒙版文字）来判断
            var skillArea = isBurst
                ? new Rect(image.Width * 1809 / 1920, image.Height * 968 / 1080, image.Width * 30 / 1920, image.Height * 15 / 1080)
                : new Rect(image.Width * 1688 / 1920, image.Height * 988 / 1080, image.Width * 22 / 1920, image.Height * 12 / 1080);

            // 执行二值化处理
            using var mask = OpenCvCommonHelper.Threshold(image.DeriveCrop(skillArea).SrcMat, new Scalar(255, 255, 255));
            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();
            
            // 计算连通域数量，若数量过大则说明存在冷却数字或锁定标志
            int numLabels = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids);

            return numLabels <= 2;
        }
        else
        {
            // 3. 后台角色：仅能检测爆发技能 (Q) 能量是否充满
            if (isBurst)
            {
                // 通过侧边栏图标的 Hough 圆变换识别明亮的高能环
                var qRects = AutoFightAssets.Instance.AvatarQRectListMap;
                if (index > qRects.Count) return false;
                var qArea = qRects[index - 1];
                using var grayImage = image.DeriveCrop(qArea).SrcMat.CvtColor(ColorConversionCodes.BGR2GRAY);

                var meanBrightness = Cv2.Mean(grayImage).Val0;
                using var canny = new Mat();
                Cv2.Canny(grayImage, canny, meanBrightness * 0.9, meanBrightness * 2.0);

                // 霍夫圆变换检测
                var circles = Cv2.HoughCircles(canny, HoughModes.Gradient, 1.2, 20, 90, 25, 25, 34);
                return circles.Length > 0;
            }
            
            return false;
        }
    }

    /// <summary>
    /// 判断指定序号的角色是否为当前活跃角色的高光状态
    /// </summary>
    /// <param name="image">游戏截图</param>
    /// <param name="index">角色索引 (1-4)</param>
    /// <returns>是否活跃</returns>
    private static readonly AvatarActiveCheckContext _avatarActiveCheckContext = new();

    public static bool IsCharacterActive(ImageRegion image, int index)
    {
        var rectList = AutoFightAssets.Instance.AvatarIndexRectList;
        if (index < 1 || index > rectList.Count) return false;

        // 使用 PartyAvatarSideIndexHelper 的综合判断逻辑（包含颜色对比、箭头检测等）
        int activeIdx = PartyAvatarSideIndexHelper.GetAvatarIndexIsActiveWithContext(image, rectList.ToArray(), _avatarActiveCheckContext);
        return activeIdx == index;
    }
    
}

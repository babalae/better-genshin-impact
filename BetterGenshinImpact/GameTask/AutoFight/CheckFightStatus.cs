using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

using static BetterGenshinImpact.GameTask.Common.TaskControl;
using TorchSharp.Modules;

namespace GameTask.AutoFight
{

    public class CheckFightFinished
    {
        public static RecognitionObject ChatEnterIconRa = null;


        /// <summary>
        ///   初始化 enter 图标 每次自动战斗启动时进行初始化
        ///   寻找 chatIcon 再根据其位置寻找当前的 enter 键
        /// </summary>
        /// <returns></returns>
        public static async Task<Boolean> initChatButton(ImageRegion ra)
        {
            double assetScale = TaskContext.Instance().SystemInfo.AssetScale;
            Region chatIcon = ra.Find(AutoFightAssets.Instance.ChatIconRa);
            Mat scrMap = ra.SrcMat;
            Mat enterIconMat = null;
            if (chatIcon.IsExist())
            {

                var x = chatIcon.Left;
                var y = chatIcon.Top + chatIcon.Height / 2;
                // 由于 enterIcon 的像素值会变化 设置基准像素 chatIcon 的右上 像素为基准
                Vec3b basePixel = scrMap.At<Vec3b>(y, x); // BGR 格式
                                                          // 定义颜色容忍范围
                int tolerance = 5;
                const int maxSearchWidth = 200; // 最大向右搜索范围

                enterIconMat = await findEnterIcon(scrMap, x, y, basePixel, tolerance, maxSearchWidth, chatIcon.Height);

            }
            else
            {
                // 若 chatIcon 不存在，则 enterIcon 的白色像素值为强制设为 (255,255,255)
                // 我需要在 x = 40    1025 < y < 1050 范围内寻找
                int x = (int)(40 * assetScale);
                int y = (int)(1025 * assetScale);
                Vec3b basePixel = new Vec3b
                {
                    Item0 = 255, // B
                    Item1 = 255, // G
                    Item2 = 255  // R
                };
                int tolerance = 5;
                const int maxSearchWidth = 50; // 最大向右搜索范围
                for (int i = 0; i < 50; i += 5)
                {
                    int currentY = y + i;
                    enterIconMat = await findEnterIcon(scrMap, x, currentY, basePixel, tolerance, maxSearchWidth);
                    if (enterIconMat != null) break;
                }
            }

            if (enterIconMat != null)
            {
                // 寻找到了 enter图标 初始化识别区
                ChatEnterIconRa = new RecognitionObject
                {
                    Name = "ChatEnterIcon",
                    RecognitionType = RecognitionTypes.TemplateMatch,
                    TemplateImageMat = enterIconMat,
                    RegionOfInterest = new Rect(0, ra.Height - (int)(100 * assetScale), (int)(180 * assetScale), (int)(100 * assetScale)),
                    DrawOnWindow = false,
                    Use3Channels = true
                }.InitTemplate();
                return true;
            }
            return false;
        }

        /// <summary>
        /// 从给定 像素位置向右遍历 maxSearchWidth 个像素 在其中找到 basePixel 表示 enterIcon 的左边界
        /// 以此上下左右寻找 enterIcon 图标
        /// </summary>
        /// <param name="scrMap"></param>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="basePixel"></param>
        /// <param name="tolerance"></param>
        /// <param name="maxSearchWidth"></param>
        /// <param name="Height"></param>
        /// <returns></returns>
        private static async Task<Mat> findEnterIcon(Mat scrMap, int x, int y, Vec3b basePixel, int tolerance, int maxSearchWidth, int Height = 0)
        {

            // 1. 寻找左边界（第一个匹配基准像素的点）
            int leftX = -1;
            for (int i = 1; i <= maxSearchWidth; i++)
            {
                int currentX = x + i;
                if (currentX >= scrMap.Width) break;

                Vec3b currentPixel = scrMap.At<Vec3b>(y, currentX);
                if (IsPixelMatch(currentPixel, basePixel, tolerance))
                {
                    leftX = currentX;
                    break;
                }
            }
            if (leftX == -1) return null; // 没找到左边界
                                          // 2. 向上、向下延伸，确定左边界（Y方向）
            int topY = y;
            int bottomY = y;

            // 向上搜索左边界（Y减小）
            while (topY > 0)
            {
                Vec3b pixel = scrMap.At<Vec3b>(topY - 1, leftX);
                if (!IsPixelMatch(pixel, basePixel, tolerance)) break;
                topY--;
            }

            // 向下搜索左边界（Y增大）
            while (bottomY < scrMap.Height - 1)
            {
                Vec3b pixel = scrMap.At<Vec3b>(bottomY + 1, leftX);
                if (!IsPixelMatch(pixel, basePixel, tolerance)) break;
                bottomY++;
            }

            // 校验 enterIcon 和 chatIcon 高度是否匹配
            if (Height != 0 && Math.Abs((bottomY - topY) - Height) > (Height / 2))
            {
                return null;
            }

            // 3. 从左边界下端（bottomY, leftX）向右寻找右边界
            int rightX = leftX;
            while (rightX < scrMap.Width - 1)
            {
                Vec3b pixel = scrMap.At<Vec3b>(bottomY, rightX + 1);
                if (!IsPixelMatch(pixel, basePixel, tolerance)) break;
                rightX++;
            }

            // 截取 enter 图标的 Mat 区域
            Rect enterIconRect = Rect.FromLTRB(leftX, topY, rightX, bottomY);
            Mat enterIconMat = new Mat(scrMap, enterIconRect);
            return enterIconMat;
        }

        // 辅助方法：判断两个像素是否匹配（在容忍范围内）
        public static bool IsPixelMatch(Vec3b pixel1, Vec3b pixel2, int tolerance)
        {
            return Math.Abs(pixel1.Item0 - pixel2.Item0) <= tolerance && // B
                   Math.Abs(pixel1.Item1 - pixel2.Item1) <= tolerance && // G
                   Math.Abs(pixel1.Item2 - pixel2.Item2) <= tolerance;   // R
        }


        /// <summary>
        ///  通过切换队伍 检查是否自动战斗 V2 版本
        /// </summary>
        /// <returns></returns>
        public static async Task<bool> CheckFightFinishByChangeGroup(ImageRegion ra, CancellationToken ct)
        {

            int detectDelayTime = 15;
            Simulation.SendInput.SimulateAction(GIActions.OpenPartySetupScreen);
            await Delay(detectDelayTime, ct);
            // 判断mainUI是否存在 存在则未非战斗状态
            bool FightFinished = false;
            if (ra.Find(ChatEnterIconRa).IsExist())
            {
                FightFinished = !Bv.IsInMainUi(ra);
                if (FightFinished)
                {
                    // 取消切换队伍操作
                    Simulation.SendInput.SimulateAction(GIActions.MoveForward);
                }
            }
            return FightFinished;

        }
    }
}

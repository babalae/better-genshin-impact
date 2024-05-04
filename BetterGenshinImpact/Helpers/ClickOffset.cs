// using BetterGenshinImpact.GameTask;
// using BetterGenshinImpact.Helpers.Extensions;
// using System;
//
// namespace BetterGenshinImpact.Helpers;
//
// /// <summary>
// /// 不推荐使用
// /// 请使用 GameCaptureRegion.GameRegionClick 或 GameCaptureRegion.GameRegion1080PPosClick 替代
// /// </summary>
// [Obsolete]
// public class ClickOffset
// {
//     public int OffsetX { get; set; }
//     public int OffsetY { get; set; }
//     public double AssetScale { get; set; }
//
//     // public double CaptureAreaScale { get; set; }
//
//     public ClickOffset()
//     {
//         if (!TaskContext.Instance().IsInitialized)
//         {
//             throw new Exception("请先启动");
//         }
//         var captureArea = TaskContext.Instance().SystemInfo.CaptureAreaRect;
//         var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
//         OffsetX = captureArea.X;
//         OffsetY = captureArea.Y;
//         AssetScale = assetScale;
//         // CaptureAreaScale = TaskContext.Instance().SystemInfo.CaptureAreaScale;
//     }
//
//     public ClickOffset(int offsetX, int offsetY, double assetScale)
//     {
//         if (!TaskContext.Instance().IsInitialized)
//         {
//             throw new Exception("请先启动");
//         }
//         // CaptureAreaScale = TaskContext.Instance().SystemInfo.CaptureAreaScale;
//
//         OffsetX = offsetX;
//         OffsetY = offsetY;
//         AssetScale = assetScale;
//     }
//
//     public void Click(int x, int y)
//     {
//         ClickExtension.Click(OffsetX + (int)(x * AssetScale), OffsetY + (int)(y * AssetScale));
//     }
//
//     /// <summary>
//     /// 输入的x,y 注意处理缩放
//     /// </summary>
//     /// <param name="x"></param>
//     /// <param name="y"></param>
//     public void ClickWithoutScale(int x, int y)
//     {
//         ClickExtension.Click(OffsetX + x, OffsetY + y);
//     }
//
//     public void Move(int x, int y)
//     {
//         ClickExtension.Move(OffsetX + (int)(x * AssetScale), OffsetY + (int)(y * AssetScale));
//     }
// }

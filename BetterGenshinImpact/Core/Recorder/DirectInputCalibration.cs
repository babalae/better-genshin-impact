// using System;
// using System.Diagnostics;
// using System.Threading.Tasks;
// using BetterGenshinImpact.Core.Monitor;
// using BetterGenshinImpact.Core.Simulator;
// using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
// using BetterGenshinImpact.GameTask;
// using BetterGenshinImpact.GameTask.Common.Element.Assets;
// using BetterGenshinImpact.GameTask.Common.Map;
// using BetterGenshinImpact.GameTask.Model.Area;
// 
// using BetterGenshinImpact.View.Drawable;
// using BetterGenshinImpact.ViewModel.Pages;
// using Microsoft.Extensions.Logging;
// using OpenCvSharp;
// using Vanara.PInvoke;
// using static BetterGenshinImpact.GameTask.Common.TaskControl;
//
// namespace BetterGenshinImpact.Core.Recorder;
//
// /// <summary>
// /// DirectInput、鼠标移动距离、视角度数之间的校准
// /// </summary>
// [Obsolete]
// public class DirectInputCalibration
// {
//     // 视角偏移移动单位
//     private const int CharMovingUnit = 500;
//
//     public async void Start()
//     {
//         var hasLock = false;
//         try
//         {
//             hasLock = await TaskSemaphore.WaitAsync(0);
//             if (!hasLock)
//             {
//                 Logger.LogError("启动视角校准功能失败：当前存在正在运行中的独立任务，请不要重复执行任务！");
//                 return;
//             }
//
//             Init();
//
//             await Task.Run(() =>
//             {
//                 GetOffsetAngle();
//             });
//         }
//         catch (Exception e)
//         {
//             Logger.LogError(e.Message);
//             Logger.LogDebug(e.StackTrace);
//         }
//         finally
//         {
//             VisionContext.Instance().DrawContent.ClearAll();
//             TaskTriggerDispatcher.Instance().SetCacheCaptureMode(DispatcherCaptureModeEnum.NormalTrigger);
//             TaskSettingsPageViewModel.SetSwitchAutoFightButtonText(false);
//             Logger.LogInformation("→ {Text}", "视角校准结束");
//
//             if (hasLock)
//             {
//                 TaskSemaphore.Release();
//             }
//         }
//     }
//
//     private void Init()
//     {
//         SystemControl.ActivateWindow();
//         Logger.LogInformation("→ {Text}", "视角校准，启动！");
//         TaskTriggerDispatcher.Instance().SetCacheCaptureMode(DispatcherCaptureModeEnum.OnlyCacheCapture);
//         Sleep(TaskContext.Instance().Config.TriggerInterval * 5); // 等待缓存图像
//     }
//
//     public int GetOffsetAngle()
//     {
//         var directInputMonitor = new DirectInputMonitor();
//         var ms1 = directInputMonitor.GetMouseState();
//         Logger.LogInformation("当前鼠标状态：{X} {Y}", ms1.X, ms1.Y);
//         var angle1 = GetCharacterOrientationAngle();
//         Simulation.SendInput.Mouse.MoveMouseBy(CharMovingUnit, 0);
//         Sleep(500);
//
//         Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_W).Sleep(100).KeyUp(User32.VK.VK_W);
//         Sleep(1000);
//
//         var ms2 = directInputMonitor.GetMouseState();
//         Logger.LogInformation("当前鼠标状态：{X} {Y}", ms2.X, ms2.Y);
//         var angle2 = GetCharacterOrientationAngle();
//         var angleOffset = angle2 - angle1;
//         var directInputXOffset = ms2.X - ms1.X;
//         Logger.LogInformation("横向移动偏移量校准：鼠标平移{CharMovingUnit}单位，角度转动{AngleOffset}，DirectInput移动{DirectInputXOffset}",
//             CharMovingUnit, angleOffset, directInputXOffset);
//
//         var angle2MouseMoveByX = CharMovingUnit * 1d / angleOffset;
//         var angle2DirectInputX = directInputXOffset * 1d / angleOffset;
//         Logger.LogInformation("校准结果：视角每移动1度，需要MouseMoveBy的距离{Angle2MouseMoveByX}，需要DirectInput移动的单位{Angle2DirectInputX}",
//                        angle2MouseMoveByX, angle2DirectInputX);
//
//         return angleOffset;
//     }
//
//     public Mat? GetMiniMapMat(ImageRegion ra)
//     {
//         var paimon = ra.Find(ElementAssets.Instance.PaimonMenuRo);
//         if (paimon.IsExist())
//         {
//             return new Mat(ra.SrcMat, new Rect(paimon.X + 24, paimon.Y - 15, 210, 210));
//         }
//
//         return null;
//     }
//
//     public int GetCharacterOrientationAngle()
//     {
//         var ra = GetRectAreaFromDispatcher();
//         var miniMapMat = GetMiniMapMat(ra);
//         if (miniMapMat == null)
//         {
//             throw new InvalidOperationException("当前不在主界面");
//         }
//
//         var angle = CharacterOrientation.Compute(miniMapMat);
//         Logger.LogInformation("当前角度：{Angle}", angle);
//         // CameraOrientation.DrawDirection(ra, angle);
//         return angle;
//     }
// }

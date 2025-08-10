using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using OpenCvSharp;
using BetterGenshinImpact.Core.Recognition.OpenCv;

namespace BetterGenshinImpact.GameTask.AutoFight
{
    public static  class MoveForwardTask
    {

        public static async Task ExecuteAsync(Scalar scalarLower, Scalar scalarHigher, ILogger logger, CancellationToken ct)
        {
            await MoveForwardAsync(scalarLower, scalarHigher, logger, ct);
        }

        public static Task<bool?> MoveForwardAsync(Scalar scalarLower, Scalar scalarHigher, ILogger logger, CancellationToken ct)
        {
            var image2 = CaptureToRectArea();
            Mat mask2 = OpenCvCommonHelper.Threshold(
                image2.DeriveCrop(0, 0, image2.Width * 1570 / 1920, image2.Height * 970 / 1080).SrcMat,
                scalarLower,
                scalarHigher
            );

            Mat labels2 = new Mat();
            Mat stats2 = new Mat();
            Mat centroids2 = new Mat();

            int numLabels2 = Cv2.ConnectedComponentsWithStats(mask2, labels2, stats2, centroids2, connectivity: PixelConnectivity.Connectivity4, ltype: MatType.CV_32S);

            logger.LogInformation("检测数量：{numLabels2}", numLabels2 - 1);

            if (numLabels2 > 1)
            {
                // 获取第一个连通对象的统计信息（标签1）
                Mat firstRow = stats2.Row(1); // 获取第1行（标签1）的数据
                int[] stats;
                bool success = firstRow.GetArray(out stats); // 使用 out 参数来接收数组数据

                if (success)
                {
                    int x = stats[0];
                    int y = stats[1];
                    // int width = stats[2];
                    int height = stats[3];

                    Point firstPixel = new Point(x, y);
                    logger.LogInformation("敌人位置: ({firstPixel.X}, {firstPixel.Y})，血量高度: {height}", firstPixel.X, firstPixel.Y, height);
                    
                    if (firstPixel.X < 580 || firstPixel.X > 1315 || firstPixel.Y > 800)
                    {
                        // 非中心区域的处理逻辑
                        if (firstPixel.X < 500 && firstPixel.Y < 800)
                        {
                            // 左上区域
                            if (height <= 6)
                            {
                                logger.LogInformation("敌人在左上，向前加向左移动");
                                Task.Run(() =>
                                {
                                    Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                                    Simulation.SendInput.SimulateAction(GIActions.MoveLeft, KeyType.KeyDown);
                                    Task.Delay(1000, ct).Wait();
                                    Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                                    Simulation.SendInput.SimulateAction(GIActions.MoveLeft, KeyType.KeyUp);
                                }, ct);
                            }
                        }
                        else if (firstPixel.X > 1315 && firstPixel.Y < 800)
                        {
                            // 右上区域
                            if (height <= 6)
                            {
                                logger.LogInformation("敌人在右上，向前加向右移动");
                                Task.Run(() =>
                                {
                                    Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                                    Simulation.SendInput.SimulateAction(GIActions.MoveRight, KeyType.KeyDown);
                                    Task.Delay(1000, ct).Wait();
                                    Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                                    Simulation.SendInput.SimulateAction(GIActions.MoveRight, KeyType.KeyUp);
                                }, ct);
                            }
                        }
                        else if (firstPixel.X < 500 && firstPixel.Y > 800)
                        {
                            // 左下区域
                            if (height <= 6)
                            {
                                logger.LogInformation("敌人在左下，向后加向左移动");
                                Task.Run(() =>
                                {
                                    Simulation.SendInput.SimulateAction(GIActions.MoveBackward, KeyType.KeyDown);
                                    Simulation.SendInput.SimulateAction(GIActions.MoveLeft, KeyType.KeyDown);
                                    Task.Delay(1000, ct).Wait();
                                    Simulation.SendInput.SimulateAction(GIActions.MoveBackward, KeyType.KeyUp);
                                    Simulation.SendInput.SimulateAction(GIActions.MoveLeft, KeyType.KeyUp);
                                }, ct);
                            }
                        }
                        else if (firstPixel.X > 1315 && firstPixel.Y > 800)
                        {
                            // 右下区域
                            if (height <= 6)
                            {
                                logger.LogInformation("敌人在右下，向后加向右移动");
                                Task.Run(() =>
                                {
                                    Simulation.SendInput.SimulateAction(GIActions.MoveBackward, KeyType.KeyDown);
                                    Simulation.SendInput.SimulateAction(GIActions.MoveRight, KeyType.KeyDown);
                                    Task.Delay(1000, ct).Wait();
                                    Simulation.SendInput.SimulateAction(GIActions.MoveBackward, KeyType.KeyUp);
                                    Simulation.SendInput.SimulateAction(GIActions.MoveRight, KeyType.KeyUp);
                                }, ct);
                            }
                        }
                        else if (firstPixel.Y < 800)
                        {
                            // 上方区域
                            if (height <= 6)
                            {
                                logger.LogInformation("敌人在上方，向前移动");
                                Task.Run(() =>
                                {
                                    Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                                    Task.Delay(1000, ct).Wait();
                                    Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                                }, ct);
                            }
                        }
                        else if (firstPixel.Y > 800)
                        {
                            // 下方区域
                            if (height <= 6)
                            {
                                logger.LogInformation("敌人在下方，向后移动");
                                Task.Run(() =>
                                {
                                    Simulation.SendInput.SimulateAction(GIActions.MoveBackward, KeyType.KeyDown);
                                    Task.Delay(1000, ct).Wait();
                                    Simulation.SendInput.SimulateAction(GIActions.MoveBackward, KeyType.KeyUp);
                                }, ct);
                            }
                        }
                        else
                        {
                            // 非上述区域且非中心区域，判断左右
                            if (firstPixel.X < 920 && height > 6)
                            {
                                logger.LogInformation("敌人在左侧，不移动");
                            }
                            else if (firstPixel.X > 920 && height > 6)
                            {
                                logger.LogInformation("敌人在右侧，不移动");
                            }
                        }
                    }
                    else // 中心区域
                    {
                        if (height > 6)
                        {
                            logger.LogInformation("敌人在中心且高度大于6，不移动");
                        }
                        else if (firstPixel.X < 1315 && firstPixel.X > 500 && firstPixel.Y < 800 && height > 2)
                        {
                            logger.LogInformation("敌人在上方，向前移动");
                            Task.Run(() =>
                            {
                                Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                                Task.Delay(1000, ct).Wait();
                                Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                            }, ct);
                        }
                        else if (firstPixel.X < 1315 && firstPixel.X > 500 && firstPixel.Y > 800 && height > 2)
                        {
                            logger.LogInformation("敌人在下方，向后移动");
                            Task.Run(() =>
                            {
                                Simulation.SendInput.SimulateAction(GIActions.MoveBackward, KeyType.KeyDown);
                                Task.Delay(1000, ct).Wait();
                                Simulation.SendInput.SimulateAction(GIActions.MoveBackward, KeyType.KeyUp);
                            }, ct);
                        }
                        else if (height < 3)
                        {
                            logger.LogInformation("敌人血量高度小于3，不移动");
                        }
                        else
                        {
                            logger.LogInformation("不移动");
                        }
                    }
                }
                else
                {
                    logger.LogError("无法获取统计信息数组");
                }
            }

            mask2.Dispose();
            labels2.Dispose();
            return Task.FromResult<bool?>(null);
        }
    }

    public  class AutoFightSeek
    {
        public static async Task<bool?> SeekAndFightAsync(ILogger logger, int detectDelayTime,int delayTime, CancellationToken ct)
        {
            Scalar bloodLower = new Scalar(255, 90, 90);
            int retryCount = 0;

            while (retryCount < 10)
            {
                var image = CaptureToRectArea();
                Mat mask = OpenCvCommonHelper.Threshold(image.DeriveCrop(0, 0, 1500, 900).SrcMat, bloodLower);
                Mat labels = new Mat();
                Mat stats = new Mat();
                Mat centroids = new Mat();

                int numLabels = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids,
                    connectivity: PixelConnectivity.Connectivity4, ltype: MatType.CV_32S);
                if (retryCount == 0) logger.LogInformation("敌人初检数量： {numLabels}", numLabels - 1);

                if (numLabels > 1)
                {
                    logger.LogInformation("检测画面内疑似有敌人，继续战斗...");
                    // 获取第一个连通对象的统计信息（标签1）
                    Mat firstRow = stats.Row(1); // 获取第1行（标签1）的数据
                    int[] statsArray;
                    bool success = firstRow.GetArray(out statsArray); // 使用 out 参数来接收数组数据
                    int height = statsArray[3];
                    logger.LogInformation("敌人血量高度：{height}", height);
                    
                    mask.Dispose();
                    labels.Dispose();
                    stats.Dispose();
                    centroids.Dispose();
                    
                    // //如果不用旋转判断敌人，直接跳过开队伍检测，加快战斗速度
                    //  return height > 2;//大于2预防误判
                    
                    if (success && height > 2)
                    {
                        if (height < 7)
                        {
                            logger.LogInformation("敌人血量高度小于7且大于2，向前移动");
                            Task.Run(() =>
                            {
                                MoveForwardTask.MoveForwardAsync(bloodLower, bloodLower, logger, ct);
                                Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                                Task.Delay(1000, ct).Wait();
                                Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                            }, ct);
                        }
                        return false;
                    }
                    if (height < 3) return null;
                }
                //如果不用旋转判断敌人，直接跳过开队伍检测，加快战斗速度
                // else
                // {
                //     logger.LogInformation("首次检测画面内没有怪物...");
                //     return true;
                // }
                if (retryCount == 0)
                {
                    await Delay(delayTime,ct);
                    Logger.LogInformation("打开编队界面检查战斗是否结束，延时{detectDelayTime}毫秒检查", detectDelayTime);
                    Simulation.SendInput.SimulateAction(GIActions.OpenPartySetupScreen);
                    await Delay(detectDelayTime, ct);
                    var ra3 = CaptureToRectArea();
                    var b33 = ra3.SrcMat.At<Vec3b>(50, 790); // 进度条颜色
                    var whiteTile3 = ra3.SrcMat.At<Vec3b>(50, 768); // 白块
                    Simulation.SendInput.SimulateAction(GIActions.Drop);

                    if (IsWhite(whiteTile3.Item2, whiteTile3.Item1, whiteTile3.Item0) &&
                        IsYellow(b33.Item2, b33.Item1, b33.Item0))
                    {
                        logger.LogInformation("识别到战斗结束");
                        Simulation.SendInput.SimulateAction(GIActions.OpenPartySetupScreen);
                        return true;
                    }
                }
                
                // logger.LogInformation("画面内没有怪物，旋转寻找...");
                if (retryCount <= 1)
                {
                    Simulation.SendInput.Mouse.MoveMouseBy(image.Width / 2, -image.Height / 3);
                }
                else
                {
                    Simulation.SendInput.Mouse.MoveMouseBy(image.Width / 2, 0);
                }

                await Task.Delay(250,ct);

                image = CaptureToRectArea();
                mask = OpenCvCommonHelper.Threshold(image.DeriveCrop(0, 0, 1500, 900).SrcMat, bloodLower);
                labels = new Mat();
                stats = new Mat();
                centroids = new Mat();

                 numLabels = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids,
                    connectivity: PixelConnectivity.Connectivity4, ltype: MatType.CV_32S);
                // if (retryCount % 2 == 0) logger.LogInformation("检测敌人第 {retryCount} 次： {numLabels}", retryCount + 1, numLabels - 1);

                if (numLabels > 1)
                {
                    logger.LogInformation("检测敌人第 {retryCount} 次： {numLabels}", retryCount + 1, numLabels - 1);
                    Mat firstRow2 = stats.Row(1); // 获取第1行（标签1）的数据
                    int[] statsArray2;
                    bool success2 = firstRow2.GetArray(out statsArray2); // 使用 out 参数来接收数组数据
                    int height2 = statsArray2[3];
                    logger.LogInformation("敌人血量高度：{height2}", height2);
                    
                    mask.Dispose();
                    labels.Dispose();
                    stats.Dispose();
                    centroids.Dispose();
                    
                    if (success2 && height2 > 2)
                    {
                        if (height2 < 7)
                        {
                            logger.LogInformation("画面内有找到敌人，继续战斗...");
                            Task.Run(() => { MoveForwardTask.MoveForwardAsync(bloodLower, bloodLower, logger, ct); }, ct);  
                        }
                        return false; 
                    }

                    if (height2 < 3) return null;

                }
               
                // logger.LogInformation("画面内没有怪物，尝试重新检测...");
                retryCount++;
            }
            logger.LogInformation("寻找敌人：{Text}", "无");
            return null;
        }
        
        private static bool IsYellow(int r, int g, int b)
        {
            //Logger.LogInformation($"IsYellow({r},{g},{b})");
            // 黄色范围：R高，G高，B低
            return (r >= 200 && r <= 255) &&
                   (g >= 200 && g <= 255) &&
                   (b >= 0 && b <= 100);
        }

        private static bool IsWhite(int r, int g, int b)
        {
            //Logger.LogInformation($"IsWhite({r},{g},{b})");
            // 白色范围：R高，G高，B低
            return (r >= 240 && r <= 255) &&
                   (g >= 240 && g <= 255) &&
                   (b >= 240 && b <= 255);
        }
    }

}

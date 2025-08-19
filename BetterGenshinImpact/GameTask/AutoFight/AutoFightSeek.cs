using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using OpenCvSharp;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoFight.Script;

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

    public class AutoFightSeek
    {
        public static int RotationCount = 0;
        
        public static async Task<bool?> SeekAndFightAsync(ILogger logger, int detectDelayTime,int delayTime,CancellationToken ct)
        {
            Scalar bloodLower = new Scalar(255, 90, 90);
            int retryCount = 0;

            while (retryCount < 27)
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

                    Mat firstRow = stats.Row(1);
                    int[] statsArray;
                    bool success = firstRow.GetArray(out statsArray); 
                    int height = statsArray[3];
                    logger.LogInformation("敌人血量高度：{height}", height);
                    
                    mask.Dispose();
                    labels.Dispose();
                    stats.Dispose();
                    centroids.Dispose();
                    
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

                if (RotationCount == 3 && retryCount == 0)
                {
                    Simulation.SendInput.Mouse.MiddleButtonClick();
                    await Task.Delay(500, ct);
                }
                
                if (retryCount <= 2)
                {
                   var offsets = new (int x, int y)[] {
                        (image.Width / 6, -image.Height / 5), 
                        (image.Width / 6, 0),                 
                        (image.Width / 6, image.Height / 6) 
                    };

                    var offsetIndex = RotationCount < 3 ? 0 : (RotationCount == 3) ? 1 : 2;
                    Simulation.SendInput.Mouse.MoveMouseBy(offsets[offsetIndex].x, offsets[offsetIndex].y);
                }
                else
                {
                    Simulation.SendInput.Mouse.MoveMouseBy(image.Width / 6, 0);
                }

                await Task.Delay(50,ct);

                image = CaptureToRectArea();
                mask = OpenCvCommonHelper.Threshold(image.DeriveCrop(0, 0, 1500, 900).SrcMat, bloodLower);
                labels = new Mat();
                stats = new Mat();
                centroids = new Mat();

                 numLabels = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids,
                    connectivity: PixelConnectivity.Connectivity4, ltype: MatType.CV_32S);

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

    public class AutoFightSkill
    {
        public static async Task EnsureGuardianSkill(Avatar guardianAvatar, CombatCommand command,string lastFightName,
            string guardianAvatarName,bool guardianAvatarHold,int retryCount,CancellationToken ct)
        {
            int attempt = 0;
            
            if (guardianAvatar.IsSkillReady())
            {
                while (attempt < retryCount)
                {
                    if (guardianAvatar.TrySwitch(15,false))
                    {
                        Simulation.ReleaseAllKey();
                        
                        await Task.Delay(100, ct);
                        
                        guardianAvatar.ManualSkillCd = -1;
                        var cd1 = guardianAvatar.AfterUseSkill();
                        if (cd1 > 0 )
                        {
                            Logger.LogInformation("优先第 {text} 盾奶位 {GuardianAvatar} 战技Cd检测：{cd} 秒", guardianAvatarName, guardianAvatar.Name, cd1);
                            guardianAvatar.ManualSkillCd = -1;
                            return;
                        }
                        
                        guardianAvatar.UseSkill(guardianAvatarHold);
                        Simulation.ReleaseAllKey();
                        await Task.Delay(200, ct);
                        
                        var cd2 = guardianAvatar.AfterUseSkill();
                        if ( cd2 > 0 && guardianAvatar.TrySwitch(4,false))
                        {
                            Logger.LogInformation("优先第 {text} 盾奶位 {GuardianAvatar} 释放战技成功，cd:{cd2} 秒", guardianAvatarName, guardianAvatar.Name, cd2);
                            guardianAvatar.ManualSkillCd = -1;
                            return;
                        }
                        
                        //新方法法：色块识别，带角色切换确认，不管OCR结果。避免OCR技能CD错误
                        // if (await AvatarSkillAsync(Logger, guardianAvatar,false, 5, ct))
                        // {
                        //     guardianAvatar.ManualSkillCd = -1;
                        //     return;
                        // }
                        
                        Logger.LogInformation("优先第 {text} 盾奶位 {GuardianAvatar} 释放战技：失败重试 {attempt} 次", guardianAvatarName, guardianAvatar.Name , attempt+1);
                        guardianAvatar.ManualSkillCd = 0;
                    }
                    attempt++;
                }
            } 
        }

        //新方法，备用，非OCR识别，判断色块进行，速度更快
        //检测技能图标中释放含有白色色块，检测前进行角色切换的确认，skills：false为E技能，true为Q技能（未开发）
        public static async Task<bool> AvatarSkillAsync(ILogger logger, Avatar guardianAvatar, bool skills , int retryCount, CancellationToken ct)
        {
            if (guardianAvatar.TrySwitch())
            {
                Scalar bloodLower = new Scalar(255, 255, 255);
                int attempt = 0;

                while (attempt < retryCount)
                {
                    var image2 = CaptureToRectArea();

                    var skillAra = skills
                        ? new Rect(image2.Width * 1700 / 1920, image2.Height * 996 / 1080,
                            image2.Width * 12 / 1920, image2.Height * 7 / 1080) //E技能区域
                        
                        : new Rect(image2.Width * 1819 / 1920, image2.Height * 977 / 1080,
                            image2.Width * 13 / 1920, image2.Height * 6 / 1080); //Q技能区域
                    
                    var mask2 = OpenCvCommonHelper.Threshold(
                        image2.DeriveCrop(skillAra).SrcMat,
                        bloodLower,
                        bloodLower
                    );

                    var labels2 = new Mat();
                    var stats2 = new Mat();
                    var centroids2 = new Mat();

                    int numLabels2 = Cv2.ConnectedComponentsWithStats(mask2, labels2, stats2, centroids2,
                        connectivity: PixelConnectivity.Connectivity4, ltype: MatType.CV_32S);

                    logger.LogInformation("盾奶位 {guardianAvatar.Name} 战技状态：{text}", guardianAvatar.Name , numLabels2 > 1 ? "已释放" : "未释放");

                    if (numLabels2 > 1)
                    {
                        return true;
                    }

                    attempt++;
                    await Task.Delay(100, ct);
                }
            }
            guardianAvatar.AfterUseSkill();
            return false;
        }
    }

}

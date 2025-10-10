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
using System;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using System.Linq;
using System.Collections.Generic;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using  OpenCvSharp;
using BetterGenshinImpact.GameTask.Model.Area;

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

            // logger.LogInformation("检测数量：{numLabels2}", numLabels2 - 1);

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
                                Simulation.SendInput.SimulateAction(GIActions.MoveBackward);
                                logger.LogInformation("敌人在左侧，不移动");
                            }
                            else if (firstPixel.X > 920 && height > 6)
                            {
                                Simulation.SendInput.SimulateAction(GIActions.MoveBackward);
                                logger.LogInformation("敌人在右侧，不移动");
                            }
                        }
                    }
                    else // 中心区域
                    {
                        if (height > 6)
                        {
                            Simulation.SendInput.SimulateAction(GIActions.MoveBackward);
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
                            Simulation.SendInput.SimulateAction(GIActions.MoveBackward);
                            logger.LogInformation("敌人血量高度小于3，不移动");
                        }
                        else
                        {
                            Simulation.SendInput.SimulateAction(GIActions.MoveBackward);
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
            stats2.Dispose();
            centroids2.Dispose();
            image2.Dispose();
            return Task.FromResult<bool?>(null);
        }
    }

    public class AutoFightSeek
    {
        public static int RotationCount = 0;
        
        private static readonly Dictionary<int, int> RotaryFactorMapping = new Dictionary<int, int> //旋转因子映射表
        {
            { 1, 100 }, { 2, 90 }, { 3, 80}, { 4, 70 }, { 5, 60}, { 6,45 },
            { 7, 30 }, { 8, 15 }, { 9, 6 }, { 10, 1 }, { 11,-10 }, { 12,-50 }, { 13, -60 }
        };
        
        public static async Task<bool?> SeekAndFightAsync(ILogger logger, int detectDelayTime,int delayTime,CancellationToken ct,bool isEndCheck = false,int rotaryFactor = 6)
        {
            Scalar bloodLower = new Scalar(255, 90, 90);
            
            var adjustedX = RotaryFactorMapping[rotaryFactor];
            var adjustedDivisor = rotaryFactor<=12 ? 2 : 1.3;
            
            // Logger.LogInformation("开始寻找敌人 {Text} ...",adjustedX);
            
            int retryCount = isEndCheck? 1 : 0;

            while (retryCount < 25+(int)(adjustedX / 5))
            {
                var image = CaptureToRectArea();
                Mat mask = OpenCvCommonHelper.Threshold(image.DeriveCrop(0, 0, 1500, 900).SrcMat, bloodLower);
                
                Mat labels = new Mat();
                Mat stats = new Mat();
                Mat centroids = new Mat();

                int numLabels = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids,
                    connectivity: PixelConnectivity.Connectivity4, ltype: MatType.CV_32S);
                // if (retryCount == 0) logger.LogInformation("敌人初检数量： {numLabels}", numLabels - 1);

                if (numLabels > 1)
                {
                    // logger.LogInformation("检测画面内疑似有敌人，继续战斗...");

                    Mat firstRow = stats.Row(1);
                    int[] statsArray;
                    bool success = firstRow.GetArray(out statsArray); 
                    int height = statsArray[3];
                    int x = statsArray[0];
                    // Logger.LogInformation("敌人位置: ({x}，血量高度: {height}", x, height);
                    
                    mask.Dispose();
                    labels.Dispose();
                    stats.Dispose();
                    centroids.Dispose();
                    image.Dispose();
                    
                    if (success)
                    {
                        if (isEndCheck) 
                        {
                            await Task.Run(() =>
                            {
                                Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                                Task.Delay(100, ct).Wait();;
                                Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                            }, ct);
                        }
                        else
                        {
                            Simulation.SendInput.SimulateAction(GIActions.MoveForward);
                            Simulation.SendInput.SimulateAction(GIActions.MoveForward);
                        }
                        
                        if (height > 2 && height < 7)
                        {
                            // logger.LogInformation("画面内有找到敌人，尝试移动...");
                            Task.Run(() => { MoveForwardTask.MoveForwardAsync(bloodLower, bloodLower, logger, ct); }, ct);
                            return false;
                        }

                        if (height > 6 && height < 25)
                        {
                            if ((x == 758 || x == 722) && (height ==7 || height == 8))//固定血条的怪物，尝试旋转寻找
                            {
                                await Task.Run(() =>
                                {
                                    Simulation.SendInput.Mouse.MoveMouseBy(960, 0);
                                    Task.Delay(200, ct).Wait();
                                    Simulation.SendInput.Mouse.MiddleButtonClick();
                                }, ct);
                            }
                            // logger.LogInformation("画面内有找到敌人，继续战斗...");
                            return false;
                        }

                        if (height < 3 || height > 25)
                        {
                            return  null;
                        }
                    }
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
                    ra3.Dispose();
                
                    if (IsWhite(whiteTile3.Item2, whiteTile3.Item1, whiteTile3.Item0) &&
                        IsYellow(b33.Item2, b33.Item1, b33.Item0))
                    {
                        logger.LogInformation("识别到战斗结束-s");
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
                        (image.Width / 6, image.Height / 7), 
                        (image.Width / 6, 0),                 
                        (image.Width / 6, -image.Height / 5),
                        (image.Width / 6, -image.Height),  
                    };

                    var offsetIndex = RotationCount < 2 ? 0 : (RotationCount == 2) ? 1 : (RotationCount >= 3) ? 2 : 3;
                    Simulation.SendInput.Mouse.MoveMouseBy(offsets[offsetIndex].x, offsets[offsetIndex].y);
                }
                else
                {
                    Simulation.SendInput.Mouse.MoveMouseBy(image.Width / 6, 0);
                }

                await Task.Delay(50+(int)(adjustedX/adjustedDivisor),ct);

                image = CaptureToRectArea();
                mask = OpenCvCommonHelper.Threshold(image.DeriveCrop(0, 0, 1500, 900).SrcMat, bloodLower);
                labels = new Mat();
                stats = new Mat();
                centroids = new Mat();

                 numLabels = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids,
                    connectivity: PixelConnectivity.Connectivity4, ltype: MatType.CV_32S);

                if (numLabels > 1)
                {
                    // logger.LogInformation("检测敌人第 {retryCount} 次： {numLabels}", retryCount + 1, numLabels - 1);
                    Mat firstRow2 = stats.Row(1); // 获取第1行（标签1）的数据
                    int[] statsArray2;
                    bool success2 = firstRow2.GetArray(out statsArray2); // 使用 out 参数来接收数组数据
                    int height2 = statsArray2[3];
                    // logger.LogInformation("敌人血量 ：{height2}", height2);
                    
                    mask.Dispose();
                    labels.Dispose();
                    stats.Dispose();
                    centroids.Dispose();
                    image.Dispose();

                    if (success2)
                    {
                        if (isEndCheck) await Task.Run(() =>
                        {
                            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                            Task.Delay(100, ct).Wait();
                            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                        }, ct);
                        
                        if (height2 > 2 && height2 < 7)
                        {
                            // logger.LogInformation("画面内有找到敌人，尝试移动...");
                            Task.Run(() => { MoveForwardTask.MoveForwardAsync(bloodLower, bloodLower, logger, ct); }, ct);
                            return false;
                        }

                        if (height2 > 6 && height2 < 25)
                        {
                            // logger.LogInformation("画面内有找到敌人，继续战斗...");
                            return false;
                        }

                        if (height2 < 3 || height2 > 25)
                        {
                            return null;
                        }
                    }
                }
                
                mask.Dispose();
                labels.Dispose();
                stats.Dispose();
                centroids.Dispose();
                image.Dispose();
                
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
        public static async Task EnsureGuardianSkill(Avatar guardianAvatar, CombatCommand command, string lastFightName,
            string guardianAvatarName, bool guardianAvatarHold, int retryCount, CancellationToken ct,bool guardianCombatSkip = false,
            bool burstEnabled = false)
        {
            int attempt = 0;

            if (guardianAvatar.IsSkillReady())
            {
                while (attempt < retryCount)
                {
                    if (guardianAvatar.TrySwitch(10, false))
                    {
                        guardianAvatar.ManualSkillCd = -1;
                        if (await AvatarSkillAsync(Logger, guardianAvatar, false, 1, ct))
                        {
                            var cd1 = guardianAvatar.AfterUseSkill();
                            if (cd1 > 0)
                            {
                                Logger.LogInformation("优先第 {text} 盾奶位 {GuardianAvatar} 战技Cd检测：{cd} 秒", guardianAvatarName,
                                    guardianAvatar.Name, cd1);
                                guardianAvatar.ManualSkillCd = -1;
                                return;
                            }
                        }
            
                        guardianAvatar.UseSkill(guardianAvatarHold);
                        var imageAfterUseSkill = CaptureToRectArea();
                        
                        var retry = 50;
                        while (!(await AvatarSkillAsync(Logger, guardianAvatar, false, 1, ct,imageAfterUseSkill)) && retry > 0)
                        {
                            Simulation.SendInput.SimulateAction(GIActions.ElementalSkill);
                            //防止在纳塔飞天或爬墙
                            Simulation.ReleaseAllKey();
                            if (retry % 3 == 0)
                            {
                                Simulation.SendInput.SimulateAction(GIActions.NormalAttack);
                                Simulation.SendInput.SimulateAction(GIActions.Drop);
                            }
                            imageAfterUseSkill = CaptureToRectArea();
                            await Task.Delay(30, ct);
                            // Logger.LogInformation("优先第333 {t}", retry);
                            retry -= 1;
                        }
                        imageAfterUseSkill.Dispose();
                        
                        if (retry > 0)
                        {
                            Logger.LogInformation("优先第 {text} 盾奶位 {GuardianAvatar} 释放战技：{t}",
                                guardianAvatarName, guardianAvatar.Name,"成功");
                            guardianAvatar.LastSkillTime = DateTime.UtcNow;
                            guardianAvatar.ManualSkillCd = -1;
                            return;
                        }
                        
                        Logger.LogInformation("优先第 {text} 盾奶位 {GuardianAvatar} 释放战技：失败重试 {attempt} 次",
                            guardianAvatarName, guardianAvatar.Name, attempt + 1);
                        guardianAvatar.ManualSkillCd = 0;
                        guardianAvatar.UseSkill(guardianAvatarHold);
                        //防止在纳塔飞天或
                        Simulation.SendInput.SimulateAction(GIActions.NormalAttack);
                        Simulation.SendInput.SimulateAction(GIActions.Drop);
                    }
                    attempt++;
                }
            }
            else if (burstEnabled)
            {
                var image = CaptureToRectArea();
                if (!guardianAvatar.IsActive(image))
                {
                    var skillArea = AutoFightAssets.Instance.AvatarQRectListMap[guardianAvatar.Index - 1];//Q技能区域
                    // 首先对图像进行预处理，转为灰度图
                    var grayImage = image.DeriveCrop(skillArea).SrcMat.CvtColor(ColorConversionCodes.BGR2GRAY);
                
                    //调试用
                    // grayImage.SaveImage("D:\\Images\\grayImage.png");
                    // Cv2.ImShow("灰度图像", grayImage);
                    
                    // 计算图像的平均亮度
                    var meanBrightness = Cv2.Mean(grayImage);
                    var avgBrightness = meanBrightness.Val0;
                    // 根据平均亮度动态调整Canny边缘检测的阈值
                    var threshold1 = avgBrightness * 0.9;
                    var threshold2 = avgBrightness * 2;
                
                    // Logger.LogInformation("角色{i} 平均亮度 {avgBrightness}", i, avgBrightness);
                
                    Cv2.Canny(grayImage, grayImage, threshold1: (float)threshold1, threshold2: (float)threshold2); // 边缘检测
                    
                    // 使用霍夫变换检测圆形
                    var circles = Cv2.HoughCircles(grayImage, HoughModes.Gradient, dp: 1.2, minDist: 20,
                        param1: 70, param2: 30, minRadius: 25, maxRadius: 34);

                    if (circles.Length > 0)
                    {
                        Logger.LogInformation("优先第 {text} 盾奶位 {GuardianAvatar} 元素爆发状态：{attempt}，尝试释放",
                            guardianAvatarName, guardianAvatar.Name, "就绪");
                        
                        if (guardianAvatar.TrySwitch(8, false))
                        {
                            Simulation.SendInput.SimulateAction(GIActions.ElementalBurst);
                            Sleep(500, ct);
                            Simulation.ReleaseAllKey();
                        
                            //普攻一下，防止在纳塔飞天
                            Simulation.SendInput.SimulateAction(GIActions.NormalAttack);
                            var imageAfterBurst = CaptureToRectArea();
                            if (AvatarSkillAsync(Logger, guardianAvatar, true, 1, ct).Result 
                                 || !Bv.IsInMainUi(imageAfterBurst)) //Q技能CD（冷却检测）或者不在主界面（大招动画播放中）
                            {
                                guardianAvatar.IsBurstReady = false;
                            }
                            else
                            {
                                Sleep(500, ct);
                                Simulation.SendInput.SimulateAction(GIActions.NormalAttack);//普攻一下，防止在纳塔飞天
                                Simulation.SendInput.SimulateAction(GIActions.ElementalBurst);//尝试再放一次,不检查
                                guardianAvatar.IsBurstReady = true;
                            }
                            Logger.LogInformation("优先第 {guardianAvatarName} 盾奶位 {GuardianAvatar} 释放元素爆发：{text}",
                                guardianAvatarName, guardianAvatar.Name, !guardianAvatar.IsBurstReady ? "成功" : "失败");
                            
                            imageAfterBurst.Dispose();
                        }
                    }
                    
                    image.Dispose();
                    grayImage.Dispose();
                }
            }
        }
        
        //新方法，备用，非OCR识别，判断色块进行，速度更快
        //检测技能图标中释放含有白色色块，检测前进行角色切换的确认，skills：false为E技能，true为Q技能
        /// <summary>
        /// 检测角色技能冷却状态
        /// </summary>
        /// <param name="logger">日志记录器</param>
        /// <param name="guardianAvatar">角色对象</param>
        /// <param name="skills">技能类型，false为E技能，true为Q技能</param>
        /// <param name="retryCount">重试次数</param>
        /// <param name="ct">取消令牌</param>
        /// <param name="image">图像对象</param>
        /// <param name="needLog">是否需要日志输出</param>
        /// <param name="isResetCd">是否重置技能冷却状态</param>
        /// <returns>技能是否就绪</returns>
        public static async Task<bool> AvatarSkillAsync(ILogger logger, Avatar guardianAvatar, bool skills , int retryCount, 
            CancellationToken ct,ImageRegion? image = null,bool needLog = false, bool isResetCd = false)
        {
            if (guardianAvatar.TrySwitch())
            {
                Scalar bloodLower = new Scalar(255, 255, 255);
                int attempt = 0;
                var model = image is null;

                while (attempt < retryCount)
                {
                    var image2 = model ? CaptureToRectArea() : image ?? CaptureToRectArea();

                    // var image2 = CaptureToRectArea();

                    var skillAra = !skills
                        ? new Rect(image2.Width * 1688 / 1920, image2.Height * 988 / 1080,
                            image2.Width * 22 / 1920, image2.Height * 12 / 1080) //E技能区域
                        
                        : new Rect(image2.Width * 1809 / 1920, image2.Height * 968 / 1080,
                            image2.Width * 30 / 1920, image2.Height * 15 / 1080); //Q技能区域
                    
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

                    if (model) image2.Dispose();
                    mask2.Dispose();
                    labels2.Dispose();
                    stats2.Dispose();
                    centroids2.Dispose();
                    
                    if (needLog) Logger.LogInformation("技能状态：{guardianAvatar.Name} - {skills} 状态 {text}", 
                        guardianAvatar.Name, skills?"Q技能":"E技能", numLabels2 > 1?"冷却中":"就绪");
                    
                    // Logger.LogInformation("技能状态：{numLabels2} 数量", numLabels2);
                    if (numLabels2 > 2)
                    {
                        if (!isResetCd)
                        {
                            return true;
                        }
                        if (skills)
                        {
                            guardianAvatar.IsBurstReady = true;
                        }
                        else
                        {
                            guardianAvatar.ManualSkillCd = 0;
                        }
                        
                        return true;
                    }
                    
                    attempt++;
                   if (retryCount > 1) await Task.Delay(100, ct);
                }
            }

            if (!isResetCd)
            {
                return false;
            }

            if (skills)
            {
                guardianAvatar.IsBurstReady = false;
            }
            else
            {
                guardianAvatar.AfterUseSkill();
            }

            return false;

        }
        
        //全队Q检测函数，备用，后续可用于自动EQ开发
        public static Task<List<int>> AvatarQSkillAsync(ImageRegion? image = null, List<int>? useEqList = null,int? avatarCurrent = null)
        {
            image ??= CaptureToRectArea();
            image.SrcMat.ConvertTo(image.SrcMat, MatType.CV_8UC3, alpha: 2, beta: -200); // 增加亮度和对比度
            var useMedicine = new List<int>();
            var eqList = useEqList ?? new List<int> { 1, 2, 3, 4 };
        
            foreach (var i in eqList)
            {
                var skillArea = i != avatarCurrent ? AutoFightAssets.Instance.AvatarQRectListMap[i - 1]: new Rect(1762, 915, 114, 111);
                
                var grayImage = image.DeriveCrop(skillArea).SrcMat.CvtColor(ColorConversionCodes.BGR2GRAY);
        
                var meanBrightness = Cv2.Mean(grayImage);
                var avgBrightness = meanBrightness.Val0;
                var threshold1 = avgBrightness * 0.9;
                var threshold2 = avgBrightness * 2;
        
                Cv2.Canny(grayImage, grayImage, threshold1: (float)threshold1, threshold2: (float)threshold2);
        
                var circles = Cv2.HoughCircles(grayImage, HoughModes.Gradient, dp: 1.2, minDist: 20,
                    param1: 90, param2:i != avatarCurrent ? 25 : 35, minRadius: i != avatarCurrent ? 25 : 50, maxRadius:i != avatarCurrent ? 34 : 60);
        
                if (circles.Length > 0)
                {
                    useMedicine.Add(i);
                }
                grayImage.Dispose();
            }
            
            image.Dispose();
        
            if (useMedicine.Count > 0)
            {
                Logger.LogInformation("元素爆发 {text} 的角色序号：{useMedicine}", "就绪", useMedicine);
                return Task.FromResult(useMedicine);
            }
        
            return Task.FromResult(new List<int>());
        }
    }

}

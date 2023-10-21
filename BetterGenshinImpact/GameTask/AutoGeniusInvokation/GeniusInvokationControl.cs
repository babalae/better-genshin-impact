using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Assets;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.Helpers.Extensions;
using Fischless.GameCapture;
using GeniusInvokationAutoToy.Utils;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp.Extensions;
using Point = OpenCvSharp.Point;

namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation;

/// <summary>
/// 用于操控游戏
/// </summary>
public class GeniusInvokationControl
{
    private readonly ILogger<GeniusInvokationControl> _logger = App.GetLogger<GeniusInvokationControl>();

    // 定义一个静态变量来保存类的实例
    private static GeniusInvokationControl? _uniqueInstance;

    // 定义一个标识确保线程同步
    private static readonly object _locker = new();

    // 定义私有构造函数，使外界不能创建该类实例
    private GeniusInvokationControl()
    {
    }

    /// <summary>
    /// 定义公有方法提供一个全局访问点,同时你也可以定义公有属性来提供全局访问点
    /// </summary>
    /// <returns></returns>
    public static GeniusInvokationControl GetInstance()
    {
        if (_uniqueInstance == null)
        {
            lock (_locker)
            {
                _uniqueInstance ??= new GeniusInvokationControl();
            }
        }

        return _uniqueInstance;
    }

    public static bool OutputImageWhenError = true;


    private CancellationTokenSource? _cts;

    private readonly AutoGeniusInvokationAssets _assets = new();

    private IGameCapture? _gameCapture;

    public void Init(GeniusInvokationTaskParam taskParam)
    {
        _cts = taskParam.Cts;
        _gameCapture = taskParam.Dispatcher.GameCapture;
    }

    public void Sleep(int millisecondsTimeout)
    {
        CheckTask();
        Thread.Sleep(millisecondsTimeout);
    }

    public Mat CaptureGameMat()
    {
        CheckTask();
        var bitmap = _gameCapture?.Capture();
        if (bitmap == null)
        {
            _logger.LogWarning("截图失败!");
            throw new RetryException("截图失败");
        }
        return bitmap.ToMat();
    }

    public Mat CaptureGameGreyMat()
    {
        var mat = CaptureGameMat();
        Cv2.CvtColor(mat, mat, ColorConversionCodes.BGR2GRAY);
        return mat;
    }

    public RectArea CaptureGameRectArea()
    {
        var mat = CaptureGameMat();
        var systemInfo = TaskContext.Instance().SystemInfo;
        return new RectArea(mat, systemInfo.CaptureAreaRect.X, systemInfo.CaptureAreaRect.Y, systemInfo.DesktopRectArea);
        ;
    }

    public void CheckTask()
    {
        Retry.Do(() =>
        {
            if (_cts is { IsCancellationRequested: true })
            {
                return;
            }
            if (!SystemControl.IsGenshinImpactActiveByProcess())
            {
                _logger.LogWarning("当前获取焦点的窗口不是原神，暂停");
                throw new RetryException("当前获取焦点的窗口不是原神");
            }
        }, TimeSpan.FromSeconds(1), 100);


        if (_cts is { IsCancellationRequested: true })
        {
            throw new TaskCanceledException("任务取消");
        }
    }

    public void CommonDuelPrepare()
    {
        // 1. 选择初始手牌
        Sleep(1000);
        _logger.LogInformation("开始选择初始手牌");
        while (!ClickConfirm())
        {
            // 循环等待选择卡牌画面
            Sleep(1000);
        }

        _logger.LogInformation("点击确认");

        // 2. 选择出战角色
        // 此处选择第2个角色 雷神
        _logger.LogInformation("等待3s对局准备...");
        Sleep(3000);

        // 是否是再角色出战选择界面
        Retry.Do(IsInCharacterPickRetryThrowable, TimeSpan.FromSeconds(0.8), 20);
        _logger.LogInformation("识别到已经在角色出战界面，等待1.5s");
        Sleep(1500);
    }

    /// <summary>
    /// 获取我方三个角色卡牌区域
    /// </summary>
    /// <returns></returns>
    public List<Rect> GetCharacterRects()
    {
        Mat srcMat = CaptureGameMat();
        int halfHeight = srcMat.Height / 2;
        Mat bottomMat = new Mat(srcMat, new Rect(0, halfHeight, srcMat.Width, srcMat.Height - halfHeight));

        var lowPurple = new Scalar(235, 245, 198);
        var highPurple = new Scalar(255, 255, 236);
        Mat gray = OpenCvCommonHelper.Threshold(bottomMat, lowPurple, highPurple);

        // 水平投影到y轴 正常只有一个连续区域
        int[] h = ArithmeticHelper.HorizontalProjection(gray);

        // y轴 从上到下确认连续区域
        int y1 = 0, y2 = 0;
        int start = 0;
        bool inLine = false;
        for (int i = 0; i < h.Length; i++)
        {
            if (h[i] > h.Average() * 10)
            {
                // 直方图
                Cv2.Line(bottomMat, 0, i, h[i], i, Scalar.Yellow);

                if (!inLine)
                {
                    //由空白进入字符区域了，记录标记
                    inLine = true;
                    start = i;
                }
            }
            else if (inLine)
            {
                //由连续区域进入空白区域了
                inLine = false;


                if (y1 == 0)
                {
                    y1 = start;
                    if (OutputImageWhenError)
                    {
                        Cv2.Line(bottomMat, 0, y1, bottomMat.Width, y1, Scalar.Red);
                    }
                }
                else if (y2 == 0 && i - y1 > 20)
                {
                    y2 = i;
                    if (OutputImageWhenError)
                    {
                        Cv2.Line(bottomMat, 0, y2, bottomMat.Width, y2, Scalar.Red);
                    }

                    break;
                }
            }
        }

        if (y1 == 0 || y2 == 0)
        {
            _logger.LogWarning("未识别到角色卡牌区域（Y轴）");
            if (OutputImageWhenError)
            {
                Cv2.ImWrite("logs\\character_card_error.jpg", bottomMat);
            }

            throw new RetryException("未获取到角色区域");
        }

        //if (y1 < windowRect.Height / 2 || y2 < windowRect.Height / 2)
        //{
        //    MyLogger.Warn("识别的角色卡牌区域（Y轴）错误：y1:{} y2:{}", y1, y2);
        //    if (OutputImageWhenError)
        //    {
        //        Cv2.ImWrite("logs\\character_card_error.jpg", bottomMat);
        //    }

        //    throw new RetryException("未获取到角色区域");
        //}


        // 垂直投影
        int[] v = ArithmeticHelper.VerticalProjection(gray);


        inLine = false;
        start = 0;
        List<int> colLines = new List<int>();
        //开始根据投影值识别分割点
        for (int i = 0; i < v.Length; ++i)
        {
            if (v[i] > h.Average() * 10)
            {
                if (OutputImageWhenError)
                {
                    Cv2.Line(bottomMat, i, 0, i, v[i], Scalar.Yellow);
                }

                if (!inLine)
                {
                    //由空白进入字符区域了，记录标记
                    inLine = true;
                    start = i;
                }
            }
            else if (i - start > 30 && inLine)
            {
                //由连续区域进入空白区域了
                inLine = false;
                if (OutputImageWhenError)
                {
                    Cv2.Line(bottomMat, start, 0, start, bottomMat.Height, Scalar.Red);
                }

                colLines.Add(start);
            }
        }

        if (colLines.Count != 6)
        {
            _logger.LogWarning("未识别到角色卡牌区域（X轴存在{Count}个识别点）", colLines.Count);
            if (OutputImageWhenError)
            {
                Cv2.ImWrite("logs\\character_card_error.jpg", bottomMat);
            }

            throw new RetryException("未获取到角色区域");
        }

        var rects = new List<Rect>();
        for (int i = 0; i < colLines.Count - 1; i++)
        {
            if (i % 2 == 0)
            {
                var r = new Rect(colLines[i], halfHeight + y1, colLines[i + 1] - colLines[i],
                    y2 - y1);
                rects.Add(r);
            }
        }

        if (rects == null || rects.Count != 3)
        {
            throw new RetryException("未获取到角色区域");
        }

        //Cv2.ImWrite("logs\\character_card_success.jpg", bottomMat);
        return rects;
    }

    /// <summary>
    /// 点击捕获区域的相对位置
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns></returns>
    public void ClickCaptureArea(int x, int y)
    {
        var rect = TaskContext.Instance().SystemInfo.CaptureAreaRect;
        ClickExtension.Click(rect.X + x, rect.Y + y);
    }

    /// <summary>
    ///  点击游戏屏幕中心点
    /// </summary>
    public void ClickGameWindowCenter()
    {
        var p = TaskContext.Instance().SystemInfo.CaptureAreaRect.GetCenterPoint();
        p.Click();
    }

    public static Dictionary<string, List<Point>> FindMultiPicFromOneImage(Mat srcMat, Dictionary<string, Mat> imgSubDictionary, double threshold = 0.8)
    {
        var dictionary = new Dictionary<string, List<Point>>();
        foreach (var kvp in imgSubDictionary)
        {
            dictionary.Add(kvp.Key, MatchTemplateHelper.MatchTemplateMulti(srcMat, kvp.Value, threshold));
            // 最好把结果给遮掩掉，避免识别率不高的时候重复识别
        }

        return dictionary;
    }

    /// <summary>
    /// 重投骰子
    /// </summary>
    /// <param name="holdElementalTypes">保留的元素类型</param>
    public bool RollPhaseReRoll(params ElementalType[] holdElementalTypes)
    {
        var gameSnapshot = CaptureGameGreyMat();
        var dictionary = FindMultiPicFromOneImage(gameSnapshot, _assets.RollPhaseDiceMats, 0.73);

        var count = dictionary.Sum(kvp => kvp.Value.Count);


        if (count != 8)
        {
            _logger.LogInformation("投骰子界面识别到了{Count}个骰子,等待重试", count);
            return false;
        }
        else
        {
            _logger.LogInformation("投骰子界面识别到了{Count}个骰子", count);
        }

        foreach (var kvp in dictionary)
        {
            // 跳过保留的元素类型
            if (holdElementalTypes.Contains(kvp.Key.ToElementalType()))
            {
                continue;
            }

            // 选中重投
            foreach (var point in kvp.Value)
            {
                ClickCaptureArea(point.X + _assets.RollPhaseDiceMats[kvp.Key].Width / 2, point.Y + _assets.RollPhaseDiceMats[kvp.Key].Height / 2);
                Sleep(100);
            }
        }

        return true;
    }

    /// <summary>
    ///  选择手牌/重投骰子 确认
    /// </summary>
    public bool ClickConfirm()
    {
        var foundRectArea = CaptureGameRectArea().Find(_assets.ConfirmButtonRo);
        if (!foundRectArea.IsEmpty())
        {
            foundRectArea.ClickCenter();
            return true;
        }

        return false;
    }

    public void ReRollDice(params ElementalType[] holdElementalTypes)
    {
        // 3.重投骰子
        _logger.LogInformation("等待5s投骰动画...");

        var msg = holdElementalTypes.Aggregate(" ", (current, elementalType) => current + (elementalType.ToChinese() + " "));

        _logger.LogInformation("保留{Msg}骰子", msg);
        Sleep(5000);
        var retryCount = 0;
        // 保留 x、万能 骰子
        while (!RollPhaseReRoll(holdElementalTypes))
        {
            retryCount++;

            if (IsDuelEnd())
            {
                throw new DuelEndException("对战已结束,停止自动打牌！");
            }

            //MyLogger.Debug("识别骰子数量不正确,第{}次重试中...", retryCount);
            Sleep(500);
            if (retryCount > 35)
            {
                throw new System.Exception("识别骰子数量不正确,重试超时,停止自动打牌！");
            }
        }

        ClickConfirm();
        _logger.LogInformation("选择需要重投的骰子后点击确认完毕");

        Sleep(1000);
        // 鼠标移动到中心
        ClickGameWindowCenter();

        _logger.LogInformation("等待10s对方重投");
        Sleep(10000);
    }

    public Point MakeOffset(Point p)
    {
        var rect = TaskContext.Instance().SystemInfo.CaptureAreaRect;
        return new Point(rect.X + p.X, rect.Y + p.Y);
    }

    /// <summary>
    /// 计算当前有那些骰子
    /// </summary>
    /// <returns></returns>
    public Dictionary<string, int> ActionPhaseDice()
    {
        var srcMat = CaptureGameGreyMat();
        // 切割图片后再识别 加快速度 位置没啥用，所以切割后比较方便
        var dictionary = FindMultiPicFromOneImage(CutRight(srcMat, srcMat.Width / 5), _assets.ActionPhaseDiceMats);

        var msg = "";
        var result = new Dictionary<string, int>();
        foreach (var kvp in dictionary)
        {
            result.Add(kvp.Key, kvp.Value.Count);
            msg += $"{kvp.Key.ToElementalType().ToChinese()} {kvp.Value.Count}| ";
        }

        _logger.LogInformation("当前骰子状态：{Res}", result);
        return result;
    }


    /// <summary>
    ///  烧牌
    /// </summary>
    public void ActionPhaseElementalTuning()
    {
        var rect = TaskContext.Instance().SystemInfo.CaptureAreaRect;
        var m = ClickExtension.Move(rect.X + rect.Width / 2d, rect.Y + rect.Height - 50).LeftButtonClick();
        Sleep(1500);
        m.LeftButtonDown();
        Sleep(100);
        m = ClickExtension.Move(rect.X + rect.Width - 50, rect.Y + rect.Height / 2d);
        Sleep(100);
        m.LeftButtonUp();
    }

    /// <summary>
    ///  烧牌确认（元素调和按钮）
    /// </summary>
    public bool ActionPhaseElementalTuningConfirm()
    {
        var foundRectArea = CaptureGameRectArea().Find(_assets.ElementalTuningConfirmButtonRo);
        if (!foundRectArea.IsEmpty())
        {
            foundRectArea.ClickCenter();
            return true;
        }

        return false;
    }

    /// <summary>
    /// 点击切人按钮
    /// </summary>
    /// <returns></returns>
    public void ActionPhasePressSwitchButton()
    {
        var info = TaskContext.Instance().SystemInfo;
        var x = info.CaptureAreaRect.X + info.CaptureAreaRect.Width - 100 * info.AssetScale;
        var y = info.CaptureAreaRect.Y + info.CaptureAreaRect.Height - 120 * info.AssetScale;

        ClickExtension.Move(x, y).LeftButtonClick();
        Sleep(800); // 等待动画彻底弹出

        ClickExtension.Move(x, y).LeftButtonClick();
    }


    /// <summary>
    /// 使用技能
    /// </summary>
    /// <param name="skillIndex">技能编号,从右往左数,从1开始</param>
    /// <returns>元素骰子是否充足</returns>
    public bool ActionPhaseUseSkill(int skillIndex)
    {
        // 技能坐标写死 (w - 100 * n, h - 120)
        var info = TaskContext.Instance().SystemInfo;
        var x = info.CaptureAreaRect.X + info.CaptureAreaRect.Width - 100 * info.AssetScale * skillIndex;
        var y = info.CaptureAreaRect.Y + info.CaptureAreaRect.Height - 120 * info.AssetScale;
        ClickExtension.Move(x, y).LeftButtonClick();
        Sleep(1000); // 等待动画彻底弹出

        CaptureGameRectArea().Find(_assets.ElementalDiceLackWarningRo, foundRectArea =>
        {
            // 多点几次保证点击到
            _logger.LogInformation("使用技能{SkillIndex}", skillIndex);
            foundRectArea.ClickCenter();
            Sleep(200);
            foundRectArea.ClickCenter();
        });

        return true;
    }

    /// <summary>
    /// 使用技能（元素骰子不够的情况下，自动烧牌）
    /// </summary>
    /// <param name="skillIndex">技能编号,从右往左数,从1开始</param>
    /// <param name="diceCost">技能消耗骰子数</param>
    /// <param name="elementalType">消耗骰子元素类型</param>
    /// <param name="duel">对局对象</param>
    /// <returns>手牌或者元素骰子是否充足</returns>
    public bool ActionPhaseAutoUseSkill(int skillIndex, int diceCost, ElementalType elementalType, Duel duel)
    {
        var dice9RetryCount = 0;
        var retryCount = 0;
        var diceStatus = ActionPhaseDice();
        while (true)
        {
            int dCount = diceStatus.Sum(x => x.Value);
            if (dCount != duel.CurrentDiceCount)
            {
                if (retryCount > 20)
                {
                    throw new System.Exception("骰子数量与预期不符，重试次数过多，可能出现了未知错误！");
                }

                if (dCount == 9 && duel.CurrentDiceCount == 8 && diceStatus[ElementalType.Omni.ToLowerString()] > 0)
                {
                    dice9RetryCount++;
                    if (dice9RetryCount > 5)
                    {
                        // 支援区存在 鲸井小弟 情况下骰子数量增加导致识别出错的问题 #1
                        // 5次重试后仍然是9个骰子并且至少有一个万能骰子，出现多识别的情况是很稀少的，此时可以基本认为 支援区存在 鲸井小弟
                        // TODO : 但是这个方法并不是100%准确，后续需要添加支援区判断
                        _logger.LogInformation("期望的骰子数量8，应为开局期望，重试多次后累计实际识别9个骰子的情况为5次");
                        duel.CurrentDiceCount = 9; // 修正当前骰子数量
                        break;
                    }
                }


                _logger.LogInformation("当前骰子数量{Count}与期望的骰子数量{Expect}不相等，重试", dCount, duel.CurrentDiceCount);
                diceStatus = ActionPhaseDice();
                retryCount++;
                Sleep(1000);
            }
            else
            {
                break;
            }
        }


        int needSpecifyElementDiceCount = diceCost - diceStatus[ElementalType.Omni.ToLowerString()] - diceStatus[elementalType.ToLowerString()];
        if (needSpecifyElementDiceCount > 0)
        {
            if (duel.CurrentCardCount < needSpecifyElementDiceCount)
            {
                _logger.LogInformation("当前手牌数{Current}小于需要烧牌数量{Expect}，无法释放技能", duel.CurrentCardCount, needSpecifyElementDiceCount);
                return false;
            }

            _logger.LogInformation("当前需要的元素骰子数量不足{Cost}个，还缺{Lack}个，当前手牌数{Current}，烧牌", diceCost, needSpecifyElementDiceCount, duel.CurrentCardCount);

            for (var i = 0; i < needSpecifyElementDiceCount; i++)
            {
                duel.CurrentCardCount--;
                _logger.LogInformation("- {Count} 烧牌", i + 1);
                ActionPhaseElementalTuning();
                Sleep(100);
                ActionPhaseElementalTuningConfirm();
                Sleep(1000); // 烧牌动画
                ClickGameWindowCenter(); // 复位
                Sleep(500);
                // 最后一张牌的回正速度较慢，多等一会
                if (duel.CurrentCardCount <= 1)
                {
                    ClickGameWindowCenter(); // 复位
                    Sleep(1000);
                }
            }
        }

        return ActionPhaseUseSkill(skillIndex);
    }


    /// <summary>
    /// 回合结束
    /// </summary>
    public void RoundEnd()
    {
        CaptureGameRectArea().Find(_assets.RoundEndButtonRo, foundRectArea =>
        {
            foundRectArea.ClickCenter();
            Sleep(1000); // 有弹出动画 
            foundRectArea.ClickCenter();
            Sleep(300);
        });

        ClickGameWindowCenter(); // 复位
    }

    /// <summary>
    /// 是否是再角色出战选择界面
    /// 可重试方法
    /// </summary>
    public void IsInCharacterPickRetryThrowable()
    {
        if (!IsInCharacterPick())
        {
            throw new RetryException("当前不在角色出战选择界面");
        }
    }

    /// <summary>
    /// 是否是再角色出战选择界面
    /// </summary>
    /// <returns></returns>
    public bool IsInCharacterPick()
    {
        return !CaptureGameRectArea().Find(_assets.InCharacterPickRo).IsEmpty();
    }

    /// <summary>
    /// 是否是我的回合
    /// </summary>
    /// <returns></returns>
    public bool IsInMyAction()
    {
        return !CaptureGameRectArea().Find(_assets.RoundEndButtonRo).IsEmpty();
    }

    /// <summary>
    /// 是否是对方的回合
    /// </summary>
    /// <returns></returns>
    public bool IsInOpponentAction()
    {
        return !CaptureGameRectArea().Find(_assets.InOpponentActionRo).IsEmpty();
    }

    /// <summary>
    /// 是否是回合结算阶段
    /// </summary>
    /// <returns></returns>
    public bool IsEndPhase()
    {
        return !CaptureGameRectArea().Find(_assets.EndPhaseRo).IsEmpty();
    }


    /// <summary>
    /// 出战角色是否被打倒
    /// </summary>
    /// <returns></returns>
    public bool IsActiveCharacterTakenOut()
    {
        return !CaptureGameRectArea().Find(_assets.CharacterTakenOutRo).IsEmpty();
    }

    /// <summary>
    /// 哪些出战角色被打倒了
    /// </summary>
    /// <returns>true 是已经被打倒</returns>
    public bool[] WhatCharacterDefeated(List<Rect> rects)
    {
        if (rects == null || rects.Count != 3)
        {
            throw new System.Exception("未能获取到我方角色卡位置");
        }

        var pList = MatchTemplateHelper.MatchTemplateMulti(CaptureGameGreyMat(), _assets.CharacterDefeatedMat, 0.8);

        var res = new bool[3];
        foreach (var p in pList)
        {
            for (var i = 0; i < rects.Count; i++)
            {
                if (IsOverlap(rects[i], new Rect(p.X, p.Y, _assets.CharacterDefeatedMat.Width, _assets.CharacterDefeatedMat.Height)))
                {
                    res[i] = true;
                }
            }
        }


        return res;
    }

    /// <summary>
    /// 判断矩形是否重叠
    /// </summary>
    /// <param name="rc1"></param>
    /// <param name="rc2"></param>
    /// <returns></returns>
    public bool IsOverlap(Rect rc1, Rect rc2)
    {
        if (rc1.X + rc1.Width > rc2.X &&
            rc2.X + rc2.Width > rc1.X &&
            rc1.Y + rc1.Height > rc2.Y &&
            rc2.Y + rc2.Height > rc1.Y
           )
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    /// <summary>
    /// 是否对局完全结束
    /// </summary>
    /// <returns></returns>
    public bool IsDuelEnd()
    {
        return !CaptureGameRectArea().Find(_assets.ExitDuelButtonRo).IsEmpty();
    }


    public Mat CutRight(Mat srcMat, int saveRightWidth)
    {
        srcMat = new Mat(srcMat, new Rect(srcMat.Width - saveRightWidth, 0, saveRightWidth, srcMat.Height));
        return srcMat;
    }

    /// <summary>
    /// 等待我的回合
    /// 我方角色可能在此期间阵亡
    /// </summary>
    public void WaitForMyTurn(Duel duel, int waitTime = 0)
    {
        if (waitTime > 0)
        {
            _logger.LogInformation("等待对方行动{Time}s", waitTime / 1000);
            Sleep(waitTime);
        }

        // 判断对方行动是否已经结束
        var retryCount = 0;
        var inMyActionCount = 0;
        while (true)
        {
            if (IsInMyAction())
            {
                if (IsActiveCharacterTakenOut())
                {
                    DoWhenCharacterDefeated(duel);
                }
                else
                {
                    // 多延迟2s // 保证被击败提示已经完成显示
                    inMyActionCount++;
                    if (inMyActionCount == 3)
                    {
                        break;
                    }
                }
            }
            else if (IsDuelEnd())
            {
                throw new DuelEndException("对战已结束,停止自动打牌！");
            }

            retryCount++;
            if (retryCount >= 60)
            {
                throw new System.Exception("等待对方行动超时,停止自动打牌！");
            }

            _logger.LogInformation("对方仍在行动中,继续等待(次数{})...", retryCount);
            Sleep(1000);
        }
    }

    /// <summary>
    /// 等待对方回合 和 回合结束阶段
    /// 我方角色可能在此期间阵亡
    /// </summary>
    public void WaitOpponentAction(Duel duel)
    {
        var rd = new Random();
        Sleep(3000 + rd.Next(1, 1000));
        // 判断对方行动是否已经结束
        var retryCount = 0;
        while (true)
        {
            if (IsInOpponentAction())
            {
                _logger.LogInformation("对方仍在行动中,继续等待(次数{})...", retryCount);
            }
            else if (IsEndPhase())
            {
                _logger.LogInformation("正在回合结束阶段,继续等待(次数{})...", retryCount);
            }
            else if (IsInMyAction())
            {
                if (IsActiveCharacterTakenOut())
                {
                    DoWhenCharacterDefeated(duel);
                }
            }
            else if (IsDuelEnd())
            {
                throw new DuelEndException("对战已结束,停止自动打牌！");
            }
            else
            {
                // 至少走三次判断才能确定对方行动结束
                if (retryCount > 2)
                {
                    break;
                }
                else
                {
                    _logger.LogError("等待对方回合 和 回合结束阶段 时程序未识别到有效内容(次数{})...", retryCount);
                }
            }

            retryCount++;
            if (retryCount >= 30)
            {
                throw new System.Exception("等待对方行动超时,停止自动打牌！");
            }


            Sleep(1000 + rd.Next(1, 500));
        }
    }

    /// <summary>
    /// 角色被打败后要切换角色
    /// </summary>
    /// <param name="duel"></param>
    /// <exception cref="DuelEndException"></exception>
    public void DoWhenCharacterDefeated(Duel duel)
    {
        _logger.LogInformation("当前出战角色被打败，需要选择新的出战角色");
        var defeatedArray = WhatCharacterDefeated(duel.CharacterCardRects);

        for (var i = defeatedArray.Length - 1; i >= 0; i--)
        {
            duel.Characters[i + 1].IsDefeated = defeatedArray[i];
        }

        var orderList = duel.GetCharacterSwitchOrder();
        if (orderList.Count == 0)
        {
            throw new DuelEndException("后续行动策略中,已经没有可切换且存活的角色了,结束自动打牌(建议添加更多行动)");
        }

        foreach (var j in orderList)
        {
            if (!duel.Characters[j].IsDefeated)
            {
                duel.Characters[j].SwitchWhenTakenOut();
                break;
            }
        }

        ClickGameWindowCenter();
        Sleep(2000); // 切人动画
    }


    ///// <summary>
    ///// 哪个角色处于出战状态
    ///// </summary>
    ///// <returns></returns>
    //public Character WhichCharacterActive(Duel duel)
    //{
    //    if (duel.CharacterCardRects == null || duel.CharacterCardRects.Count != 3)
    //    {
    //        throw new System.Exception("未能获取到我方角色卡位置");
    //    }

    //    Mat srcMat = Capture();

    //    // 切割下半部分
    //    int halfHeight = srcMat.Height / 2;
    //    Mat bottomMat = new Mat(srcMat, new Rect(0, halfHeight, srcMat.Width, srcMat.Height - halfHeight));
    //    Mat resMat;
    //    List<Point> pList = ImageRecognition.FindMultiTarget(bottomMat,
    //        ImageResCollections.CharacterHpUpperBitmap.ToMat(), "HpUpper", out resMat, 0.7);

    //    if (pList.Count != duel.GetCharacterAliveNum())
    //    {
    //        if (OutputImageWhenError)
    //        {
    //            var outMat = srcMat.Clone();
    //            foreach (var point in pList)
    //            {
    //                Cv2.Rectangle(outMat,
    //                    new Rect(point.X, point.Y + halfHeight, ImageResCollections.CharacterHpUpperBitmap.Width,
    //                        ImageResCollections.CharacterHpUpperBitmap.Height), Scalar.Red, 2);
    //            }

    //            Cv2.ImWrite("logs\\active_character_error.jpg", outMat);
    //        }

    //        throw new RetryException($"角色Hp区块识别有误,识别到区块数量{pList.Count} != 当前存活角色数{duel.GetCharacterAliveNum()}");
    //    }

    //    int cnt = 0;
    //    for (var i = 1; i < duel.Characters.Length; i++)
    //    {
    //        var cardRect = duel.Characters[i].Area;
    //        // 2倍高度 保证能够矩形相交
    //        var rect1 = new Rectangle(cardRect.X, cardRect.Y - cardRect.Height, cardRect.Width,
    //            cardRect.Height + cardRect.Height);

    //        foreach (var point in pList)
    //        {
    //            var rect2 = new Rectangle(point.X, halfHeight + point.Y,
    //                ImageResCollections.CharacterHpUpperBitmap.Width,
    //                ImageResCollections.CharacterHpUpperBitmap.Height);
    //            if (IsOverlap(rect1, rect2))
    //            {
    //                duel.Characters[i].HpUpperArea = rect2;
    //                // 出战角色判断
    //                if (halfHeight + point.Y < cardRect.Y)
    //                {
    //                    cnt++;
    //                    duel.CurrentCharacter = duel.Characters[i];
    //                }

    //                break;
    //            }
    //        }
    //    }

    //    if (cnt != 1)
    //    {
    //        if (OutputImageWhenError)
    //        {
    //            var outMat = srcMat.Clone();
    //            foreach (var point in pList)
    //            {
    //                Cv2.Rectangle(outMat,
    //                    new Rect(point.X, point.Y + halfHeight, ImageResCollections.CharacterHpUpperBitmap.Width,
    //                        ImageResCollections.CharacterHpUpperBitmap.Height), Scalar.Red, 2);
    //            }

    //            foreach (var rc in duel.CharacterCardRects)
    //            {
    //                Cv2.Rectangle(outMat,
    //                    rc.ToCvRect(), Scalar.Green, 2);
    //            }

    //            Cv2.ImWrite("logs\\active_character_error.jpg", outMat);
    //        }

    //        throw new RetryException($"识别到{cnt}个出战角色");
    //    }

    //    AppendCharacterStatus(duel.CurrentCharacter, srcMat);
    //    return duel.CurrentCharacter;
    //}

    public void AppendCharacterStatus(Character character, Mat srcMat)
    {
        // 截取出战角色区域扩展
        var characterMat = new Mat(srcMat, new Rect(character.Area.X,
            character.Area.Y,
            character.Area.Width + 40,
            character.Area.Height + 10));
        // 识别角色异常状态
        var pCharacterStatusFreeze = MatchTemplateHelper.MatchTemplate(characterMat, _assets.CharacterStatusFreezeMat, TemplateMatchModes.CCoeffNormed);
        if (pCharacterStatusFreeze != new Point())
        {
            character.StatusList.Add(CharacterStatusEnum.Frozen);
        }
        var pCharacterStatusDizziness = MatchTemplateHelper.MatchTemplate(characterMat, _assets.CharacterStatusDizzinessMat, TemplateMatchModes.CCoeffNormed);
        if (pCharacterStatusDizziness != new Point())
        {
            character.StatusList.Add(CharacterStatusEnum.Frozen);
        }

        // 识别角色能量
        var energyPointList = MatchTemplateHelper.MatchTemplateMulti(characterMat.Clone(),_assets.CharacterEnergyOnMat, 0.8);
        character.EnergyByRecognition = energyPointList.Count;

        _logger.LogInformation("当前出战{Character}", character);
    }

    public Character WhichCharacterActiveWithRetry(Duel duel)
    {
        // 检查角色是否被击败 // 这里又检查一次是因为最后一个角色存活的情况下，会自动出战
        var defeatedArray = WhatCharacterDefeated(duel.CharacterCardRects);
        for (var i = defeatedArray.Length - 1; i >= 0; i--)
        {
            duel.Characters[i + 1].IsDefeated = defeatedArray[i];
        }

        return Retry.Do(() => WhichCharacterActiveByHpWord(duel), TimeSpan.FromSeconds(0.3), 2);
    }

    public Character WhichCharacterActiveByHpWord(Duel duel)
    {
        if (duel.CharacterCardRects == null || duel.CharacterCardRects.Count != 3)
        {
            throw new System.Exception("未能获取到我方角色卡位置");
        }

        var srcMat = CaptureGameMat();

        int halfHeight = srcMat.Height / 2;
        Mat bottomMat = new Mat(srcMat, new Rect(0, halfHeight, srcMat.Width, srcMat.Height - halfHeight));

        var lowPurple = new Scalar(239, 239, 239);
        var highPurple = new Scalar(242, 242, 250);
        Mat gray = OpenCvCommonHelper.Threshold(bottomMat, lowPurple, highPurple);

        var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(15, 10), new OpenCvSharp.Point(-1, -1));
        Cv2.Dilate(gray, gray, kernel); //膨胀


        OpenCvSharp.Point[][] contours;
        HierarchyIndex[] hierarchy;
        Cv2.FindContours(gray, out contours, out hierarchy, RetrievalModes.External,
            ContourApproximationModes.ApproxSimple, null);

        if (contours.Length > 0)
        {
            // .Where(w => w.Width > 1 && w.Height >= 5)
            var rects = contours.Select(Cv2.BoundingRect).ToList();


            // 按照Y轴高度排序
            rects = rects.OrderBy(r => r.Y).ToList();

            // 第一个和角色卡重叠的矩形
            foreach (var rect in rects)
            {
                for (var i = 0; i < duel.CharacterCardRects.Count; i++)
                {
                    // 延长高度，确保能够相交
                    var rect1 = new Rect(rect.X, halfHeight + rect.Y, rect.Width + 20,
                        rect.Height + 20);
                    if (IsOverlap(rect1, duel.CharacterCardRects[i]) &&
                        halfHeight + rect.Y < duel.CharacterCardRects[i].Y)
                    {
                        // 首个相交矩形就是出战角色
                        duel.CurrentCharacter = duel.Characters[i + 1];
                        AppendCharacterStatus(duel.CurrentCharacter, srcMat);

                        Cv2.Rectangle(srcMat, rect1, Scalar.Yellow);
                        Cv2.Rectangle(srcMat, duel.CharacterCardRects[i], Scalar.Blue, 2);
                        OutputImage(duel, rects, bottomMat, halfHeight, "logs\\active_character2_success.jpg");
                        return duel.CurrentCharacter;
                    }
                }
            }

            OutputImage(duel, rects, bottomMat, halfHeight, "logs\\active_character2_no_overlap_error.jpg");
        }
        else
        {
            if (OutputImageWhenError)
            {
                Cv2.ImWrite("logs\\active_character2_no_rects_error.jpg", gray);
            }
        }

        throw new RetryException($"未识别到个出战角色");
    }

    private static void OutputImage(Duel duel, List<Rect> rects, Mat bottomMat, int halfHeight, string fileName)
    {
        if (OutputImageWhenError)
        {
            foreach (var rect2 in rects)
            {
                Cv2.Rectangle(bottomMat, new OpenCvSharp.Point(rect2.X, rect2.Y),
                    new OpenCvSharp.Point(rect2.X + rect2.Width, rect2.Y + rect2.Height), Scalar.Red, 1);
            }

            foreach (var rc in duel.CharacterCardRects)
            {
                Cv2.Rectangle(bottomMat,
                    new Rect(rc.X, rc.Y - halfHeight, rc.Width, rc.Height), Scalar.Green, 1);
            }


            Cv2.ImWrite(fileName, bottomMat);
        }
    }
}
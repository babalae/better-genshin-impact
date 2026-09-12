using System;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.ExceptionRecovery;

/// <summary>
/// 异常恢复类触发器共用的能力：判断"游戏现在是不是明确可玩"、把游戏从
/// 「点击进入 / 进入游戏」界面推回可玩状态，以及本模块唯一的点击出口。
///
/// **为什么需要这一步**：异常弹窗（更新通知 / 连接已断开 / 连接超时）被点掉之后，游戏经常停在
/// 「点击进入」/「进入游戏」界面而不是主界面。上游负责这一段的是 <c>GameLoadingTrigger</c>（名字叫「自动开门」），
/// 但它是普通触发器、不是常驻触发器——任务启动会把它清出触发列表
/// （<see cref="GameTaskManager.ClearTriggers"/>），而且它成功一次或超过 5 分钟就会自毁
/// （`GameTask/GameLoading/GameLoading.cs:245-257`）。结果是**任务运行期间没有任何组件做这件事**，
/// 脚本卡在进入界面直到人工介入。
///
/// **整段流程照抄上游，没有新增任何图片素材**（判据与素材出处逐条标在方法上）：
/// `GameTask/GameLoading/GameLoading.cs:259-383` 的进门顺序是
/// ① 适龄提示 → ② 「点击进入」→ ③ 「进入游戏」→ ④ 月卡 → ⑤ 原石；
/// 素材全部来自 `GameTask/GameLoading/Assets/Recognition.json`（<c>ChooseEnterGame</c> / <c>EnterGame</c>）
/// 与 `GameTask/Common/Element/Assets/Recognition.json`（<c>BtnWhiteConfirm</c> / <c>Primogem</c>）。
/// </summary>
internal class EnterGameRecovery
{
    private static readonly ILogger Logger = App.GetLogger<EnterGameRecovery>();

    /// <summary>适龄提示的文案判据（同上游 `GameTask/GameLoading/GameLoading.cs:263-264`）。</summary>
    private static readonly string[] AgePromptTexts = ["适龄", "监护"];

    /// <summary>
    /// "游戏已经明确处于可玩状态"的判据，照抄上游进入游戏的成功判定
    /// （`GameTask/GameLoading/GameLoading.cs:252`：主界面 / 任一可关闭界面 / 秘境内部）。
    ///
    /// 三个判据覆盖的界面并不相同，取并集才等价于上游那句"已经进入游戏"：
    /// <see cref="Bv.IsInMainUi"/> 是派蒙菜单图标，<see cref="Bv.IsInAnyClosableUi"/> 实际只认
    /// **大地图的关闭按钮**（`BvStatus.cs:148-152`，虽然它的注释写的是"任意可以关闭的UI界面"），
    /// <see cref="Bv.IsInDomain"/> 只认秘境内部。
    ///
    /// 反过来也**不能**把它的否定当失败判据：不在已知可玩界面并不等于脚本跑不下去。
    /// 已知的反例是**剧情对话与过场动画**——那里既没有可玩的标志、也没有进入按钮，本判据会返回 false；
    /// 另外还可能是加载中，或者停在需要账号/密码的登录页。
    /// 调用方对这些情况的处理是"等到预算耗尽后如实提示"，**不记为失败**
    /// （见 <c>GameExceptionPopupTrigger.StepEnteringGame</c>）。本方法只回答"现在是不是明确可玩"。
    /// </summary>
    internal static bool IsGamePlayable(ImageRegion ra)
    {
        return Bv.IsInMainUi(ra) || Bv.IsInAnyClosableUi(ra) || Bv.IsInDomain(ra);
    }

    /// <summary>
    /// 按**上游的进门顺序**尝试推进一步：① 适龄提示 → ② 「点击进入」→ ③ 「进入游戏」→ ④ 月卡 → ⑤ 原石。
    /// 返回这一步是否真的做了动作（点了某个按钮/提示）；什么都没命中返回 false——
    /// 调用方据此**继续等待**，而不是当成失败（见 <see cref="IsGamePlayable"/> 的说明）。
    ///
    /// **为什么不止点「进入游戏」那两个按钮**：进门之后客户端还会弹**月卡**（每天首次登录几乎必现）
    /// 与**原石**提示，它们同样挡着画面。只点按钮的话脚本会卡在那块提示上：可玩判据不成立、
    /// 而进入按钮早已消失，最后只能给出一条"未检测到进入按钮"的提示——进门其实没做成。
    /// 上游把这三样（含适龄提示）都算在"进门"这一段里，这里照抄。
    ///
    /// 顺序不能乱：适龄提示是模态的，它挡着的时候点「进入游戏」是白点（上游也是先判它）。
    /// </summary>
    internal static bool TryAdvanceEnterGame(ImageRegion ra)
    {
        if (TryCloseAgePrompt(ra))
        {
            return true;
        }

        // 「点击进入」是顶号/切号后的二次确认按钮，上游也是先点它、再点「进入游戏」。
        if (TryClick(ra, "ChooseEnterGame", "点击进入"))
        {
            return true;
        }

        if (TryClick(ra, "EnterGame", "进入游戏"))
        {
            return true;
        }

        // 月卡与「原石获得」提示：上游的做法是把鼠标移到空白处点一下跳过（`GameLoading.cs:366-383`）。
        if (Bv.IsInBlessingOfTheWelkinMoon(ra))
        {
            return ClickEmptySpot("月卡提示");
        }

        using var primogem = ra.Find(ElementRecognition.Get("Primogem", ra));
        if (primogem.IsExist())
        {
            return ClickEmptySpot("原石提示");
        }

        return false;
    }

    /// <summary>
    /// 适龄提示自动关闭，判据与动作照抄上游（`GameTask/GameLoading/GameLoading.cs:259-274`）：
    /// OCR 命中「适龄」或「监护」**且**画面上有白色确认按钮才点。
    ///
    /// 与上游的唯一差别是 **OCR 区域**：上游用全屏 OCR（`RecognitionObject.OcrThis`，还有 1 秒节流），
    /// 这里只扫屏幕中部 0.5W×0.5H——与异常弹窗处理那条 OCR 同一个 ROI 依据（提示文字在画面中部）。
    /// 目的是把这一步的开销从"全屏 OCR"降到同数量级里最小的一档；如果真机上适龄提示的文字不在中部，
    /// 现象是它不被自动关闭（列在真机点检的反例里），不会误点别的东西。
    ///
    /// 这里**刻意要求 OCR 证据**，不能只看 `BtnWhiteConfirm` 模板：那是通用白色确认按钮，
    /// 别的对话框上也会出现，只看模板会在无关对话框上乱点。
    /// </summary>
    private static bool TryCloseAgePrompt(ImageRegion ra)
    {
        var hitAgePrompt = false;
        foreach (var region in ra.FindMulti(BuildAgePromptOcrRo(ra)))
        {
            foreach (var keyword in AgePromptTexts)
            {
                if (region.Text.Contains(keyword, StringComparison.Ordinal))
                {
                    hitAgePrompt = true;
                    break;
                }
            }

            if (hitAgePrompt)
            {
                break;
            }
        }

        if (!hitAgePrompt)
        {
            return false;
        }

        using var confirm = ra.Find(ElementRecognition.Get("BtnWhiteConfirm", ra));
        if (!confirm.IsExist())
        {
            return false;
        }

        return ClickIfGameActive(confirm, "适龄提示确认");
    }

    private static bool TryClick(ImageRegion ra, string objectName, string what)
    {
        using var button = ra.Find(RecognitionAssets.Get("GameLoading", objectName, ra));
        if (!button.IsExist())
        {
            return false;
        }

        // 返回的是"**真的点下去了**"，不是"找到了按钮"：窗口不在前台时会被出口挡掉，
        // 那时调用方的"已尝试 N 次"不能计数——否则会把"一次都没点过"翻成假失败。
        return ClickIfGameActive(button, what);
    }

    /// <summary>月卡/原石提示的跳过动作：点一下画面空白处（上游用的是 1080P 坐标 (100,100)，左上角天空）。</summary>
    private static bool ClickEmptySpot(string what)
    {
        return ClickIfGameActive(100, 100, what);
    }

    /// <summary>适龄提示文案的 OCR 区域：屏幕中部 x/y 各 0.25~0.75（理由见 <see cref="TryCloseAgePrompt"/>）。</summary>
    private static RecognitionObject BuildAgePromptOcrRo(ImageRegion ra)
    {
        return RecognitionObject.Ocr(ra.Width * 0.25, ra.Height * 0.25, ra.Width * 0.5, ra.Height * 0.5);
    }

    /// <summary>
    /// 点击前的最后一道校验，也是本模块**唯一**的点击出口：所有会发输入的路径都从这里走，
    /// 避免各写一份之后各自漂移（"会发输入的出口要收口"）。**当前只有异常弹窗处理这一条路径**；
    /// 后续把断网恢复接进来时也从这里点，不要在别处新开一个点击出口。
    ///
    /// 点击走的是 SendInput 绝对桌面坐标，会落到**当前前台窗口**上，所以必须先确认前台就是
    /// **本实例的游戏主窗口**（<see cref="SystemControl.IsGenshinImpactActive"/>：`GetForegroundWindow() == GameHandle`，
    /// 与 `TaskTriggerDispatcher` 判定"游戏是否在前台"用的是同一个判据）。
    /// 刻意不用只比进程名的 <see cref="SystemControl.IsGenshinImpactActiveByProcess"/>：多开/子会话下
    /// 两个原神进程**同名**，前台是另一个实例的窗口时它仍然为真，键鼠就会打到别的实例上。
    /// 刻意**不**抢焦点、**不**还原最小化窗口：那是用户自己的桌面状态。
    ///
    /// 返回**是否真的点了**（详见 <see cref="TryClick"/>）。
    /// </summary>
    internal static bool ClickIfGameActive(Region region, string what)
    {
        if (!IsGameWindowReadyToClick(what))
        {
            return false;
        }

        Logger.LogInformation("[异常恢复] 点击：{What}", what);
        region.Click();
        return true;
    }

    /// <summary>
    /// 同 <see cref="ClickIfGameActive(Region, string)"/>，但点的是 **1080P 固定坐标**
    /// （跳过月卡/原石提示要用它：那两个提示没有可识别的按钮，上游也是点空白处）。
    /// 两个重载共用同一个前台校验，避免"有两份收口、各自漂移"。
    /// </summary>
    internal static bool ClickIfGameActive(float x1080, float y1080, string what)
    {
        if (!IsGameWindowReadyToClick(what))
        {
            return false;
        }

        Logger.LogInformation("[异常恢复] 点击：{What}", what);
        GameCaptureRegion.GameRegion1080PPosClick(x1080, y1080);
        return true;
    }

    /// <summary>点击前的前台/最小化校验（两个重载共用）。</summary>
    private static bool IsGameWindowReadyToClick(string what)
    {
        if (SystemControl.IsGenshinImpactMinimized())
        {
            Logger.LogWarning("[异常恢复] 游戏窗口已最小化，跳过点击（{What}）", what);
            return false;
        }

        if (!SystemControl.IsGenshinImpactActive())
        {
            Logger.LogWarning("[异常恢复] 游戏主窗口不在前台，跳过点击（{What}）", what);
            return false;
        }

        return true;
    }
}

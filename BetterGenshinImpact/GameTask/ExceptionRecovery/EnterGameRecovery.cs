using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.ExceptionRecovery;

/// <summary>
/// 异常恢复类触发器共用的能力：判断"游戏现在是不是明确可玩"、把游戏从
/// 「点击进入 / 进入游戏」界面推回可玩状态，以及本模块唯一的点击出口。
///
/// **为什么需要这一步**：异常弹窗（更新通知 / 连接已断开 / 连接超时）被点掉之后，游戏经常停在
/// 「点击进入」/「进入游戏」界面而不是主界面。上游负责点这两个按钮的是 <c>GameLoadingTrigger</c>，
/// 但它是普通触发器、不是常驻触发器——任务启动会把它清出触发列表
/// （<see cref="GameTaskManager.ClearTriggers"/>），而且它成功一次或超过 5 分钟就会自毁
/// （`GameTask/GameLoading/GameLoading.cs:245-257`）。结果是**任务运行期间没有任何组件会点这两个按钮**，
/// 脚本卡在进入界面直到人工介入。
///
/// **识别与点击顺序照抄上游，没有新增任何图片素材**：用的是
/// `GameTask/GameLoading/Assets/Recognition.json` 里已有的 <c>ChooseEnterGame</c> 与 <c>EnterGame</c>，
/// 顺序与 `GameTask/GameLoading/GameLoading.cs:310-328` 一致（先「点击进入」，再「进入游戏」）。
/// </summary>
internal class EnterGameRecovery
{
    private static readonly ILogger Logger = App.GetLogger<EnterGameRecovery>();

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
    /// 识别并点击「点击进入 / 进入游戏」。返回是否真的点了一下。
    /// 两个按钮都不在画面上时返回 false——调用方据此**继续等待**，而不是当成失败（见类注释）。
    /// </summary>
    internal static bool TryClickEnterGame(ImageRegion ra)
    {
        // 「点击进入」是顶号/切号后的二次确认按钮，上游也是先点它、再点「进入游戏」。
        return TryClick(ra, "ChooseEnterGame", "点击进入")
               || TryClick(ra, "EnterGame", "进入游戏");
    }

    private static bool TryClick(ImageRegion ra, string objectName, string what)
    {
        using var button = ra.Find(RecognitionAssets.Get("GameLoading", objectName, ra));
        if (!button.IsExist())
        {
            return false;
        }

        // 返回的是"**真的点下去了**"，不是"找到了按钮"：窗口不在前台时会被出口挡掉，
        // 那时调用方的"已点击 N 次"不能计数——否则会把"一次都没点过"翻成假失败。
        return ClickIfGameActive(button, what);
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

        Logger.LogInformation("[异常恢复] 点击：{What}", what);
        region.Click();
        return true;
    }
}

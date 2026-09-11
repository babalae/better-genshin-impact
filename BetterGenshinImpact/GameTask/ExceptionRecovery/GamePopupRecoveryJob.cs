using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoWood.Utils;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.ExceptionRecovery;

/// <summary>
/// 异常弹窗恢复流程：把「更新通知 / 连接中断 / 连接超时 / 无法登录服务器」这类弹窗点掉，
/// 必要时（可选）自动重新登录，最终回到主界面。
///
/// 与茶包版实现的关键差别（鲁棒性修复）：
/// 1. 返回值即"是否真的回到主界面"，由派蒙头像模板判定，不再无条件宣布成功；
/// 2. 点击前必须同时存在"异常文案"证据，绝不对普通弹窗乱点「确认」；
/// 3. 每次截图都用 using 释放，不存在为了取宽高而白截一帧的泄漏；
/// 4. 登录界面处理优先复用既有的 ExitAndReloginJob（模板驱动），不使用固定坐标点登录页；
/// 5. 全程响应 CancellationToken，超时/取消立即退出。
/// </summary>
public class GamePopupRecoveryJob
{
    /// <summary>本次恢复所属的截图会话代次；会话一切换，旧流程必须立刻停手。</summary>
    private readonly int _sessionEpoch;

    private readonly Login3rdParty _login3rdParty = new();

    /// <summary>本次恢复是否真的对游戏做出过动作（点击过弹窗按钮/残留关闭按钮）。</summary>
    private bool _acted;

    public GamePopupRecoveryJob(int sessionEpoch)
    {
        _sessionEpoch = sessionEpoch;
    }

    /// <summary>
    /// 本次恢复是否真的点击过游戏。未点击时不该计入"恢复失败"：
    /// 例如游戏正在加载/下载（画面里没有可点按钮），那不是"反复尝试失败"，不该消耗熔断额度。
    /// </summary>
    public bool Acted => _acted;

    public string Name => "异常弹窗恢复";

    /// <summary>
    /// 会话已被切换（截图器停止 / 新会话接管）。
    /// 单飞的所有权以会话为单位，会话切换后旧流程就"失去身份"了；
    /// 而取消只是协作式请求，所以每次动作前都要自己确认一次，避免两个恢复流程同时点击游戏。
    /// </summary>
    private bool IsSessionExpired => !GameExceptionPopupTrigger.IsCurrentSession(_sessionEpoch);

    /// <summary>判定"这是异常弹窗"的文案；命中其中之一才允许继续动作。</summary>
    public static readonly string[] DefaultPopupTexts =
    [
        "连接超时",
        "连接已断开",
        "与服务器断开连接",
        "网络错误",
        "无法登录服务器",
        "更新通知",
        "更新公告",
        "游戏已更新",
        "重新连接"
    ];

    /// <summary>
    /// 允许点击的按钮文案（白名单，避免误点）。
    /// 命中的按钮还必须与异常文案"属于同一个弹窗"（见 <see cref="TryClickPopupButton"/>）。
    /// 一个都没命中时（例如非简中客户端的按钮文案是 "Confirm"）退回按钮**图形模板**点击，
    /// 不把白名单当作唯一路径。
    /// </summary>
    private static readonly string[] ClickableButtonTexts =
    [
        "确认",
        "确定",
        "点击进入",
        "知道了"
    ];

    private static ILogger Logger => TaskControl.Logger;

    /// <summary>
    /// 执行一次恢复。返回 true 的两种情形：**已确认回到主界面**，
    /// 或者**触发恢复的证据已全部消失**（弹窗被点掉，游戏可能正在加载/重登）。
    /// </summary>
    public async Task<bool> RunAsync(CancellationToken ct)
    {
        var config = TaskContext.Instance().Config.OtherConfig.ExceptionRecoveryConfig;
        var popupTexts = ExceptionRecoveryDecisions.MergeKeywords(DefaultPopupTexts, config.PopupRecoveryExtraKeywords);
        var rounds = Math.Clamp(config.PopupRecoveryClickRounds, 1, 20);

        // 会话已切换（截图器停止 / 新会话接管）就不要有任何动作——
        // 连抢焦点都不要做：那是窗口级副作用，会打断用户。
        if (IsSessionExpired || ct.IsCancellationRequested)
        {
            return false;
        }

        // 点击动作需要有焦点窗口；先尝试恢复一次（失败不致命，点击前还会再确认）
        EnsureGameFocus();

        // 阶段一：循环点掉弹窗，直到回到主界面
        for (var i = 0; i < rounds; i++)
        {
            if (ct.IsCancellationRequested || IsSessionExpired)
            {
                return false;
            }

            if (IsInMainUi())
            {
                Logger.LogInformation("[异常恢复] 已在主界面，恢复结束");
                return true;
            }

            try
            {
                var clicked = TryClickPopupButton(popupTexts, ct);
                // 恢复流程内不使用 TaskControl.Delay：它内部的 CheckAndActivateGameWindow 会抛
                // RetryException，被 NewRetry 放大成"每次等待 ≈100 秒"，10 分钟超时基本必到。
                await Task.Delay(clicked ? 800 : 400, ct);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                // 单轮失败（例如抢焦点超时抛 RetryException）不应终止整个恢复流程
                if (ct.IsCancellationRequested)
                {
                    return false;
                }

                Logger.LogDebug(ex, "[异常恢复] 本轮处理异常，继续重试");
                await Task.Delay(300, ct);
            }
        }

        // 阶段二：残留弹窗兜底（茶包版同样会点掉遮挡层的关闭按钮）
        TryClickResidualCloseButton(ct);
        if (await VerifyMainUiAsync(ct, 3))
        {
            Logger.LogInformation("[异常恢复] 点击残留弹窗后已回到主界面");
            return true;
        }

        // 阶段三要用的判定必须提前算：下面的"弹窗已消失"短路要让位于"需要重登"的场景，
        // 否则点掉「无法登录服务器」后游戏停在登录界面时，自动重登永远不会执行。
        var loginScreenDetected = IsLoginScreen();

        // 阶段二点五：异常弹窗已经消失，且阶段三本来也不会做任何事 → 判成功。
        // 热更后的资源校验/加载画面可能几十秒都不是主界面，若在那里判失败，
        // 探针每 30 秒一次就会在 2~3 分钟内连续失败 3 次触发 10 分钟熔断——
        // 而游戏其实只是还在加载，恰好把最需要这个功能的时段关掉了。
        //
        // 门槛用"登录界面证据"而不是 needRelogin：needRelogin = 开关 ∧ 证据，
        // 用户没开自动重登时它恒为 false，会把"停在登录界面"也判成成功——熔断被清零，
        // 脚本在登录界面上继续跑，而任务期间 GameLoadingTrigger 已被清出触发列表，没人会去点「进入游戏」。
        if (!ct.IsCancellationRequested && !loginScreenDetected && IsAbnormalPopupGone(popupTexts))
        {
            // 判成功之前**有界重采样**登录界面：上面那次快照可能早于登录界面渲染——
            // 点掉弹窗后游戏常先进黑屏/资源校验，登录界面 4~8 秒后才出现（真机点检清单记录的时序）。
            // 这里必须**真的等**：两次紧邻的采样间隔只有一次截图，覆盖不到那个窗口，等于没采。
            // 代价只在"即将判成功"时支付（最长 Rounds × Interval），而阶段二原本已等约 3 秒、
            // 阶段四可等 30 秒——用这点等待换"不会跳过自动重登"是划算的。
            for (var i = 0; i < LoginScreenResampleRounds && !loginScreenDetected; i++)
            {
                await Task.Delay(LoginScreenResampleIntervalMs, ct);
                loginScreenDetected |= IsLoginScreen();
            }

            if (!loginScreenDetected)
            {
                Logger.LogInformation("[异常恢复] 异常弹窗已消失（游戏可能正在加载），本次恢复判定为成功");
                return true;
            }

            // 观察到了登录界面：不要在这里判成功，交给阶段三按登录界面处理（重登或明确失败）。
            Logger.LogWarning("[异常恢复] 弹窗消失后观察到登录/进入游戏界面，改按登录界面流程处理");
        }

        var needRelogin = ExceptionRecoveryDecisions.ShouldRelogin(
            config.PopupRecoveryAutoReloginEnabled, loginScreenDetected);

        // 阶段三：登录/进入游戏界面处理
        // flowAborted：流程"异常中断"（非取消类异常）后，阶段四的"证据消失即成功"回退不可信
        var flowAborted = false;
        try
        {
            if (IsSessionExpired)
            {
                return false;
            }

            if (loginScreenDetected)
            {
                if (!needRelogin)
                {
                    // 停在登录界面、但用户没开「异常处理后自动重新登录」：这里**不**盲动——
                    // 登录界面上按 ESC / 找「退出门」有可能把游戏退掉。直接判失败，
                    // 交给熔断与提示告诉用户需要手动进入游戏。
                    Logger.LogWarning("[异常恢复] 识别到登录/进入游戏界面，但未开启「异常处理后自动重新登录」，请手动进入游戏");
                    return false;
                }

                Logger.LogWarning("[异常恢复] 识别到登录/进入游戏界面，尝试自动进入游戏");
                if (EnsureGameFocus())
                {
                    if (!await EnterGameFromLoginScreenAsync(ct))
                    {
                        Logger.LogWarning("[异常恢复] 未能从登录界面进入游戏");
                    }
                }
                else
                {
                    Logger.LogWarning("[异常恢复] 游戏窗口不在前台，跳过自动进入游戏");
                }
            }
            else
            {
                Logger.LogWarning("[异常恢复] 未识别到登录界面（login={Login}），尝试返回主界面", loginScreenDetected);

                // 进入共享的上游 Job 之前先自行确认一次会话：它内部只认 CancellationToken，
                // 不认本模块的会话代次。会话已切换时取消虽然会被 ResetSession 请求（ct 就是那个
                // 被取消的 CTS），但协作式取消只在它的下一个等待点生效，因此不能靠它来"不进入"。
                if (ct.IsCancellationRequested || IsSessionExpired)
                {
                    return false;
                }

                if (EnsureGameFocus())
                {
                    // 上游 Job 内部自己发 ESC / 回车 / 点击，本文件无法在每次输入前校验；
                    // 而恢复作用域又刻意短路了 TaskControl 的"失焦即中止"保护（否则会被 NewRetry
                    // 放大成约 100 秒），因此这里包一层前台守护：游戏不再前台就取消这个 Job，
                    // 避免键鼠输入打到用户当前的前台窗口上。
                    await RunWithFocusGuardAsync(token => new ReturnMainUiTask().Start(token), ct);
                }
                else
                {
                    Logger.LogWarning("[异常恢复] 游戏窗口不在前台，跳过返回主界面");
                }
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (NormalEndException)
        {
            // TaskControl.Delay/Sleep 在被取消（或用户暂停）时抛这个，它不是 OperationCanceledException：
            // 同样属于"这次恢复被中止"，绝不能继续往下走"证据消失即成功"的回退。
            Logger.LogWarning("[异常恢复] 恢复流程在登录/返回主界面阶段被取消");
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[异常恢复] 登录/返回主界面流程异常");
            flowAborted = true;
        }

        // 阶段四：回到主界面就算成功。
        // 窗口给足 30 秒：热更/重登后游戏要加载，5 秒的旧预算会把"还在加载"误判成失败，
        // 连续几次就把功能自己熔断了。
        var succeeded = await VerifyMainUiAsync(ct, 30);
        if (succeeded)
        {
            Logger.LogInformation("[异常恢复] 已确认回到主界面");
        }
        else if (!flowAborted && !ct.IsCancellationRequested && !loginScreenDetected && IsAbnormalPopupGone(popupTexts))
        {
            // 只有"本来就不需要重登、也没有停在登录界面"时，"证据消失"才算成功。
            // 一旦识别到登录界面，就必须真的回到主界面才算成功——否则重登失败/被跳过会被
            // 这里的回退洗成"恢复成功"，清掉熔断计数并把"已处理，脚本继续执行"报给用户，
            // 而游戏其实还停在登录界面（那里没有异常弹窗，触发器不会再给它第二次机会）。
            //
            // **刻意不在这里拿 IsLoginScreen() 否决成功**：主要场景（弹窗消失后登录界面才出现）
            // 已由阶段二点五的**有界重采样**封住；剩下这个残留窗口（返回主界面期间切到登录界面）
            // 只能靠 AutoWood/EnterGame 那个 38×36 电源符号小模板来判（阈值 0.8、ROI 下半屏），
            // 它在加载画面/主界面上会不会误命中**无法离线判定**；一旦误命中就把"本该成功"翻成失败，
            // 连续 3 次即假熔断——那正是上面注释刻意要避免的形态（修一个点、多一个点）。
            // 所以这里只留**诊断**：真机上出现这条 Warning 就说明该残留窗口真实存在，
            // 届时按"加第二独立证据"的方式收紧，而不是凭猜收紧。
            if (IsLoginScreen())
            {
                Logger.LogWarning("[异常恢复] 证据已消失但当前疑似停在登录/进入游戏界面（已知残留，仍按成功处理）");
            }

            Logger.LogInformation("[异常恢复] 异常弹窗已消失，本次恢复判定为成功（未能确认主界面，游戏可能仍在加载）");
            succeeded = true;
        }
        else
        {
            Logger.LogWarning("[异常恢复] 未能确认回到主界面，本次恢复判定为失败");
        }

        return succeeded;
    }

    /// <summary>
    /// 从登录/进入界面进入游戏。
    ///
    /// **照抄上游既有实现**，取两处现成逻辑的并集：
    /// * 「自动开门」触发器（<c>GameLoadingTrigger</c>，上游在 #1853 强化的「自动点击登录」）：
    ///   先处理官服**顶号/切号**后弹出的「进入游戏」按钮（<c>GameLoading/ChooseEnterGame</c>），
    ///   再处理普通的「点击进入」（<c>GameLoading/EnterGame</c>）——用模板定位而不是写死坐标；
    /// * <see cref="ExitAndReloginJob"/> 的后半段：以"派蒙菜单出现"作为进入游戏完成的判据。
    ///
    /// **不做** <see cref="ExitAndReloginJob"/> 的前半段（ESC 打开背包菜单 → 点左下角退出 → 确认退出）：
    /// 进入本流程时游戏本来就停在登录界面，那一段既无用（白等十几秒），
    /// 又会在登录界面上盲点左下角、并调用内部带忙等循环的 <c>SystemControl.FocusWindow</c>。
    /// </summary>
    private async Task<bool> EnterGameFromLoginScreenAsync(CancellationToken ct)
    {
        _login3rdParty.RefreshAvailabled();
        if (_login3rdParty is { Type: Login3rdParty.The3rdPartyType.Bilibili, IsAvailabled: true })
        {
            // B 服登录复用上游 Login3rdParty：它内部每轮含固定 sleep 与坐标点击，
            // 取消只在轮间生效。这里在调用前后各校验一次（取消/会话已切换就不进/立刻退出），
            // 把"旧流程继续输入"的窗口压到最小。
            if (ct.IsCancellationRequested || IsSessionExpired)
            {
                return false;
            }

            Logger.LogInformation("[异常恢复] 进入游戏流程启用 B 服模式");
            _login3rdParty.Login(ct);

            if (ct.IsCancellationRequested || IsSessionExpired)
            {
                return false;
            }
        }

        // 每次点击后重新识别，直到确认回到主界面；上限沿用上游 ExitAndReloginJob 的量级（约 2 分钟）
        for (var i = 0; i < 120; i++)
        {
            if (ct.IsCancellationRequested || IsSessionExpired)
            {
                return false;
            }

            using var ra = CaptureOrNull();
            if (ra != null)
            {
                if (Bv.IsInMainUi(ra))
                {
                    return true;
                }

                // 官服：顶号/切号后的「进入游戏」弹窗（照抄 GameLoadingTrigger）
                using (var chooseEnterGame = ra.Find(RecognitionAssets.Get("GameLoading", "ChooseEnterGame", ra)))
                {
                    if (!chooseEnterGame.IsEmpty())
                    {
                        Logger.LogInformation("[异常恢复] 检测到顶号/切号后的进入游戏弹窗，点击进入");

                        // 与其他点击路径共用 TryClick(region, ct)：取消 / 会话 / 前台三项校验都在里面，
                        // 并在焦点恢复的等待之后、真正发送点击之前再复查一次。
                        // 点击成功会记 _acted —— 自动进入游戏同样是对游戏做出的动作；
                        // 否则"点了进入游戏、最终仍没回到主界面"这类失败永远不会计入熔断。
                        if (!TryClick(chooseEnterGame, ct))
                        {
                            return false;
                        }

                        await Task.Delay(1000, ct);
                        continue;
                    }
                }

                // 普通的「点击进入」
                using (var enterGame = ra.Find(RecognitionAssets.Get("GameLoading", "EnterGame", ra)))
                {
                    if (!enterGame.IsEmpty())
                    {
                        Logger.LogInformation("[异常恢复] 点击「点击进入」");

                        // 同上：走统一的点击路径（含 _acted 记账）
                        if (!TryClick(enterGame, ct))
                        {
                            return false;
                        }

                        await Task.Delay(1000, ct);
                        continue;
                    }
                }
            }

            await Task.Delay(1000, ct);
        }

        Logger.LogWarning("[异常恢复] 未检测到主界面，进入游戏可能未完成");
        return false;
    }

    /// <summary>
    /// 触发恢复的那组证据（外观模板 + 提示框判定 + 异常文案，与触发器启动恢复时用的是同一组）
    /// 是否都已经消失。用于把"游戏正在加载"与"恢复失败"区分开：
    /// 加载画面既不是主界面，也不该算失败。
    ///
    /// 前提是**这一帧本身有效**：窗口最小化、或近全黑的无内容帧一律按"未消失"处理，
    /// 否则"看不见任何东西"会被当成"弹窗不在了"，从而误报成功并反复清零熔断计数。
    /// </summary>
    private bool IsAbnormalPopupGone(IReadOnlyList<string> popupTexts)
    {
        if (IsSessionExpired)
        {
            return false;
        }

        try
        {
            // 窗口最小化：截图必然是无效内容，不能作为任何判据
            if (SystemControl.IsGenshinImpactMinimized())
            {
                return false;
            }

            using var ra = CaptureOrNull();
            if (ra == null || IsNearlyBlackFrame(ra))
            {
                // 截图失败 / 近全黑帧：不据此判定成功
                return false;
            }

            // 外观证据（与触发器启动恢复用的是同一组判定）仍在 → 弹窗还没消失
            using (var confirm = ra.Find(ElementRecognition.Get("PopupConfirmButton", ra)))
            {
                if (confirm.IsExist())
                {
                    return false;
                }
            }

            using (var exitSwitch = ra.Find(ElementRecognition.Get("PopupExitSwitch", ra)))
            {
                if (exitSwitch.IsExist())
                {
                    return false;
                }
            }

            if (Bv.IsInPromptDialog(ra))
            {
                return false;
            }

            var textRegions = ra.FindMulti(BuildOcrRo(ra));
            return !textRegions.Any(r => ExceptionRecoveryDecisions.MatchesAny(r.Text, popupTexts));
        }
        catch (Exception ex)
        {
            // 识别异常按"未知"处理：不据此判成功（阶段一/阶段二的同类调用也都有保护）
            Logger.LogDebug(ex, "[异常恢复] 判定弹窗是否消失时出现异常，按未消失处理");
            return false;
        }
    }

    /// <summary>
    /// 是否为近全黑的无内容帧（窗口最小化/切换过程中的采集结果）。
    /// 判据沿用仓库既有写法（<c>AutoSkipTrigger</c> 的黑屏统计：中间 1/3 区域纯黑像素占比）。
    /// </summary>
    private static bool IsNearlyBlackFrame(ImageRegion ra)
    {
        try
        {
            var grey = ra.CacheGreyMat;
            using var greyPart = new Mat(grey, new Rect(0, grey.Height / 3, grey.Width, grey.Height / 3));
            var blackCount = OpenCvCommonHelper.CountGrayMatColor(greyPart, 0);
            var rate = blackCount * 1d / (greyPart.Width * greyPart.Height);
            return rate >= 0.98999;
        }
        catch (Exception)
        {
            // 统计失败时不阻断判定（按"不是黑帧"处理，后续还有模板与 OCR 两道证据）
            return false;
        }
    }

    /// <summary>当前是否在主界面（截图失败按"不是"处理）。</summary>
    private static bool IsInMainUi()
    {
        using var ra = CaptureOrNull();
        return ra != null && Bv.IsInMainUi(ra);
    }

    /// <summary>
    /// 恢复流程专用截图：走 <see cref="TaskControl.CaptureGameImageNoRetry"/>，
    /// 不进入 CaptureGameImage 的"截图失败重试 3 次 + CheckAndActivateGameWindow"路径——
    /// 那条路径在游戏窗口非前台且未开启焦点恢复时会经 NewRetry 放大成每次约 100 秒且不可取消，
    /// 会把恢复超时预算白白吃掉。截图失败返回 null，由调用方按"本次判定失败"处理。
    /// </summary>
    private static ImageRegion? CaptureOrNull()
    {
        var image = TaskControl.CaptureGameImageNoRetry(TaskTriggerDispatcher.GlobalGameCapture);
        return image == null ? null : new CaptureContent(image, 0, 0).CaptureRectArea;
    }

    /// <summary>
    /// 点击前确认游戏在前台。
    /// SendInput 用的是绝对桌面坐标，窗口失焦时点击会落到别的窗口上；
    /// 这里只做"一次恢复尝试 + 判定"，失败就让本次点击作罢，
    /// 不使用 <c>CheckAndActivateGameWindow</c>（它内部是无上限的抢焦点循环，还会最小化其它窗口）。
    /// 用 <c>RestoreWindow</c> 而不是 <c>FocusWindow</c>：后者在窗口持续最小化时会忙等死循环，
    /// 一旦卡住恢复流程就永远不会走到 finally（单飞标记与挂起都清不掉）。
    /// </summary>
    private static bool EnsureGameFocus()
    {
        try
        {
            if (SystemControl.IsGenshinImpactActiveByProcess())
            {
                return true;
            }

            SystemControl.RestoreWindow(TaskContext.Instance().GameHandle);
            Thread.Sleep(200);
            return SystemControl.IsGenshinImpactActiveByProcess();
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "[异常恢复] 确认游戏窗口前台状态失败");
            return false;
        }
    }

    /// <summary>
    /// 反复确认是否回到主界面。
    /// 会话已切换（截图器停止 / 新会话接管）时立刻收手：此时读到的是**新会话**的画面，
    /// 拿它去宣布"已回到主界面"毫无意义（旧流程的结果会被会话代次挡住，但没必要继续空转 30 秒）。
    /// </summary>
    private async Task<bool> VerifyMainUiAsync(CancellationToken ct, int retryTimes)
    {
        for (var i = 0; i < retryTimes; i++)
        {
            if (ct.IsCancellationRequested || IsSessionExpired)
            {
                return false;
            }

            if (IsInMainUi() && !IsSessionExpired)
            {
                return true;
            }

            await Task.Delay(1000, ct);
        }

        return false;
    }

    /// <summary>
    /// 尝试点击弹窗按钮：先要求"存在异常文案"与"存在白名单按钮文案"且两者配成同一个弹窗；
    /// 白名单文案一个都没命中时（其它语言的客户端），退回"异常文案 + 确认按钮图形模板"。
    /// </summary>
    private bool TryClickPopupButton(IReadOnlyList<string> popupTexts, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || IsSessionExpired)
        {
            return false;
        }

        using var ra = CaptureOrNull();
        if (ra == null)
        {
            return false;
        }

        // FindMulti 返回的是仅含坐标与文本的 Region（Region.Derive，没有自己的 Mat，
        // Region.Dispose() 是空实现），图像由上面的 using 持有，这里无需逐个释放。
        var textRegions = ra.FindMulti(BuildOcrRo(ra));
        if (textRegions.Count == 0)
        {
            return false;
        }

        // 证据 1：屏幕上确实存在异常弹窗文案（避免把普通确认框当异常弹窗）
        var popupText = textRegions.FirstOrDefault(r => ExceptionRecoveryDecisions.MatchesAny(r.Text, popupTexts));
        if (popupText == null)
        {
            return false;
        }

        // 证据 2：存在可点击的按钮文案，且必须与异常文案**属于同一个弹窗**——
        // 只要求两者分别出现在同一块大 ROI 里是不够的：画面上只要有任意一个普通对话框的
        // 「确认/确定」，就可能被误点。这里按"离异常文案最近的按钮"配对，并限制两者的距离。
        // 排除异常文案自身那块区域：OCR 可能把正文和按钮标签合并进同一个框，
        // 那时"最近的按钮"就是正文自己，点击会落在弹窗中心而不是按钮上。
        var button = textRegions
            .Where(r => !ReferenceEquals(r, popupText) && ExceptionRecoveryDecisions.MatchesAny(r.Text, ClickableButtonTexts))
            .OrderBy(r => CenterDistance(r, popupText))
            .FirstOrDefault();

        if (button == null)
        {
            // 一个白名单按钮文案都没有：多半是按钮文案跟着客户端语言变了（"Confirm" 之类）。
            // 文案会变、按钮图形不会变，因此退回"图形"点击——**但仍然要求与异常文案同处一个弹窗**。
            return TryClickConfirmButtonByTemplate(ra, popupText, ct);
        }

        if (CenterDistance(button, popupText) > MaxEvidenceDistance)
        {
            // 找得到按钮、但它离异常文案太远：这种情况**不**再退模板兜底，
            // 否则等于把"离得太远所以不点"这条判定重新放行。
            Logger.LogWarning("[异常恢复] 可点按钮离异常文案过远（{Distance:F0}px），判定不属于同一弹窗，跳过点击",
                CenterDistance(button, popupText));
            return false;
        }

        Logger.LogWarning("[异常恢复] 点击弹窗按钮: {Text}", button.Text);
        return TryClick(button, ct);
    }

    /// <summary>
    /// 按钮文案没命中时依次尝试的确认按钮模板：先本功能自己的弹窗确认按钮，
    /// 再上游通用的白色/黑色确认按钮（覆盖黑白两种提示框）。
    /// </summary>
    private static readonly string[] ConfirmButtonTemplates =
    [
        "PopupConfirmButton",
        "BtnWhiteConfirm",
        "BtnBlackConfirm"
    ];

    /// <summary>
    /// 按钮文案没命中时的兜底：用确认按钮的**图形模板**点击。
    ///
    /// 依据与上游一致——「适龄提示」自动关闭（<c>GameLoading</c>：OCR 文案作证据 + <c>BtnWhiteConfirm</c> 模板点击）
    /// 与 <c>Bv.ClickConfirmButton</c>（黑/白确认按钮 + 联机确认）都是这么做的：
    /// 按钮文案随客户端语言变化，按钮图形不随语言变化。
    /// 前提没有放松——调用方已确认画面里确有异常弹窗文案，且模板按钮同样要过
    /// <see cref="MaxEvidenceDistance"/> 这道"与异常文案同处一个弹窗"的判定。
    /// </summary>
    private bool TryClickConfirmButtonByTemplate(ImageRegion ra, Region popupText, CancellationToken ct)
    {
        // 搜索范围限定在画面下半部分：弹窗按钮都在那里，避免命中其它界面上的按钮。
        // 用 Clone + RegionOfInterest 与上游 Bv.FindElementAndClick 的 searchRect 是同一套机制。
        var lowerHalf = new Rect(0, ra.Height / 2, ra.Width, ra.Height / 2);

        foreach (var name in ConfirmButtonTemplates)
        {
            if (ct.IsCancellationRequested || IsSessionExpired)
            {
                return false;
            }

            var ro = ElementRecognition.Get(name, ra).Clone();
            ro.RegionOfInterest = ro.RegionOfInterest == default
                ? lowerHalf // 通用按钮模板没有自带 ROI
                : ro.RegionOfInterest.Intersect(lowerHalf); // 自带 ROI 的（弹窗确认按钮）与下半屏取交集

            // 交集为空要显式跳过：把空的 RegionOfInterest 交给识别器会被当成"没有 ROI"，
            // 搜索范围从下半屏静默扩大到整屏（上游 Bv.FindElementAndClick 同样有这个守卫）。
            if (ro.RegionOfInterest.Width <= 0 || ro.RegionOfInterest.Height <= 0)
            {
                continue;
            }

            using var button = ra.Find(ro);
            if (!button.IsExist())
            {
                continue;
            }

            // 与白名单路径同一口径：模板按钮也必须离异常文案足够近，否则换下一个模板
            // （否则画面上同时存在异常文案和另一个确认框时，会点到无关按钮）。
            if (CenterDistance(button, popupText) > MaxEvidenceDistance)
            {
                Logger.LogWarning("[异常恢复] 确认按钮模板 {Template} 离异常文案过远（{Distance:F0}px），换下一个模板",
                    name, CenterDistance(button, popupText));
                continue;
            }

            Logger.LogWarning("[异常恢复] 未识别到按钮文案，改用确认按钮模板点击: {Template}", name);
            return TryClick(button, ct);
        }

        return false;
    }

    /// <summary>
    /// 在委托给"内部自己发输入"的上游长流程 Job 期间守护前台。
    ///
    /// 为什么需要：恢复流程跑在 <see cref="ExceptionSuspendSignal.EnterRecoveryScope"/> 作用域里，
    /// 该作用域会短路 <c>TaskControl.CheckAndActivateGameWindow</c>（否则失焦时抛的 RetryException
    /// 会被 NewRetry 放大成约 100 秒的无效等待），于是上游 Job 内部"窗口不在前台就中止本轮"的
    /// 保护一起失效；而它发的 ESC / 回车是全局输入、点击用的是**绝对桌面坐标**——
    /// 用户在此期间切走窗口，输入就会打到别的程序上。
    ///
    /// 处理方式与 <see cref="EnsureGameFocus"/> 同口径：先尝试抢回前台，抢不回来（或窗口被最小化）
    /// 就取消这个 Job。每 500ms 复查一次，而上游 Job 的两次输入之间至少间隔 0.5 秒，
    /// 因此最多只会有一次输入落在检查窗口里。
    /// </summary>
    private static async Task RunWithFocusGuardAsync(Func<CancellationToken, Task> action, CancellationToken ct)
    {
        using var guarded = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var guard = Task.Run(async () =>
        {
            // 最小化要连续命中两次才中止：全屏/窗口切换的瞬间 IsIconic 可能是真，
            // 只命中一次就取消会让"游戏其实正常"的返回主界面流程被误杀。
            var minimizedStrikes = 0;

            try
            {
                while (!guarded.IsCancellationRequested)
                {
                    await Task.Delay(500).ConfigureAwait(false);

                    if (guarded.IsCancellationRequested)
                    {
                        return;
                    }

                    if (SystemControl.IsGenshinImpactMinimized())
                    {
                        if (++minimizedStrikes >= 2)
                        {
                            Logger.LogWarning("[异常恢复] 游戏窗口被最小化，中止返回主界面流程");
                            guarded.Cancel();
                            return;
                        }

                        continue;
                    }

                    minimizedStrikes = 0;

                    if (SystemControl.IsGenshinImpactActiveByProcess())
                    {
                        continue;
                    }

                    SystemControl.RestoreWindow(TaskContext.Instance().GameHandle);
                    Thread.Sleep(100);
                    if (!SystemControl.IsGenshinImpactActiveByProcess())
                    {
                        Logger.LogWarning("[异常恢复] 游戏窗口不再前台，中止返回主界面流程（避免输入落到其它窗口）");
                        guarded.Cancel();
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                // 守护本身失败不影响主流程（最坏退化成原来的行为）
                Logger.LogDebug(ex, "[异常恢复] 前台守护异常（已忽略）");
            }
        });

        try
        {
            await action(guarded.Token).ConfigureAwait(false);
        }
        finally
        {
            guarded.Cancel();

            // 等守护线程收尾（有界），避免留下一个还在跑的轮询任务
            await Task.WhenAny(guard, Task.Delay(1500)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 点击前确认会话与前台状态后点击。
    /// 失焦时绝对坐标点击会落到别的窗口上，因此点之前再确认一次；不行就放弃本次点击
    /// （会话已切换或任务已取消时同样放弃——旧流程的点击会与新会话的恢复互相打架）。
    /// </summary>
    private bool TryClick(Region region, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || IsSessionExpired || !EnsureGameFocus())
        {
            Logger.LogWarning("[异常恢复] 游戏窗口不在前台、会话已切换或任务已取消，跳过本次点击");
            return false;
        }

        // EnsureGameFocus 内含 200ms 的等待，等待期间可能刚被取消或换了会话：
        // 点击前再复查一次，把"取消之后仍发出一次点击"的窗口压到最小
        // （协作式取消无法彻底消除最后一次复查与点击之间的竞态，但不再有可避免的那一次）。
        if (ct.IsCancellationRequested || IsSessionExpired)
        {
            Logger.LogWarning("[异常恢复] 点击前发现任务已取消或会话已切换，放弃本次点击");
            return false;
        }

        // 用户主动最小化 = "别碰我的桌面"：不要抢回前台，也不要往别处发点击
        if (SystemControl.IsGenshinImpactMinimized())
        {
            Logger.LogWarning("[异常恢复] 游戏窗口被最小化，跳过本次点击");
            return false;
        }

        region.Click();
        _acted = true;
        return true;
    }

    /// <summary>
    /// 两项证据允许的最大中心距（1080P 下的像素）。弹窗标题与确认按钮通常相距不到 200px；
    /// 超过这个距离基本可以认为它们不属于同一个弹窗。
    /// </summary>
    private const double MaxEvidenceDistance = 420;

    /// <summary>
    /// 阶段二点五"判成功"之前对登录界面的有界重采样轮数。
    ///
    /// 必须覆盖"点掉异常弹窗后游戏先进黑屏/资源校验、登录界面 **4~8 秒后**才渲染出来"这条真机时序
    /// （见真机点检清单）。**两次紧邻的采样（间隔只有一次截图，约 0.1~0.3 秒）覆盖不到它，等于没采**——
    /// 那正是"修了一个点却没修住"的形态。
    /// </summary>
    private const int LoginScreenResampleRounds = 3;

    /// <summary>有界重采样的间隔（毫秒），与 <see cref="LoginScreenResampleRounds"/> 相乘即最长等待。</summary>
    private const int LoginScreenResampleIntervalMs = 2000;

    /// <summary>两个识别区域中心的距离。</summary>
    private static double CenterDistance(Region a, Region b)
    {
        var dx = a.X + a.Width / 2.0 - (b.X + b.Width / 2.0);
        var dy = a.Y + a.Height / 2.0 - (b.Y + b.Height / 2.0);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>点掉残留遮挡弹窗的关闭按钮（对应早期实现的 mi_menu 点击）。</summary>
    private void TryClickResidualCloseButton(CancellationToken ct)
    {
        if (ct.IsCancellationRequested || IsSessionExpired)
        {
            return;
        }

        try
        {
            // 已经在主界面就不存在"残留遮挡弹窗"：这一步没有任何异常证据要求，
            // 若不加这道判定，可能在标题/主界面右下角产生一次无意义的点击。
            if (IsInMainUi())
            {
                return;
            }

            using var ra = CaptureOrNull();
            if (ra == null)
            {
                return;
            }

            using var miMenu = ra.Find(ElementRecognition.Get("PopupMiMenu", ra));
            if (miMenu.IsExist())
            {
                Logger.LogWarning("[异常恢复] 点击残留弹窗关闭按钮");

                // 与弹窗按钮同一条点击路径（会话/取消/前台三项校验都在里面）
                TryClick(miMenu, ct);
            }
        }
        catch (Exception ex)
        {
            // 兜底点击失败不影响主流程（后续还有登录/返回主界面阶段）
            Logger.LogDebug(ex, "[异常恢复] 残留弹窗兜底点击失败");
        }
    }

    /// <summary>是否停留在登录/进入游戏界面（复用既有的进入游戏按钮模板）。</summary>
    private static bool IsLoginScreen()
    {
        using var ra = CaptureOrNull();
        if (ra == null)
        {
            return false;
        }

        using var enterGame = ra.Find(RecognitionAssets.Get("AutoWood", "EnterGame", ra));
        return enterGame.IsExist();
    }

    /// <summary>
    /// OCR 弹窗文本区域：与触发器保持一致（x 0.25~0.8W、y 0.25~0.8H），
    /// 比早期实现（0.3~0.95W × 0.1~0.97H）面积小约 45%，降低识别开销。
    /// </summary>
    private static RecognitionObject BuildOcrRo(ImageRegion ra)
    {
        return RecognitionObject.Ocr(ra.Width * 0.25, ra.Height * 0.25, ra.Width * 0.55, ra.Height * 0.55);
    }
}

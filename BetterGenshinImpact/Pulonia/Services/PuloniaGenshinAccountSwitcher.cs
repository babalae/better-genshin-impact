using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Input.Backends.Win32;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Script.Dependence;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Helpers.Win32;
using BetterGenshinImpact.Pulonia.Models;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 将 AccountSwitchStateMachine 和 SwitchAccountMultipleMode 的 PC 官服流程移植到 C#。
/// 调用方拥有输入租约；所有识别循环共享有限预算，不模拟停止热键、不运行 JS。
/// </summary>
public sealed class PuloniaGenshinAccountSwitcher(PuloniaTaskStore store) : IPuloniaGenshinAccountSwitcher
{
    /// <summary>整个登录准备的有限总时长。</summary>
    private static readonly TimeSpan PreparationBudget = TimeSpan.FromSeconds(180);
    /// <summary>键鼠响应与截图刷新等待时间。</summary>
    private const int ResponseDelayMilliseconds = 500;
    /// <summary>等待进入主界面期间，日志窗口提示的间隔。</summary>
    private static readonly TimeSpan WaitingHintInterval = TimeSpan.FromSeconds(15);

    /// <inheritdoc />
    public async Task<string> PrepareAsync(GameRuntime runtime, PuloniaGenshinAccountProfile account, CancellationToken ct)
    {
        if (!account.IsDefault && !Regex.IsMatch(account.Uid ?? "", "^[0-9]{9,10}$"))
            throw new InvalidOperationException("指定账号需要填写完整 UID，不能按未指定目标切换。");
        if (!account.IsDefault) EnsureSupportedRuntime(runtime);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(PreparationBudget);
        try
        {
            using var recognition = new PuloniaGenshinAccountRecognition();
            // 1. 核验当前账号：停在派蒙菜单或可关闭的游戏内界面时先返回主界面，登录界面不按 Esc
            TaskControl.Logger.LogInformation("账号准备：正在核验当前登录的账号");
            await ReturnFromGameMenuAsync(runtime, recognition, budget.Token).ConfigureAwait(false);
            var current = await ReadStableUidAsync(runtime, null, budget.Token).ConfigureAwait(false);
            if (current is not null && (account.IsDefault || current == account.Uid))
            {
                TaskControl.Logger.LogInformation("账号准备：账号已核验");
                return current;
            }
            if (!account.IsDefault)
            {
                var hasLoginSelection = account.LoginMode switch
                {
                    PuloniaGenshinAccountLoginMode.Image => account.ImagePath is not null,
                    PuloniaGenshinAccountLoginMode.ListText => !string.IsNullOrWhiteSpace(account.ListText),
                    PuloniaGenshinAccountLoginMode.Password => account.CredentialReference is not null,
                    PuloniaGenshinAccountLoginMode.Automatic => account.ImagePath is not null
                        || !string.IsNullOrWhiteSpace(account.ListText) || account.CredentialReference is not null,
                    _ => false
                };
                if (!hasLoginSelection) throw new InvalidOperationException("目标账号尚未配置图片、列表文字或密码，未登出当前账号。");
                TaskControl.Logger.LogInformation("账号准备：正在切换到账号“{Name}”", account.Name);
            }
            // 2. 指定账号时先切换，再进入主界面重新核验 UID
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (!account.IsDefault)
                {
                    await GoToAccountSelectionAsync(runtime, recognition, budget.Token).ConfigureAwait(false);
                    await SelectAccountAsync(runtime, account, recognition, budget.Token).ConfigureAwait(false);
                }
                await EnterMainUiAsync(runtime, recognition, budget.Token).ConfigureAwait(false);
                current = await ReadStableUidAsync(runtime, account.Uid, budget.Token).ConfigureAwait(false);
                if (current is not null)
                {
                    TaskControl.Logger.LogInformation("账号准备：账号已核验");
                    return current;
                }
            }
            throw new InvalidOperationException("切号后无法完整核验目标 UID，未执行任何业务任务。");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException("账号准备超过 180 秒，已停止并等待人工处理。");
        }
    }

    /// <inheritdoc />
    public async Task VerifyAsync(GameRuntime runtime, string expectedUid, CancellationToken ct)
    {
        if (await ReadStableUidAsync(runtime, expectedUid, ct).ConfigureAwait(false) is null)
            throw new InvalidOperationException("当前 UID 与本次已核验的账号不一致，已停止后续游戏任务。");
    }

    /// <summary>要求当前租约绑定 PC 官服本机窗口，避免把官服模板用于其他登录渠道。</summary>
    /// <param name="runtime">调用方租约持有的运行环境</param>
    private static void EnsureSupportedRuntime(GameRuntime runtime)
    {
        if (InputHub.Backend is not Win32InputBackend)
            throw new InvalidOperationException("自动切号首版仅支持 PC 官服的键鼠输入。");
        User32.GetWindowThreadProcessId(runtime.Window.Handle, out var processId);
        using var process = Process.GetProcessById((int)processId);
        // B 服同为 YuanShen 进程，但登录界面与官服模板不同，会在模板匹配阶段停止而不会误操作，因此不再读取 config.ini 区分渠道。
        if (!string.Equals(process.ProcessName, "YuanShen", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("自动切号首版仅支持本机 PC 官服。");
    }

    /// <summary>在独立的新截图中连续读取两次完整 UID，识别失败不当作 UID 为零。</summary>
    private static async Task<string?> ReadStableUidAsync(GameRuntime runtime, string? expected, CancellationToken ct)
    {
        string? previous = null;
        for (var i = 0; i < 6; i++)
        {
            ct.ThrowIfCancellationRequested();
            using (var frame = runtime.CaptureToRectArea())
            {
                if (Bv.IsInMainUi(frame))
                {
                    var ocr = RecognitionObject.Ocr(PuloniaGenshinAccountRecognition.Scale(frame, new Rect(1683, 1051, 234, 28)));
                    using var pen = ocr.DrawOnWindowPen;
                    using var result = frame.Find(ocr);
                    var rawText = result.Text ?? "";
                    var uid = Regex.Match(rawText.Replace(" ", ""), @"(?<!\d)\d{9,10}(?!\d)").Value;
                    // UID 核验失败时只能从 OCR 原文判断原因；Debug 级别只写入日志文件，不显示在遮罩日志框。
                    TaskControl.Logger.LogDebug("Pulonia UID 识别第 {Frame}/6 帧：OCR 原文“{Text}”，提取 UID“{Uid}”，目标 UID“{Expected}”",
                        i + 1, rawText, uid, expected ?? "不限定");
                    if (uid.Length > 0 && (expected is null || uid == expected))
                    {
                        if (previous == uid) return uid;
                        previous = uid;
                    }
                    else previous = null;
                }
                else previous = null;
            }
            await Task.Delay(ResponseDelayMilliseconds, ct).ConfigureAwait(false);
        }
        return null;
    }

    /// <summary>识别验证码及明确登录错误，不重复提交凭据。</summary>
    private static void CheckLoginFailure(ImageRegion frame, PuloniaGenshinAccountRecognition recognition)
    {
        using var verification = recognition.Find(frame, "verification");
        if (verification.IsExist()) throw new InvalidOperationException("登录需要验证码，请人工处理后重新运行。");
        var ocr = RecognitionObject.Ocr(PuloniaGenshinAccountRecognition.Scale(frame, new Rect(450, 160, 1020, 780)));
        using var pen = ocr.DrawOnWindowPen;
        using var text = frame.Find(ocr);
        if (Regex.IsMatch(text.Text, "密码错误|账号或密码错误|账号不存在|账号被冻结|登录失败|安全验证|人机验证|验证码"))
            throw new InvalidOperationException("登录出现验证码、安全验证或账号密码错误，请人工处理。");
    }

    /// <summary>进入账号选择登录页；状态判断使用 JS 状态机的模板和明确的转移。</summary>
    private static async Task GoToAccountSelectionAsync(GameRuntime runtime, PuloniaGenshinAccountRecognition recognition, CancellationToken ct)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(60))
        {
            ct.ThrowIfCancellationRequested();
            using (var frame = runtime.CaptureToRectArea())
            {
                CheckLoginFailure(frame, recognition);
                switch (DetectState(frame, recognition))
                {
                    case PuloniaGenshinAccountScreenState.AccountSelection:
                    case PuloniaGenshinAccountScreenState.AccountList:
                    case PuloniaGenshinAccountScreenState.PasswordForm: return;
                    case PuloniaGenshinAccountScreenState.MainUi: InputHub.Foreground.Keyboard.KeyPress(User32.VK.VK_ESCAPE); break;
                    case PuloniaGenshinAccountScreenState.Menu: ClickReference(frame, 50, 1025); break;
                    case PuloniaGenshinAccountScreenState.LogoutConfirmation: ClickTemplate(frame, recognition, "PrepareToLogOut"); break;
                    case PuloniaGenshinAccountScreenState.ExitAccount: ClickTemplate(frame, recognition, "Exit"); break;
                    case PuloniaGenshinAccountScreenState.AgeConfirmation: ClickTemplate(frame, recognition, "Confirm"); break;
                    case PuloniaGenshinAccountScreenState.Gate: ClickTemplate(frame, recognition, "SwitchAccount"); break;
                    case PuloniaGenshinAccountScreenState.NoAccount: ClickTemplate(frame, recognition, "Login"); break;
                }
            }
            await Task.Delay(ResponseDelayMilliseconds, ct).ConfigureAwait(false);
        }
        throw new InvalidOperationException("无法到达账号选择界面，请人工检查登录状态。");
    }

    /// <summary>按配置选择账号；只有目标未找到时才使用可选的密码备用。</summary>
    private async Task SelectAccountAsync(GameRuntime runtime, PuloniaGenshinAccountProfile account,
        PuloniaGenshinAccountRecognition recognition, CancellationToken ct)
    {
        if (account.LoginMode != PuloniaGenshinAccountLoginMode.Password
            && (account.ImagePath is not null || !string.IsNullOrWhiteSpace(account.ListText)))
        {
            // 已停留在密码表单时，图片或列表模式先退出表单回到登录选择。
            using (var frame = runtime.CaptureToRectArea())
                if (Exists(frame, recognition, "EnterAccountAndPassword")) ClickReference(frame, 1260, 259);
            await Task.Delay(ResponseDelayMilliseconds, ct).ConfigureAwait(false);
            using (var frame = runtime.CaptureToRectArea())
                if (Exists(frame, recognition, "EnterGame") && !Exists(frame, recognition, "SelectAccount")) ClickReference(frame, 974, 496);
            await Task.Delay(ResponseDelayMilliseconds, ct).ConfigureAwait(false);
            if (account.LoginMode is PuloniaGenshinAccountLoginMode.Automatic or PuloniaGenshinAccountLoginMode.Image
                && account.ImagePath is not null && await SelectByImageAsync(runtime, account, ct).ConfigureAwait(false)) return;
            if (account.LoginMode is PuloniaGenshinAccountLoginMode.Automatic or PuloniaGenshinAccountLoginMode.ListText
                && !string.IsNullOrWhiteSpace(account.ListText) && await SelectByTextAsync(runtime, account.ListText, ct).ConfigureAwait(false)) return;
        }
        if (account.LoginMode is not (PuloniaGenshinAccountLoginMode.Automatic or PuloniaGenshinAccountLoginMode.Password)
            || account.CredentialReference is null)
            throw new InvalidOperationException("未找到目标账号，请更新账号图片或登录列表文字。");
        await LoginWithPasswordAsync(runtime, account, recognition, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 匹配运行快照记录的账号图片；使用唯一结果，不能点击多个相同标识。
    /// 图片文件每次新建、不覆盖，快照中的 ImagePath 即固定了图片内容，因此不再复核内容指纹。
    /// </summary>
    private async Task<bool> SelectByImageAsync(GameRuntime runtime, PuloniaGenshinAccountProfile account, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(store.GetAccountImagePath(account.ImagePath!), ct).ConfigureAwait(false);
        using var original = Cv2.ImDecode(bytes, ImreadModes.Color);
        for (var i = 0; i < 6; i++)
        {
            using var frame = runtime.CaptureToRectArea();
            using var scaled = new Mat();
            Cv2.Resize(original, scaled, new Size(Math.Max(1, (int)Math.Round(original.Width * frame.Width / 1920d)),
                Math.Max(1, (int)Math.Round(original.Height * frame.Width / 1920d))));
            var template = RecognitionObject.TemplateMatch(scaled);
            try
            {
                template.Threshold = 0.9;
                var matches = frame.FindMulti(template);
                try
                {
                    if (matches.Count > 1) throw new InvalidOperationException("账号图片匹配到多个候选，请重新采集更明确的账号标识。");
                    if (matches.Count == 1)
                    {
                        matches[0].Click();
                        await Task.Delay(ResponseDelayMilliseconds, ct).ConfigureAwait(false);
                        return true;
                    }
                }
                finally { foreach (var match in matches) match.Dispose(); }
            }
            finally { template.TemplateImageGreyMat?.Dispose(); template.DrawOnWindowPen.Dispose(); }
            await Task.Delay(ResponseDelayMilliseconds, ct).ConfigureAwait(false);
        }
        return false;
    }

    /// <summary>规范化 OCR 账号文字；掩码只匹配对应字符位置，不使用任意子串相似度。</summary>
    internal static bool MatchesListText(string actual, string target)
    {
        static string Normalize(string value) => Regex.Replace(value.Normalize().ToLowerInvariant(), @"\s+", "")
            .Replace('＊', '*').Replace('•', '*').Replace('●', '*');
        actual = Normalize(actual); target = Normalize(target);
        return actual.Length == target.Length && actual.Length > 0
            && actual.Zip(target).All(pair => pair.First == pair.Second || pair.First == '*' || pair.Second == '*');
    }

    /// <summary>只在账号列表范围识别文字，匹配多个账号时直接停止。</summary>
    private static async Task<bool> SelectByTextAsync(GameRuntime runtime, string target, CancellationToken ct)
    {
        for (var i = 0; i < 6; i++)
        {
            using var frame = runtime.CaptureToRectArea();
            var ocr = RecognitionObject.Ocr(PuloniaGenshinAccountRecognition.Scale(frame, new Rect(640, 440, 650, 580)));
            using var pen = ocr.DrawOnWindowPen;
            var results = frame.FindMulti(ocr);
            try
            {
                var matches = results.Where(result => MatchesListText(result.Text, target)).ToArray();
                if (matches.Length > 1) throw new InvalidOperationException("登录列表中存在多个匹配账号，请改用账号图片或密码登录。");
                if (matches.Length == 1)
                {
                    matches[0].Click();
                    await Task.Delay(ResponseDelayMilliseconds, ct).ConfigureAwait(false);
                    return true;
                }
            }
            finally { foreach (var result in results) result.Dispose(); }
            await Task.Delay(ResponseDelayMilliseconds, ct).ConfigureAwait(false);
        }
        return false;
    }

    /// <summary>使用 Windows 凭据填充已识别的登录表单，密码不进入快照和日志。</summary>
    private static async Task LoginWithPasswordAsync(GameRuntime runtime, PuloniaGenshinAccountProfile account,
        PuloniaGenshinAccountRecognition recognition, CancellationToken ct)
    {
        var credential = CredentialManagerHelper.ReadCredential(account.CredentialReference!)
                         ?? throw new InvalidOperationException("登录凭据不存在，请在账号管理中重新保存。");
        var alreadyInForm = false;
        using (var frame = runtime.CaptureToRectArea())
        {
            alreadyInForm = Exists(frame, recognition, "EnterAccountAndPassword");
            if (Exists(frame, recognition, "SelectAccount")) ClickReference(frame, 957, 404);
        }
        await Task.Delay(ResponseDelayMilliseconds, ct).ConfigureAwait(false);
        if (!alreadyInForm) await WaitAndClickAsync(runtime, recognition, "LoginOther", ct).ConfigureAwait(false);
        // 登录表单只输入一次；错误或验证界面由后续状态识别明确停止。
        await WaitAndClickAsync(runtime, recognition, "EnterAccount", ct).ConfigureAwait(false);
        GlobalMethod.InputText(credential.UserName);
        await Task.Delay(ResponseDelayMilliseconds, ct).ConfigureAwait(false);
        await WaitAndClickAsync(runtime, recognition, "EnterPassword", ct).ConfigureAwait(false);
        GlobalMethod.InputText(credential.Password);
        await Task.Delay(ResponseDelayMilliseconds, ct).ConfigureAwait(false);
        using (var frame = runtime.CaptureToRectArea()) ClickTemplate(frame, recognition, "Agree");
        await Task.Delay(ResponseDelayMilliseconds, ct).ConfigureAwait(false);
        await WaitAndClickAsync(runtime, recognition, "EnterGame2", ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 从明确登录状态进入主界面；只处理已识别的按钮、月卡提示和游戏内菜单。
    /// 其他界面（加载、对话、过场等）不猜测操作，按间隔在日志窗口提示，总时长由调用方的准备预算限制。
    /// </summary>
    private static async Task EnterMainUiAsync(GameRuntime runtime, PuloniaGenshinAccountRecognition recognition, CancellationToken ct)
    {
        // 等待计时只用于提示
        var waited = Stopwatch.StartNew();
        var nextHintAt = TimeSpan.Zero;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var inGameMenu = false;
            using (var frame = runtime.CaptureToRectArea())
            {
                if (Bv.IsInMainUi(frame)) return;
                CheckLoginFailure(frame, recognition);
                var state = DetectState(frame, recognition);
                switch (state)
                {
                    case PuloniaGenshinAccountScreenState.AccountSelection: ClickTemplate(frame, recognition, "EnterGame"); break;
                    case PuloniaGenshinAccountScreenState.AccountList: ClickReference(frame, 957, 404); break;
                    case PuloniaGenshinAccountScreenState.Gate: ClickReference(frame, 960, 990); break;
                    case PuloniaGenshinAccountScreenState.AgeConfirmation: ClickTemplate(frame, recognition, "Confirm"); break;
                    default:
                        if (Bv.IsInBlessingOfTheWelkinMoon(frame)) ClickReference(frame, 960, 900);
                        else inGameMenu = IsInGameMenu(frame, state);
                        break;
                }
            }

            // 1. 游戏内菜单：按 Esc 返回主界面后重新识别
            if (inGameMenu)
            {
                await ReturnMainUiFromMenuAsync(runtime, ct).ConfigureAwait(false);
                continue;
            }

            // 2. 其他界面：首次立即提示，之后按间隔提示仍在等待
            if (waited.Elapsed >= nextHintAt)
            {
                if (nextHintAt == TimeSpan.Zero)
                    TaskControl.Logger.LogInformation("账号准备：当前不在游戏主界面，正在等待进入主界面（账号准备最长 {Seconds} 秒）。如果游戏停在对话、过场等界面，请手动返回主界面",
                        (int)PreparationBudget.TotalSeconds);
                else
                    TaskControl.Logger.LogInformation("账号准备：仍在等待进入游戏主界面，已等待 {Seconds} 秒", (int)waited.Elapsed.TotalSeconds);
                nextHintAt += WaitingHintInterval;
            }
            await Task.Delay(ResponseDelayMilliseconds, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 开始准备时游戏停在派蒙菜单或可关闭的游戏内界面，先按 Esc 返回主界面；主界面、登录界面和其他界面不操作。
    /// </summary>
    private static async Task ReturnFromGameMenuAsync(GameRuntime runtime, PuloniaGenshinAccountRecognition recognition, CancellationToken ct)
    {
        using (var frame = runtime.CaptureToRectArea())
        {
            if (Bv.IsInMainUi(frame) || !IsInGameMenu(frame, DetectState(frame, recognition))) return;
        }
        await ReturnMainUiFromMenuAsync(runtime, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 判断是否停在游戏内菜单：派蒙菜单，或未识别为任何登录界面且带关闭按钮的界面。
    /// 登录相关界面一律不算，避免按 Esc 弹出退出确认。
    /// </summary>
    /// <param name="frame">当前截图。</param>
    /// <param name="state">同一截图的登录界面状态。</param>
    private static bool IsInGameMenu(ImageRegion frame, PuloniaGenshinAccountScreenState state)
        => state == PuloniaGenshinAccountScreenState.Menu
           || (state == PuloniaGenshinAccountScreenState.Unknown && Bv.IsInAnyClosableUi(frame));

    /// <summary>提示并按 Esc 返回主界面；返回主界面任务自带按键后的等待，是否成功由调用方重新识别。</summary>
    private static async Task ReturnMainUiFromMenuAsync(GameRuntime runtime, CancellationToken ct)
    {
        TaskControl.Logger.LogInformation("账号准备：当前在游戏内菜单，正在按 Esc 返回主界面");
        await new ReturnMainUiTask(runtime).Start(ct).ConfigureAwait(false);
    }

    /// <summary>在同一取消预算内等待按钮，识别不到时不能猜测点击位置。</summary>
    private static async Task WaitAndClickAsync(GameRuntime runtime, PuloniaGenshinAccountRecognition recognition, string name, CancellationToken ct)
    {
        for (var i = 0; i < 60; i++)
        {
            ct.ThrowIfCancellationRequested();
            using (var frame = runtime.CaptureToRectArea())
            {
                CheckLoginFailure(frame, recognition);
                if (ClickTemplate(frame, recognition, name))
                {
                    await Task.Delay(ResponseDelayMilliseconds, ct).ConfigureAwait(false);
                    return;
                }
            }
            await Task.Delay(ResponseDelayMilliseconds, ct).ConfigureAwait(false);
        }
        throw new InvalidOperationException("登录界面未出现预期按钮：" + name);
    }

    /// <summary>查找一次模板并在截图释放前读取结果。</summary>
    private static bool Exists(ImageRegion frame, PuloniaGenshinAccountRecognition recognition, string name)
    {
        using var region = recognition.Find(frame, name);
        return region.IsExist();
    }

    /// <summary>按对话框、登录表单、账号列表和背景界面的优先级识别状态，避免背景按钮抢先匹配。</summary>
    private static PuloniaGenshinAccountScreenState DetectState(ImageRegion frame, PuloniaGenshinAccountRecognition recognition)
    {
        if (Exists(frame, recognition, "Confirm")) return PuloniaGenshinAccountScreenState.AgeConfirmation;
        if (Exists(frame, recognition, "Exit")) return PuloniaGenshinAccountScreenState.ExitAccount;
        if (Exists(frame, recognition, "PrepareToLogOut")) return PuloniaGenshinAccountScreenState.LogoutConfirmation;
        if (Exists(frame, recognition, "EnterAccountAndPassword")) return PuloniaGenshinAccountScreenState.PasswordForm;
        if (Exists(frame, recognition, "SelectAccount")) return PuloniaGenshinAccountScreenState.AccountList;
        if (Exists(frame, recognition, "EnterGame") && Exists(frame, recognition, "LoginOther")) return PuloniaGenshinAccountScreenState.AccountSelection;
        if (Exists(frame, recognition, "SwitchAccount")) return PuloniaGenshinAccountScreenState.Gate;
        if (Exists(frame, recognition, "Login")) return PuloniaGenshinAccountScreenState.NoAccount;
        if (Exists(frame, recognition, "Menu")) return PuloniaGenshinAccountScreenState.Menu;
        return Bv.IsInMainUi(frame) ? PuloniaGenshinAccountScreenState.MainUi : PuloniaGenshinAccountScreenState.Unknown;
    }

    /// <summary>只有图像匹配成功才点击按钮，后续识别由调用方等待新帧。</summary>
    private static bool ClickTemplate(ImageRegion frame, PuloniaGenshinAccountRecognition recognition, string name)
    {
        using var region = recognition.Find(frame, name);
        if (!region.IsExist()) return false;
        region.Click();
        return true;
    }

    /// <summary>已核验状态下使用脚本的参考坐标，通过截图区域执行坐标转换。</summary>
    private static void ClickReference(ImageRegion frame, int x, int y)
    {
        var scale = frame.Width / 1920d;
        frame.ClickTo(x * scale, y * scale);
    }

    /// <inheritdoc />
    public async Task<(string Uid, string ImagePath)> CaptureCurrentAccountAsync(GameRuntime runtime, string accountId,
        CancellationToken ct)
    {
        // 先校验账号 ID，避免退出到登录界面后才发现无法保存图片。
        PuloniaTaskValidator.ValidateId(accountId, "account/id");
        EnsureSupportedRuntime(runtime);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(PreparationBudget);
        using var recognition = new PuloniaGenshinAccountRecognition();
        var uid = await ReadStableUidAsync(runtime, null, budget.Token).ConfigureAwait(false)
                  ?? throw new InvalidOperationException("请先登录待采集的账号并返回主界面。");
        await GoToAccountSelectionAsync(runtime, recognition, budget.Token).ConfigureAwait(false);
        using (var frame = runtime.CaptureToRectArea())
            if (Exists(frame, recognition, "SelectAccount")) ClickReference(frame, 957, 404);
        await Task.Delay(ResponseDelayMilliseconds, budget.Token).ConfigureAwait(false);
        using (var frame = runtime.CaptureToRectArea())
        {
            if (!Exists(frame, recognition, "EnterGame") || !Exists(frame, recognition, "LoginOther"))
                throw new InvalidOperationException("账号标识采集需要收起账号列表并显示进入游戏按钮，请返回主界面后重试。");
        }
        // 账号图片统一为 1080p 参考尺寸，运行时按实际截图宽度缩放。
        byte[] png;
        using (var frame = runtime.CaptureToRectArea())
        using (var crop = frame.DeriveCrop(PuloniaGenshinAccountRecognition.Scale(frame, new Rect(780, 481, 150, 27))))
        using (var normalized = new Mat())
        {
            Cv2.Resize(crop.SrcMat, normalized, new Size(150, 27));
            png = normalized.ImEncode(".png");
        }
        // 每次采集都新建 account-images\<账号ID>-<时间>.png，不覆盖已有图片，旧运行快照引用的图片保持不变。
        var relative = await store.CreateAccountImageAsync(accountId, png, budget.Token).ConfigureAwait(false);
        await EnterMainUiAsync(runtime, recognition, budget.Token).ConfigureAwait(false);
        await VerifyAsync(runtime, uid, budget.Token).ConfigureAwait(false);
        return (uid, relative);
    }
}

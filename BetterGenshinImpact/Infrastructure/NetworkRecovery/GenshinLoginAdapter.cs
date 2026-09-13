using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.AutoWood.Utils;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Model.Area;

namespace BetterGenshinImpact.Infrastructure.NetworkRecovery;

public sealed class GenshinLoginAdapter : ILoginAdapter
{
    private static readonly string[] NetworkErrorTexts =
    [
        "连接超时",
        "连接已断开",
        "网络错误",
        "无法登录服务器"
    ];

    private static readonly string[] LoginTexts = ["登录其他账号", "忘记密码", "进入游戏"];

    public string Name => "原神登录适配器";

    public bool CanHandle() => TaskContext.Instance().IsInitialized;

    public Task<LoginScreenState> DetectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var screen = TaskControl.CaptureToRectArea();
        if (Bv.IsInMainUi(screen))
        {
            return Task.FromResult(LoginScreenState.MainUi);
        }

        using var networkError = screen.Find(RecognitionObject.OcrMatch(
            0,
            0,
            screen.Width,
            screen.Height,
            NetworkErrorTexts));
        if (networkError.IsExist())
        {
            return Task.FromResult(LoginScreenState.NetworkError);
        }

        using var loginRequired = screen.Find(RecognitionObject.OcrMatch(
            0,
            0,
            screen.Width,
            screen.Height,
            LoginTexts));
        return Task.FromResult(loginRequired.IsExist()
            ? LoginScreenState.LoginRequired
            : LoginScreenState.Unknown);
    }

    public async Task<bool> ConfirmNetworkErrorAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var screen = TaskControl.CaptureToRectArea();
            var matches = screen.FindMulti(RecognitionObject.Ocr(0, 0, screen.Width, screen.Height));
            try
            {
                foreach (var match in matches)
                {
                    if (match.Text is "确认" or "确定" or "点击进入")
                    {
                        match.Click();
                        await Task.Delay(300, cancellationToken);
                        return true;
                    }
                }
            }
            finally
            {
                foreach (var match in matches)
                {
                    match.Dispose();
                }
            }

            await Task.Delay(500, cancellationToken);
        }

        return false;
    }

    public async Task<bool> ReturnToMainUiAsync(CancellationToken cancellationToken)
    {
        await new ReturnMainUiTask().Start(cancellationToken);
        using var screen = TaskControl.CaptureToRectArea();
        return Bv.IsInMainUi(screen);
    }

    public async Task<bool> ReloginAsync(CancellationToken cancellationToken)
    {
        await new ExitAndReloginJob().Start(cancellationToken);
        using var screen = TaskControl.CaptureToRectArea();
        return Bv.IsInMainUi(screen);
    }
}

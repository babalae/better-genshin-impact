using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OpenCvSharp;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// 账户页中需要本实例游戏的操作：从当前游戏读取 UID、导入或采集账户图片、测试切换。
/// 每个操作都先取得本实例的游戏任务租约，期间禁止其他编辑，可以取消。
/// </summary>
public partial class PuloniaAccountsPageViewModel
{
    /// <summary>持有游戏操作期间的输入所有权。</summary>
    private readonly PuloniaGameTaskCoordinator _coordinator;

    /// <summary>账户切换、UID 读取与图片采集。</summary>
    private readonly IPuloniaGenshinAccountSwitcher _switcher;

    /// <summary>进行中的游戏操作的取消源；没有操作时为 null。</summary>
    private CancellationTokenSource? _operation;

    /// <summary>是否有进行中的游戏操作，界面据此显示“取消”。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelOperationCommand))]
    private bool _isGameOperationRunning;

    /// <summary>
    /// “测试切换”的说明：云原神实例不能切换；账户没有 UID 时不能切换；否则说明会做什么。
    /// </summary>
    public string TestSwitchHint
    {
        get
        {
            if (IsCloudInstance)
                return "云原神实例不能切换账户。";
            if (SelectedAccount is not { } account)
                return "";
            if (string.IsNullOrEmpty(account.Saved?.Uid))
                return "账户没有 UID，不能测试切换。请先填写 UID 并保存。";
            return "在本实例的游戏中切换到这个账户并核验 UID，成功后本实例当前账户改为这个账户。使用已保存的登录资料，修改后请先保存。";
        }
    }

    /// <summary>是否可以对所选账户执行游戏操作。</summary>
    private bool CanRunGameOperation() => CanEdit && SelectedAccount is not null;

    /// <summary>能否测试切换：不是云原神实例，且所选账户已保存并填写了 UID。</summary>
    private bool CanTestSwitch() => CanRunGameOperation() && !IsCloudInstance && !string.IsNullOrEmpty(SelectedAccount?.Saved?.Uid);

    /// <summary>
    /// 从当前游戏读取 UID 填入草稿：只在主界面截图识别，不按键；读不到时提示让游戏停在主界面。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRunGameOperation))]
    private async Task ReadUidAsync()
    {
        if (SelectedAccount is not { } account)
            return;
        await RunGameOperationAsync("正在读取游戏中的 UID…", "未能读取 UID：", async (lease, ct) =>
        {
            if (await _switcher.TryReadUidAsync(lease.Runtime, ct) is not { } uid)
            {
                Message = "没有读到 UID，请让游戏停在主界面后重试。";
                return;
            }
            account.Uid = uid;
            Message = $"已读取 UID {uid}，点“保存”后生效。";
        });
    }

    /// <summary>导入 1080p 登录列表中的账号标识图片，验证可解码后另存为该账户的新 PNG，不覆盖已有图片。</summary>
    [RelayCommand(CanExecute = nameof(CanRunGameOperation))]
    private async Task ImportImageAsync()
    {
        if (SelectedAccount is not { } account || IsBusy)
            return;
        var picker = new OpenFileDialog { Filter = "账户图片|*.png;*.jpg;*.jpeg", Title = "选择 1080p 登录列表中的账户标识裁剪图" };
        if (picker.ShowDialog() != true)
            return;
        IsBusy = true;
        try
        {
            var bytes = await File.ReadAllBytesAsync(picker.FileName);
            using var image = Cv2.ImDecode(bytes, ImreadModes.Color);
            if (image.Empty() || image.Width > 1920 || image.Height > 1080)
                throw new InvalidOperationException("请选择可解码的 1080p 账户标识裁剪图片，或使用“采集当前账户图片”。");
            // 每次导入都新建 account-images\<账户ID>-<时间>.png，已有运行快照引用的旧图片保持不变。
            account.ImagePath = await _store.CreateAccountImageAsync(account.Id, image.ImEncode(".png"));
            Message = "图片已导入，点“保存”后生效。";
        }
        catch (Exception ex) { Message = "图片未导入：" + ex.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>采集前确认会进入登录界面；识别当前 UID 并为所选账户新建账户图片，过程共用游戏任务输入租约。</summary>
    [RelayCommand(CanExecute = nameof(CanRunGameOperation))]
    private async Task CaptureImageAsync()
    {
        if (SelectedAccount is not { } account)
            return;
        if (await ThemedMessageBox.ShowAsync("将从当前已登录的账户识别 UID，退出到登录界面采集账户标识，再返回游戏。请确认当前登录的是要保存的账户。",
                "采集账户图片", MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        await RunGameOperationAsync("正在采集账户图片…", "采集未完成：", async (lease, ct) =>
        {
            var result = await _switcher.CaptureCurrentAccountAsync(lease.Runtime, account.Id, ct);
            account.Uid = result.Uid;
            account.ImagePath = result.ImagePath;
            Message = "采集完成，已返回游戏。点“保存”后生效。";
        });
    }

    /// <summary>
    /// 测试切换：在本实例取得游戏租约，按已保存的登录资料切换到所选账户并核验 UID；成功后把本实例当前账户改为该账户，
    /// 切换开始后失败（含取消）把本实例当前账户改为“未确定”。云原神实例与没有 UID 的账户不可用。
    /// </summary>
    /// <remarks>
    /// 切换期间同样取得目标账户的占用（统筹设计 4.5），账户正在任一实例运行时不切换；
    /// 另一个在线实例的当前账户就是目标账户时（4.6），确认框中说明切换会把它挤下线。
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanTestSwitch))]
    private async Task TestSwitchAsync()
    {
        if (SelectedAccount is not { Saved: { Uid: { Length: > 0 } } saved } account || IsCloudInstance)
            return;
        // 1. 切换按已保存的资料进行，草稿有修改时先保存，避免测试的不是界面上看到的资料
        if (account.IsDirty)
        {
            Message = "这个账户有未保存的修改，请先保存，再测试切换。";
            return;
        }
        // 2. 切换前检查：其他在线实例正在使用这个账户时，切换会把它挤下线，在确认框中一并说明
        IReadOnlyList<string> displaced;
        try { displaced = await _accounts.ListOtherOnlineInstancesUsingAccountAsync(saved.Id); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { displaced = []; System.Diagnostics.Debug.WriteLine(ex); }
        var displacedText = displaced.Count == 0
            ? ""
            : $"\n\n{string.Join("、", displaced.Select(PuloniaInstanceKey.Describe))}的当前账户是“{saved.Name}”且正在线，切换会把它挤下线。";
        if (await ThemedMessageBox.ShowAsync($"将在本实例的游戏中切换到账户“{saved.Name}”（UID {saved.Uid}）并核验 UID，当前登录的账户会被登出。{displacedText}\n\n确定继续吗？",
                "测试切换", MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        // 3. 取得目标账户的占用：账户正在任一实例运行时不切换，避免两处同时使用同一账户
        IDisposable? occupancy;
        PuloniaLockOwner? occupant;
        try { occupancy = _accounts.TryAcquireAccountOccupancy(saved.Id, null, out occupant); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PuloniaTaskValidationException)
        {
            Message = $"无法取得账户“{saved.Name}”的占用，未切换：{ex.Message}";
            return;
        }
        if (occupancy is null)
        {
            Message = $"账户“{saved.Name}”正在{(occupant is null ? "另一个实例" : PuloniaAccountService.DescribeOccupant(occupant))}运行，未切换。";
            return;
        }
        using var heldOccupancy = occupancy;
        // 4. 切换并核验；已离开主界面去登出或登录后失败时，游戏中登录的账户不再确定
        await RunGameOperationAsync($"正在切换到“{saved.Name}”…", "测试切换失败：", async (lease, ct) =>
        {
            var switchStarted = false;
            string uid;
            try
            {
                uid = await _switcher.PrepareAsync(lease.Runtime, saved, () => switchStarted = true, ct);
            }
            catch (Exception ex) when (switchStarted)
            {
                var undetermined = await TrySetCurrentAccountAsync(null);
                Message = (ex is OperationCanceledException ? "测试切换已取消。" : "测试切换失败：" + ex.Message)
                          + (undetermined
                              ? "切换已经开始，本实例当前账户已改为“未确定”，请确认游戏中登录的账户后在顶部重新选择。"
                              : "切换已经开始，但未能把本实例当前账户改为“未确定”，请确认游戏中登录的账户后在顶部重新选择。");
                return;
            }
            // 5. 核验成功后本实例当前账户就是该账户
            Message = await TrySetCurrentAccountAsync(saved.Id)
                ? $"已切换到“{saved.Name}”，UID {uid} 核验通过；本实例当前账户已改为“{saved.Name}”。"
                : $"已切换到“{saved.Name}”，UID {uid} 核验通过，但未能修改本实例当前账户，请在顶部手动选择。";
        });
    }

    /// <summary>
    /// 尽力修改本实例当前账户；使用独立时限，切换被取消时仍要写下“未确定”。
    /// </summary>
    /// <param name="accountId">新的当前账户；为空表示“未确定”</param>
    /// <returns>是否修改成功</returns>
    private async Task<bool> TrySetCurrentAccountAsync(string? accountId)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await _accounts.SetCurrentAccountAsync(_accounts.CurrentInstanceKey, accountId, cleanup.Token);
            SetCurrentAccountIdSilently(accountId);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    /// <summary>取消进行中的游戏操作。</summary>
    [RelayCommand(CanExecute = nameof(IsGameOperationRunning))]
    private void CancelOperation() => _operation?.Cancel();

    /// <summary>
    /// 取得本实例的游戏任务租约后执行操作；期间页面忙碌、可以取消，失败时把原因写入状态栏。
    /// </summary>
    /// <param name="progress">进行中的说明</param>
    /// <param name="failurePrefix">失败说明的前缀</param>
    /// <param name="operation">持有租约时执行的操作</param>
    private async Task RunGameOperationAsync(string progress, string failurePrefix,
        Func<PuloniaGameTaskLease, CancellationToken, Task> operation)
    {
        if (IsBusy)
            return;
        using var cancellation = new CancellationTokenSource();
        _operation = cancellation;
        IsBusy = true;
        IsGameOperationRunning = true;
        Message = progress;
        try
        {
            using var lease = await _coordinator.AcquireAsync(cancellation.Token);
            await operation(lease, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Message = "操作已取消。";
        }
        catch (Exception ex)
        {
            Message = failurePrefix + ex.Message;
        }
        finally
        {
            _operation = null;
            IsGameOperationRunning = false;
            IsBusy = false;
        }
    }

    /// <summary>通知游戏操作命令重新计算可执行状态。</summary>
    private void NotifyGameCommands()
    {
        ReadUidCommand.NotifyCanExecuteChanged();
        ImportImageCommand.NotifyCanExecuteChanged();
        CaptureImageCommand.NotifyCanExecuteChanged();
        TestSwitchCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(TestSwitchHint));
    }
}

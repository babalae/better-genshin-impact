using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.ViewModel.Message;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.Core.Recorder;

/// <summary>录制页、热键与战斗策略控件共用的录制和保存服务。</summary>
public sealed class KeyMouseRecordService
{
    /// <summary>游戏运行环境服务；每次开始时读取当前环境。</summary>
    private readonly GameRuntimeService _runtimeService;
    /// <summary>录制错误日志。</summary>
    private readonly ILogger<KeyMouseRecordService> _logger;
    /// <summary>启动与录制阶段占用标记，阻止不同入口重复启动。</summary>
    private int _isRecording;
    /// <summary>停止过程占用标记，避免多入口重复保存。</summary>
    private int _isStopping;
    /// <summary>本次保存目录。</summary>
    private string? _saveFolder;
    /// <summary>弱引用发起方，录制期间不延长控件的生命周期。</summary>
    private WeakReference<object>? _requester;

    /// <summary>是否正在启动或录制。</summary>
    public bool IsRecording => Volatile.Read(ref _isRecording) != 0;

    /// <summary>创建录制单例服务。</summary>
    public KeyMouseRecordService(GameRuntimeService runtimeService, ILogger<KeyMouseRecordService> logger)
    {
        _runtimeService = runtimeService;
        _logger = logger;
    }

    /// <summary>开始录制，保存子目录必须位于录制根目录内；没有截图器时提示并返回 false。</summary>
    public async Task<bool> StartAsync(string? subFolder = null, object? requester = null)
    {
        if (Interlocked.CompareExchange(ref _isRecording, 1, 0) != 0) return false;
        var started = false;
        try
        {
            if (_runtimeService.Current is not { IsStopping: false } runtime)
            {
                UIDispatcherHelper.Invoke(() => Toast.Warning("请先在启动页，启动截图器再使用本功能"));
                return false;
            }
            var root = Path.GetFullPath(Global.Absolute(@"User\KeyMouseScript"));
            var folder = Path.GetFullPath(Path.Combine(root, subFolder ?? ""));
            if (!folder.Equals(root, StringComparison.OrdinalIgnoreCase)
                && !folder.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("录制保存目录不能超出键鼠脚本目录", nameof(subFolder));
            Directory.CreateDirectory(folder);
            _saveFolder = folder;
            _requester = requester == null ? null : new WeakReference<object>(requester);
            BroadcastState();
            started = await GlobalKeyMouseRecord.Instance.StartRecord(runtime);
            return started;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "启动键鼠录制失败");
            UIDispatcherHelper.Invoke(() => Toast.Error($"启动录制失败：{ex.Message}"));
            return false;
        }
        finally
        {
            if (!started)
            {
                _requester = null;
                _saveFolder = null;
                Interlocked.Exchange(ref _isRecording, 0);
                BroadcastState();
            }
        }
    }

    /// <summary>停止录制并保存；未开始或正在倒计时时返回 null。</summary>
    public string? Stop()
    {
        if (!IsRecording || GlobalKeyMouseRecord.Instance.Status != KeyMouseRecorderStatus.Recording
            || Interlocked.CompareExchange(ref _isStopping, 1, 0) != 0) return null;
        try
        {
            var macro = GlobalKeyMouseRecord.Instance.StopRecord();
            // Genshin Copilot Macro：沿用录制文件命名规则与 UTF-8 无 BOM 编码。
            var path = Path.Combine(_saveFolder!, $"BetterGI_GCM_{DateTime.Now:yyyyMMddHHmmssffff}.json");
            File.WriteAllText(path, macro, new UTF8Encoding(false));
            object? requester = null;
            _requester?.TryGetTarget(out requester);
            UIDispatcherHelper.Invoke(() => WeakReferenceMessenger.Default.Send(new KeyMouseRecordSavedMessage(path, requester)));
            return path;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "停止录制或保存文件失败");
            UIDispatcherHelper.Invoke(() => Toast.Error($"保存录制失败：{ex.Message}"));
            return null;
        }
        finally
        {
            _requester = null;
            _saveFolder = null;
            Interlocked.Exchange(ref _isRecording, 0);
            Interlocked.Exchange(ref _isStopping, 0);
            BroadcastState();
        }
    }

    /// <summary>把热键线程上的状态通知切回 UI 线程。</summary>
    private void BroadcastState()
        => UIDispatcherHelper.Invoke(() => WeakReferenceMessenger.Default.Send(new KeyMouseRecordStateChangedMessage(IsRecording)));
}

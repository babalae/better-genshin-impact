using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using BetterGenshinImpact.Core.Config;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>旧默认 GPT 资源的成套草稿，只有完整有效时才交给宿主自动保存。</summary>
public partial class GptSovitsDefaultVoiceDraft : ObservableObject, IDisposable
{
    /// <summary>兼容原有字段，不迁移存储格式。</summary>
    private readonly QuestVoiceConfig _config;
    /// <summary>本地与远程草稿独立持有，切换不会丢失半成品输入。</summary>
    private readonly bool _remote;
    /// <summary>资产相对路径沿用安装目录，不依赖进程当前目录。</summary>
    private readonly string _programDirectory;
    /// <summary>成套校验防抖，不在每次按键执行文件访问。</summary>
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    /// <summary>已撤销对象的迟到通知不能重新安排写入。</summary>
    private bool _disposed;
    /// <summary>宿主历史名称由两实例共享，只提交用户在本草稿真正编辑的名称。</summary>
    private string _originalName;
    /// <summary>用户显示名称。</summary>
    [ObservableProperty] private string _name;
    /// <summary>GPT 模型路径。</summary>
    [ObservableProperty] private string _gptWeightsPath;
    /// <summary>SoVITS 模型路径。</summary>
    [ObservableProperty] private string _sovitsWeightsPath;
    /// <summary>参考音频路径。</summary>
    [ObservableProperty] private string _referenceAudioPath;
    /// <summary>参考音频逐字原文。</summary>
    [ObservableProperty] private string _promptText;
    /// <summary>只显示保存/校验状态，不回显路径。</summary>
    [ObservableProperty] private string _saveStatus = "沿用原默认声线；修改成套资源后自动校验保存";
    /// <summary>关闭时识别未完成输入，原有不完整旧配置不会无故阻止关闭。</summary>
    public bool HasChanges { get; private set; }

    /// <summary>从对应历史实例初始化，不触发保存或检查远程文件。</summary>
    public GptSovitsDefaultVoiceDraft(QuestVoiceConfig config, bool remote, string programDirectory)
    {
        _config = config; _remote = remote; _programDirectory = programDirectory;
        _originalName = _name = config.VoiceName; _gptWeightsPath = remote ? config.RemoteGptWeightsPath : config.GptWeightsPath;
        _sovitsWeightsPath = remote ? config.RemoteSovitsWeightsPath : config.SovitsWeightsPath;
        _referenceAudioPath = remote ? config.RemoteReferenceAudioPath : config.ReferenceAudioPath;
        _promptText = remote ? config.RemotePromptText : config.PromptText;
        _timer.Tick += (_, _) => TryCommit();
    }
    /// <summary>修改任一资源都保留草稿，等待输入停顿后成套校验。</summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_disposed || e.PropertyName == nameof(SaveStatus)) return;
        HasChanges = true; _timer.Stop(); _timer.Start(); SaveStatus = "等待输入有效的成套资源";
    }
    /// <summary>输入不完整时不改原配置；本地相对路径仍按原程序目录解析。</summary>
    public bool TryCommit()
    {
        _timer.Stop();
        if (_disposed) return true;
        if (!HasChanges) return true;
        try
        {
            if (new[] { Name, GptWeightsPath, SovitsWeightsPath, ReferenceAudioPath, PromptText }.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException();
            if (!_remote && new[] { GptWeightsPath, SovitsWeightsPath, ReferenceAudioPath }
                .Select(p => new FileInfo(Path.GetFullPath(p.Trim(), _programDirectory))).Any(f => !f.Exists || f.Length == 0))
                throw new InvalidOperationException();
            if (Name != _originalName) { _config.VoiceName = Name.Trim(); _originalName = Name; }
            if (_remote)
            {
                _config.RemoteGptWeightsPath = GptWeightsPath.Trim(); _config.RemoteSovitsWeightsPath = SovitsWeightsPath.Trim();
                _config.RemoteReferenceAudioPath = ReferenceAudioPath.Trim(); _config.RemotePromptText = PromptText;
            }
            else
            {
                _config.GptWeightsPath = GptWeightsPath.Trim(); _config.SovitsWeightsPath = SovitsWeightsPath.Trim();
                _config.ReferenceAudioPath = ReferenceAudioPath.Trim(); _config.PromptText = PromptText;
            }
            HasChanges = false; SaveStatus = "有效资源已交给 BetterGI 自动保存 · 下次启动配音生效"; return true;
        }
        catch { SaveStatus = "等待有效名称、台词和资源路径；原配置保留"; return false; }
    }
    /// <summary>撤销或替换草稿时停止旧防抖，避免迟到提交旧输入。</summary>
    public void Dispose() { _disposed = true; _timer.Stop(); }
}

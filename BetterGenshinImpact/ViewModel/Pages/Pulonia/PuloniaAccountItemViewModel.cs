using BetterGenshinImpact.Pulonia.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 账户页中一个账户的编辑草稿：名称、UID、服务器与登录资料都直接在这里编辑，点“保存”时由页面一次保存整个目录。
/// 同时提供列表行的展示文字（UID 尾号、作为哪些实例的当前账户、是否有登录资料）。
/// </summary>
public sealed partial class PuloniaAccountItemViewModel : ObservableObject
{
    /// <summary>账户 ID，草稿与已保存资料共用，不可修改。</summary>
    public string Id { get; }

    /// <summary>最近一次读取或保存的资料；新增且尚未保存的账户为 null。测试切换只使用已保存的资料。</summary>
    public PuloniaGenshinAccountProfile? Saved { get; private set; }

    /// <summary>账户名称草稿。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string _name;

    /// <summary>完整 UID 草稿；留空表示不填 UID（目录中最多一个账户可以不填）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UidText))]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string _uid;

    /// <summary>服务器时区草稿。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private PuloniaTaskServerTimeZone _serverTimeZone;

    /// <summary>登录方式草稿。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private PuloniaGenshinAccountLoginMode _loginMode;

    /// <summary>登录列表中显示的脱敏账号文字草稿。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoginProfile))]
    [NotifyPropertyChangedFor(nameof(LoginProfileText))]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string _listText;

    /// <summary>账户图片相对于 User/Pulonia 的路径草稿。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoginProfile))]
    [NotifyPropertyChangedFor(nameof(LoginProfileText))]
    [NotifyPropertyChangedFor(nameof(ImagePathText))]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string? _imagePath;

    /// <summary>登录用户名草稿，只在内存中，保存时交给存储层写入 Windows 凭据。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoginProfile))]
    [NotifyPropertyChangedFor(nameof(LoginProfileText))]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string _loginUsername = "";

    /// <summary>登录密码草稿，只在内存中，保存时交给存储层写入 Windows 凭据。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoginProfile))]
    [NotifyPropertyChangedFor(nameof(LoginProfileText))]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string _loginPassword = "";

    /// <summary>作为哪些实例的当前账户，如“主实例、云原神 · 小号a”；不是任何实例的当前账户时为空。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInstances))]
    [NotifyPropertyChangedFor(nameof(InstancesDetailText))]
    private string _instancesText = "";

    /// <summary>
    /// 账户正被运行占用时的说明，如“运行中 · 云原神 · 小号a”（统筹设计 9.3）；没有被占用时为空。
    /// 页面显示和刷新时按账户占用文件读取。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    private string _runningText = "";

    /// <summary>
    /// 由已保存的资料建立草稿。
    /// </summary>
    /// <param name="saved">账户目录中的资料</param>
    public PuloniaAccountItemViewModel(PuloniaGenshinAccountProfile saved) : this(saved, isSaved: true)
    {
    }

    /// <summary>
    /// 建立草稿；新增账户传入尚未保存的资料。
    /// </summary>
    /// <param name="profile">账户资料</param>
    /// <param name="isSaved">资料是否已经保存在账户目录中</param>
    public PuloniaAccountItemViewModel(PuloniaGenshinAccountProfile profile, bool isSaved)
    {
        Id = profile.Id;
        Saved = isSaved ? profile : null;
        _name = profile.Name;
        _uid = profile.Uid ?? "";
        _serverTimeZone = profile.ServerTimeZone;
        _loginMode = profile.LoginMode;
        _listText = profile.ListText;
        _imagePath = profile.ImagePath;
    }

    /// <summary>列表中显示的 UID：尾号四位或“未填写 UID”。</summary>
    public string UidText => Uid.Trim() is { Length: > 0 } uid
        ? "UID 尾号 " + (uid.Length <= 4 ? uid : uid[^4..])
        : "未填写 UID";

    /// <summary>是否作为某个实例的当前账户。</summary>
    public bool HasInstances => InstancesText.Length > 0;

    /// <summary>账户是否正被运行占用。</summary>
    public bool IsRunning => RunningText.Length > 0;

    /// <summary>概况页显示的“作为哪些实例的当前账户”。</summary>
    public string InstancesDetailText => HasInstances ? InstancesText : "不是任何实例的当前账户";

    /// <summary>是否有登录资料（账户图片、列表文字或密码），有资料时 BetterGI 才能自动切换到这个账户。</summary>
    public bool HasLoginProfile => !string.IsNullOrWhiteSpace(ImagePath) || !string.IsNullOrWhiteSpace(ListText)
                                   || Saved?.CredentialReference is not null || HasCredentialDraft;

    /// <summary>列表中显示的登录资料状态。</summary>
    public string LoginProfileText => HasLoginProfile ? "有登录资料，可自动切换" : "没有登录资料";

    /// <summary>账户图片路径的显示文字。</summary>
    public string ImagePathText => string.IsNullOrWhiteSpace(ImagePath) ? "未设置" : ImagePath;

    /// <summary>是否填写了新的登录凭据草稿。</summary>
    public bool HasCredentialDraft => LoginUsername.Length > 0 || LoginPassword.Length > 0;

    /// <summary>草稿与已保存资料是否不同；新增账户、填写了凭据草稿时都算有未保存的修改。</summary>
    public bool IsDirty => Saved is not { } saved
                           || Name.Trim() != saved.Name.Trim()
                           || NormalizedUid != saved.Uid
                           || ServerTimeZone != saved.ServerTimeZone
                           || LoginMode != saved.LoginMode
                           || ListText.Trim() != saved.ListText.Trim()
                           || ImagePath != saved.ImagePath
                           || HasCredentialDraft;

    /// <summary>规范化后的 UID：去掉首尾空白，留空为 null。</summary>
    private string? NormalizedUid => string.IsNullOrWhiteSpace(Uid) ? null : Uid.Trim();

    /// <summary>
    /// 把草稿转换为要保存的资料；凭据引用沿用已保存的值，由存储层按本次凭据草稿和登录方式重新确定。
    /// </summary>
    /// <returns>新的资料对象，不修改已保存的资料</returns>
    public PuloniaGenshinAccountProfile ToProfile() => new()
    {
        Id = Id,
        Name = Name.Trim(),
        Uid = NormalizedUid,
        ServerTimeZone = ServerTimeZone,
        LoginMode = LoginMode,
        ListText = ListText.Trim(),
        ImagePath = ImagePath,
        CredentialReference = Saved?.CredentialReference
    };

    /// <summary>
    /// 保存成功后用保存结果更新草稿基准。
    /// </summary>
    /// <param name="saved">刚保存的资料</param>
    /// <param name="keepCredentialDraft">凭据未能写入时保留密码草稿，用户可直接再次保存重试</param>
    public void MarkSaved(PuloniaGenshinAccountProfile saved, bool keepCredentialDraft)
    {
        Saved = saved;
        Name = saved.Name;
        Uid = saved.Uid ?? "";
        ListText = saved.ListText;
        if (!keepCredentialDraft)
        {
            LoginUsername = "";
            LoginPassword = "";
        }
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(HasLoginProfile));
        OnPropertyChanged(nameof(LoginProfileText));
    }

    /// <summary>清空内存中的密码草稿，离开页面或重新读取时调用。</summary>
    public void ClearCredentialDraft()
    {
        LoginUsername = "";
        LoginPassword = "";
    }
}

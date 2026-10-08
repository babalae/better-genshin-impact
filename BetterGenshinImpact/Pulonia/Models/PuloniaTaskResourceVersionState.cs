namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 一次资源检查得到的本机仓库状态；声明版本仅用于展示，内容指纹仍决定是否存在更新。
/// </summary>
public sealed class PuloniaTaskResourceVersionState
{
    /// <summary>本机仓库当前资源的内容指纹；纯任务没有外部资源时为 null。</summary>
    public string? CurrentContentVersion { get; }

    /// <summary>本机仓库当前 Git 提交或文件式来源版本；旧路径资源为 null。</summary>
    public string? CurrentRepositoryRevision { get; }

    /// <summary>任务使用版本在资源正文中声明的版本号；无法读取或没有声明时为 null。</summary>
    public string? ApprovedDeclaredVersion { get; }

    /// <summary>本机仓库当前资源在正文中声明的版本号；无法读取或没有声明时为 null。</summary>
    public string? CurrentDeclaredVersion { get; }

    /// <summary>建立一份不可变资源检查结果。</summary>
    public PuloniaTaskResourceVersionState(string? currentContentVersion, string? currentRepositoryRevision,
        string? approvedDeclaredVersion, string? currentDeclaredVersion)
    {
        CurrentContentVersion = currentContentVersion;
        CurrentRepositoryRevision = currentRepositoryRevision;
        ApprovedDeclaredVersion = approvedDeclaredVersion;
        CurrentDeclaredVersion = currentDeclaredVersion;
    }
}

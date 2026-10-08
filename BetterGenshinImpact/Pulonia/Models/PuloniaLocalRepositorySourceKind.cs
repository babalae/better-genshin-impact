namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>本地仓库添加入口识别出的来源类型，决定直接注册还是导入离线包。</summary>
public enum PuloniaLocalRepositorySourceKind
{
    /// <summary>直接引用用户维护的仓库根目录。</summary>
    Directory,
    /// <summary>校验并解压到共享 Repos 目录的 ZIP 仓库包。</summary>
    Zip
}

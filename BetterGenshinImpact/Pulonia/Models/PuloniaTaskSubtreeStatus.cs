namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 一次运行中某个节点子树（例如一条龙清单的一行）的汇总状态，只用于展示，不持久化、不参与执行判断。
/// </summary>
public enum PuloniaTaskSubtreeStatus
{
    /// <summary>没有相关运行或本子树尚无结果。</summary>
    NotRun,

    /// <summary>运行仍在执行本子树内的节点。</summary>
    Running,

    /// <summary>子树内存在失败或超时的尝试。</summary>
    Failed,

    /// <summary>子树内存在待处理或已执行未核验的尝试，需要核对游戏状态。</summary>
    NeedsVerification,

    /// <summary>子树内全部尝试均已执行完成。</summary>
    Completed,

    /// <summary>有结果但既未全部完成也未失败，例如被跳过或停止。</summary>
    SkippedOrStopped
}

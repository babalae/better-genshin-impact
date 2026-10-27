namespace BetterGenshinImpact.ViewModel.Controls;

/// <summary>人工操作后的局部刷新方式，不代表持久化或权限策略。</summary>
internal enum AccountRecordsRefreshKind
{
    /// <summary>更新内容，保留行顺序和图像。</summary>
    Content,
    /// <summary>内容更新后重新加载图像。</summary>
    Icons,
    /// <summary>仅重新分配置顶组，其余排序键保持不变。</summary>
    Prior
}

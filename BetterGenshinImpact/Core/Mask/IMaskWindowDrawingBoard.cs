using System;
using System.Collections.Generic;
using System.Threading;

namespace BetterGenshinImpact.Core.Mask;

/// <summary>
/// 遮罩窗口绘制入口（业务侧）。
/// 线程安全，所有方法只写数据并立即返回，不等待 UI；何时重绘由遮罩窗口自行决定。
/// </summary>
public interface IMaskWindowDrawingBoard
{
    /// <summary>
    /// 整组替换。shapes 为 null 或空时等价于 <see cref="Clear"/>；内容与当前相同则不做任何事
    /// </summary>
    void Set(MaskWindowDrawingGroup group, IReadOnlyList<MaskWindowDrawingShape>? shapes);

    void Clear(MaskWindowDrawingGroup group);

    void ClearAll();
}

public static class MaskWindowDrawingBoardExtensions
{
    public static void Set(this IMaskWindowDrawingBoard board, MaskWindowDrawingGroup group, MaskWindowDrawingShape shape)
    {
        board.Set(group, new[] { shape });
    }

    /// <summary>
    /// 返回一个作用域，Dispose 时清除该分组。用于"任务期间显示、结束即清"
    /// </summary>
    public static IDisposable Scope(this IMaskWindowDrawingBoard board, MaskWindowDrawingGroup group)
    {
        return new MaskWindowDrawingScope(board, group);
    }

    private sealed class MaskWindowDrawingScope(IMaskWindowDrawingBoard board, MaskWindowDrawingGroup group) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                board.Clear(group);
            }
        }
    }
}

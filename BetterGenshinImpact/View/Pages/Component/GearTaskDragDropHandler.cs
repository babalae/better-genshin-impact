using System.Windows;
using GongSolutions.Wpf.DragDrop;
using BetterGenshinImpact.ViewModel.Pages.Component;

namespace BetterGenshinImpact.View.Pages.Component;

/// <summary>
/// 齿轮任务拖拽处理器，实现拖拽时的验证逻辑
/// </summary>
public class GearTaskDragDropHandler : IDropTarget
{
    /// <summary>
    /// 静态实例，用于XAML绑定
    /// </summary>
    public static GearTaskDragDropHandler Instance { get; } = new GearTaskDragDropHandler();
    public void DragOver(IDropInfo dropInfo)
    {
        // 获取拖拽的源数据和目标数据
        var sourceItem = dropInfo.Data as GearTaskViewModel;
        var targetItem = dropInfo.TargetItem as GearTaskViewModel;

        // 如果源数据不是GearTaskViewModel，不允许拖拽
        if (sourceItem == null)
        {
            dropInfo.Effects = DragDropEffects.None;
            return;
        }

        // 如果目标是任务组，允许拖拽到其下
        if (targetItem != null && targetItem.IsDirectory)
        {
            dropInfo.Effects = DragDropEffects.Move;
            dropInfo.DropTargetAdorner = DropTargetAdorners.Highlight;
            return;
        }

        // 如果目标是任务节点（非任务组），允许在同级之间调整顺序
        if (targetItem != null && !targetItem.IsDirectory)
        {
            // 检查插入位置，允许在节点之间插入
            if (dropInfo.InsertPosition.HasFlag(RelativeInsertPosition.BeforeTargetItem) ||
                dropInfo.InsertPosition.HasFlag(RelativeInsertPosition.AfterTargetItem))
            {
                dropInfo.Effects = DragDropEffects.Move;
                dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
                return;
            }
        }

        // 如果是拖拽到根节点，允许拖拽
        if (targetItem == null)
        {
            dropInfo.Effects = DragDropEffects.Move;
            dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
            return;
        }

        // 其他情况不允许拖拽
        dropInfo.Effects = DragDropEffects.None;
    }

    public void Drop(IDropInfo dropInfo)
    {
        // 获取拖拽的源数据和目标数据
        var sourceItem = dropInfo.Data as GearTaskViewModel;
        var targetItem = dropInfo.TargetItem as GearTaskViewModel;

        // 如果源数据不是GearTaskViewModel，不执行拖拽
        if (sourceItem == null)
        {
            return;
        }

        // 如果目标是任务组，允许拖拽到其下
        if (targetItem != null && targetItem.IsDirectory)
        {
            GongSolutions.Wpf.DragDrop.DragDrop.DefaultDropHandler.Drop(dropInfo);
            return;
        }

        // 如果目标是任务节点（非任务组），允许在同级之间调整顺序
        if (targetItem != null && !targetItem.IsDirectory)
        {
            // 检查插入位置，允许在节点之间插入
            if (dropInfo.InsertPosition.HasFlag(RelativeInsertPosition.BeforeTargetItem) ||
                dropInfo.InsertPosition.HasFlag(RelativeInsertPosition.AfterTargetItem))
            {
                GongSolutions.Wpf.DragDrop.DragDrop.DefaultDropHandler.Drop(dropInfo);
                return;
            }
        }

        // 如果是拖拽到根节点，允许拖拽
        if (targetItem == null)
        {
            GongSolutions.Wpf.DragDrop.DragDrop.DefaultDropHandler.Drop(dropInfo);
            return;
        }
    }
}
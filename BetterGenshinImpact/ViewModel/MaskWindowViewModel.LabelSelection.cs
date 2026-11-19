using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace BetterGenshinImpact.ViewModel
{
    /// <summary>
    /// 遮罩窗口：标点分类网格中高亮已选中的分类，方便确认哪些正在地图上显示
    /// </summary>
    public partial class MaskWindowViewModel
    {
        /// <summary>
        /// 订阅当前的网格列表与已选列表
        /// </summary>
        private void InitMapLabelSelectionHighlight()
        {
            MapLabelItems.CollectionChanged += OnMapLabelSelectionSourceChanged;
            SelectedMapLabelItems.CollectionChanged += OnMapLabelSelectionSourceChanged;
            RefreshMapLabelSelectionHighlight();
        }

        /// <summary>
        /// 网格列表被整体替换时改为订阅新列表
        /// </summary>
        partial void OnMapLabelItemsChanged(ObservableCollection<MapLabelItemVm>? oldValue, ObservableCollection<MapLabelItemVm> newValue)
        {
            if (oldValue != null)
            {
                oldValue.CollectionChanged -= OnMapLabelSelectionSourceChanged;
            }

            newValue.CollectionChanged += OnMapLabelSelectionSourceChanged;
            RefreshMapLabelSelectionHighlight();
        }

        /// <summary>
        /// 已选列表被整体替换时改为订阅新列表
        /// </summary>
        partial void OnSelectedMapLabelItemsChanged(ObservableCollection<MapLabelItemVm>? oldValue, ObservableCollection<MapLabelItemVm> newValue)
        {
            if (oldValue != null)
            {
                oldValue.CollectionChanged -= OnMapLabelSelectionSourceChanged;
            }

            newValue.CollectionChanged += OnMapLabelSelectionSourceChanged;
            RefreshMapLabelSelectionHighlight();
        }

        /// <summary>
        /// 任一列表内容变化时重新计算高亮
        /// </summary>
        private void OnMapLabelSelectionSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RefreshMapLabelSelectionHighlight();
        }

        /// <summary>
        /// 按已选列表刷新网格项的选中状态
        /// </summary>
        private void RefreshMapLabelSelectionHighlight()
        {
            MapLabelSelection.Apply(MapLabelItems, SelectedMapLabelItems);
        }
    }
}

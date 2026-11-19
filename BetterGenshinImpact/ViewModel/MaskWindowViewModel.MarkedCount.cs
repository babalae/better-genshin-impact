using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BetterGenshinImpact.Model.MaskMap;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.ViewModel
{
    /// <summary>
    /// 遮罩窗口：标点分类网格显示“已标记/总数”。
    /// 打开某个大类时在后台加载该大类的全部点位（接口结果有缓存），再按已标记集合统计。
    /// </summary>
    public partial class MaskWindowViewModel
    {
        /// <summary>
        /// 当前已加载点位的大类；用于避免重复加载
        /// </summary>
        private MapLabelCategoryVm? _markedCountCategory;

        /// <summary>
        /// 当前大类下的全部点位
        /// </summary>
        private IReadOnlyList<MaskMapPoint> _markedCountPoints = Array.Empty<MaskMapPoint>();

        /// <summary>
        /// 加载版本号，切换大类后丢弃旧的加载结果
        /// </summary>
        private int _markedCountLoadVersion;

        /// <summary>
        /// 加载大类的全部点位并统计已标记数量；同一大类只加载一次
        /// </summary>
        /// <param name="category">当前大类</param>
        private async Task LoadCategoryMarkedCountsAsync(MapLabelCategoryVm? category)
        {
            if (category == null || ReferenceEquals(category, _markedCountCategory))
            {
                return;
            }

            var version = Interlocked.Increment(ref _markedCountLoadVersion);
            _markedCountCategory = category;
            _markedCountPoints = Array.Empty<MaskMapPoint>();

            try
            {
                var labels = category.Items.Select(x => x.ToModel()).ToList();
                var result = await _maskMapPointService.GetPointsAsync(labels, CancellationToken.None);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (version != _markedCountLoadVersion)
                    {
                        return;
                    }

                    _markedCountPoints = result.Points.ToList();
                    RefreshMapLabelMarkedCounts();
                }, DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                // 计数只是辅助信息，失败时保持只显示总数，下次打开该大类再试
                if (version == _markedCountLoadVersion)
                {
                    _markedCountCategory = null;
                }

                _logger.LogDebug(ex, "加载分类点位用于统计已标记数量失败");
            }
        }

        /// <summary>
        /// 按当前已标记集合重新统计大类下各分类的已标记数量
        /// </summary>
        private void RefreshMapLabelMarkedCounts()
        {
            if (_markedCountCategory == null)
            {
                return;
            }

            MapLabelMarkedCount.Apply(_markedCountCategory.Items, _markedCountPoints, IsMapPointHidden);
        }
    }
}

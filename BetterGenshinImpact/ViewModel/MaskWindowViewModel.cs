using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Controls.Overlay;
using BetterGenshinImpact.GameTask.MapMask;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using PresentMonFps;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.Model.MaskMap;
using Vanara.PInvoke;
using MaskMapPoint = BetterGenshinImpact.Model.MaskMap.MaskMapPoint;
using MaskMapPointLabel = BetterGenshinImpact.Model.MaskMap.MaskMapPointLabel;
using Rect = System.Windows.Rect;

namespace BetterGenshinImpact.ViewModel
{
    public partial class MaskWindowViewModel : ObservableRecipient
    {
        private readonly ILogger<MaskWindowViewModel> _logger = App.GetLogger<MaskWindowViewModel>();

        [ObservableProperty] private Rect _windowRect;

        [ObservableProperty] private ObservableCollection<StatusItem> _statusList = [];

        public AllConfig? Config { get; set; }

        [ObservableProperty] private string _fps = "0";
        
        [ObservableProperty] private double _maskWindowWidth;

        [ObservableProperty] private double _maskWindowHeight;

        [ObservableProperty] private bool _isInBigMapUi;

        [ObservableProperty] private bool _isMapPointPickerOpen;

        [ObservableProperty] private bool _isMapLabelTreeLoading;

        [ObservableProperty] private bool _isMapLabelItemsLoading;

        [ObservableProperty] private string _mapLabelSearchText = string.Empty;

        [ObservableProperty] private ObservableCollection<MapLabelCategoryVm> _mapLabelCategories = [];

        [ObservableProperty] private ObservableCollection<MapLabelItemVm> _mapLabelItems = [];

        [ObservableProperty] private MapLabelCategoryVm? _selectedMapLabelCategory;

        [ObservableProperty] private ObservableCollection<MapLabelItemVm> _selectedMapLabelItems = [];

        [ObservableProperty] private ObservableCollection<MaskMapPoint> _mapPoints = [];

        [ObservableProperty] private ObservableCollection<MaskMapPointLabel> _mapPointLabels = [];

        [ObservableProperty] private bool _isMapPointsLoading;

        [ObservableProperty] private string _mapPointsLoadingText = "正在加载点位...";

        public double MiniMapOverlayLeftRatio => MapAssets.MimiMapRect1080P.X / 1920d;

        public double MiniMapOverlayTopRatio => MapAssets.MimiMapRect1080P.Y / 1080d;

        public double MiniMapOverlaySizeRatio => MapAssets.MimiMapRect1080P.Width / 1080d;

        public sealed record MapPointApiProviderOption(MapPointApiProvider Provider, string DisplayName);

        public IReadOnlyList<MapPointApiProviderOption> MapPointApiProviderOptions { get; } =
        [
            new(MapPointApiProvider.MihoyoMap, "米游社大地图"),
            new(MapPointApiProvider.KongyingTavern, "空荧酒馆")
        ];

        [ObservableProperty] private MapPointApiProviderOption? _selectedMapPointApiProviderOption;

        public MaskMapPointInfoPopupViewModel PointInfoPopup { get; } = new();

        private bool _isMapLabelTreeLoaded;
        private int _mapLabelTreeLoadVersion;
        private CancellationTokenSource? _mapLabelItemsCts;
        private CancellationTokenSource? _mapPointListCts;
        private int _mapLabelItemsLoadVersion;
        private int _mapPointsLoadVersion;
        private readonly SemaphoreSlim _iconLoadSemaphore = new(10, 10);

        public MaskWindowViewModel()
        {
            WeakReferenceMessenger.Default.Register<PropertyChangedMessage<object>>(this, (sender, msg) =>
            {
                if (msg.PropertyName == "RefreshSettings")
                {
                    UIDispatcherHelper.Invoke(RefreshSettings);
                }
            });
        }

        private void InitializeStatusList()
        {
            if (Config != null)
            {
                StatusList.Add(new StatusItem("\uf256 拾取", Config.AutoPickConfig));
                StatusList.Add(new StatusItem("\uf075 剧情", Config.AutoSkipConfig));
                StatusList.Add(new StatusItem("\ue5c8 邀约", Config.AutoSkipConfig, "AutoHangoutEventEnabled"));
                StatusList.Add(new StatusItem("\uf578 钓鱼", Config.AutoFishingConfig));
                StatusList.Add(new StatusItem("\uf3c5 传送", Config.QuickTeleportConfig));
            }
        }

        [RelayCommand]
        private void OnLoaded()
        {
            RefreshSettings();
            InitializeStatusList();
            InitFps();
        }

        [RelayCommand]
        private async Task ToggleMapPointPickerAsync()
        {
            if (!IsInBigMapUi)
            {
                IsMapPointPickerOpen = false;
                return;
            }

            IsMapPointPickerOpen = !IsMapPointPickerOpen;
            if (IsMapPointPickerOpen)
            {
                await EnsureLabelTreeLoadedAsync();
            }
        }

        [RelayCommand(AllowConcurrentExecutions = true)]
        private async Task SelectMapLabelCategory(MapLabelCategoryVm? category)
        {
            SelectedMapLabelCategory = category;
            await StartPopulateRightListAsync(category, MapLabelSearchText);
        }

        [RelayCommand]
        private async Task SelectMapLabelItem(MapLabelItemVm? item)
        {
            if (item == null)
            {
                return;
            }

            var existing = SelectedMapLabelItems.FirstOrDefault(x => x.Id == item.Id);
            if (existing != null)
            {
                SelectedMapLabelItems.Remove(existing);
            }
            else
            {
                SelectedMapLabelItems.Add(item);
                await EnsureIconLoadedAsync(item, CancellationToken.None);
            }

            StartRefreshSelectedMapPoints();
        }

        [RelayCommand]
        private void ResetSelectedMapLabelSelection()
        {
            if (SelectedMapLabelItems.Count == 0)
            {
                return;
            }

            SelectedMapLabelItems.Clear();
            StartRefreshSelectedMapPoints();
        }

        private void RefreshSettings()
        {
            InitConfig();
            if (Config != null)
            {
                OnPropertyChanged(nameof(Config));
            }

            SyncSelectedMapPointApiProviderFromConfig();
        }

        /// <summary>
        /// 这个窗口比较特殊，无法直接使用构造函数依赖注入
        /// </summary>
        private void InitConfig()
        {
            if (Config == null)
            {
                var configService = App.GetService<IConfigService>();
                if (configService != null)
                {
                    Config = configService.Get();
                }
            }
        }

        private void SyncSelectedMapPointApiProviderFromConfig()
        {
            var provider = TaskContext.Instance().Config.MapMaskConfig.MapPointApiProvider;
            SelectedMapPointApiProviderOption = MapPointApiProviderOptions.FirstOrDefault(x => x.Provider == provider)
                                                ?? MapPointApiProviderOptions.FirstOrDefault();
        }

        partial void OnSelectedMapPointApiProviderOptionChanged(MapPointApiProviderOption? value)
        {
            if (value == null)
            {
                return;
            }

            _ = SwitchMapPointApiProviderAsync(value.Provider);
        }

        private async Task SwitchMapPointApiProviderAsync(MapPointApiProvider provider)
        {
            try
            {
                var mapMaskConfig = TaskContext.Instance().Config.MapMaskConfig;
                if (mapMaskConfig.MapPointApiProvider == provider)
                {
                    return;
                }

                mapMaskConfig.MapPointApiProvider = provider;
                if (Config != null)
                {
                    Config.MapMaskConfig.MapPointApiProvider = provider;
                }

                await ResetAndReloadMapPointPickerAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "切换地图点位来源时发生异常");
            }
        }

        private async Task ResetAndReloadMapPointPickerAsync()
        {
            _mapLabelItemsCts?.Cancel();
            _mapPointListCts?.Cancel();
            PointInfoPopup.Close();

            Interlocked.Increment(ref _mapLabelTreeLoadVersion);
            _isMapLabelTreeLoaded = false;
            MapLabelSearchText = string.Empty;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                SelectedMapLabelItems.Clear();
                SelectedMapLabelCategory = null;
                MapLabelCategories = [];
                MapLabelItems = [];
                MapPointLabels = [];
                MapPoints = [];
            }, DispatcherPriority.Background);

            if (IsMapPointPickerOpen)
            {
                await EnsureLabelTreeLoadedAsync();
            }
        }

        private void InitFps()
        {
            if (Config!.MaskWindowConfig.ShowFps)
            {
                nint targetHWnd = TaskContext.Instance().GameHandle;
                _ = User32.GetWindowThreadProcessId(targetHWnd, out var pid);
                Task.Run(async () => { await FpsInspector.StartForeverAsync(new FpsRequest(pid), (result) => { Fps = $"{result.Fps:0}"; }); });
            }
        }

        [RelayCommand]
        private void OnOverlayLayoutCommitted(OverlayLayoutCommittedEventArgs args)
        {
            if (Config == null)
            {
                return;
            }

            if (args.Width <= 0 || args.Height <= 0)
            {
                return;
            }

            if (MaskWindowWidth <= 0 || MaskWindowHeight <= 0)
            {
                return;
            }

            var leftRatio = ToRatio(args.Left, MaskWindowWidth);
            var topRatio = ToRatio(args.Top, MaskWindowHeight);
            var widthRatio = ToRatio(args.Width, MaskWindowWidth);
            var heightRatio = ToRatio(args.Height, MaskWindowHeight);

            switch (args.LayoutKey)
            {
                case "LogTextBox":
                    Config.MaskWindowConfig.LogTextBoxLeftRatio = leftRatio;
                    Config.MaskWindowConfig.LogTextBoxTopRatio = topRatio;
                    Config.MaskWindowConfig.LogTextBoxWidthRatio = widthRatio;
                    Config.MaskWindowConfig.LogTextBoxHeightRatio = heightRatio;
                    break;
                case "StatusList":
                    Config.MaskWindowConfig.StatusListLeftRatio = leftRatio;
                    Config.MaskWindowConfig.StatusListTopRatio = topRatio;
                    Config.MaskWindowConfig.StatusListWidthRatio = widthRatio;
                    Config.MaskWindowConfig.StatusListHeightRatio = heightRatio;
                    break;
            }
        }

        [RelayCommand]
        private void OnWindowSizeChanged(SizeChangedEventArgs args)
        {
            MaskWindowWidth = args.NewSize.Width;
            MaskWindowHeight = args.NewSize.Height;
        }

        [RelayCommand]
        private void OnExitOverlayLayoutEditMode()
        {
            if (Config == null)
            {
                return;
            }

            Config.MaskWindowConfig.OverlayLayoutEditEnabled = false;
            SystemControl.ActivateWindow();
        }

        private static double ToRatio(double value, double baseSize)
        {
            if (double.IsNaN(value) || double.IsNaN(baseSize) || baseSize <= 0)
            {
                return 0;
            }

            var ratio = value / baseSize;
            return ratio switch
            {
                < 0 => 0,
                > 1 => 1,
                _ => ratio
            };
        }

        partial void OnIsInBigMapUiChanged(bool value)
        {
            if (!value)
            {
                IsMapPointPickerOpen = false;
                PointInfoPopup.Close();
            }
        }

        partial void OnMapLabelSearchTextChanged(string value)
        {
            _ = StartPopulateRightListAsync(SelectedMapLabelCategory, value);
        }

        private async Task EnsureLabelTreeLoadedAsync()
        {
            if (_isMapLabelTreeLoaded || IsMapLabelTreeLoading)
            {
                return;
            }

            var loadVersion = _mapLabelTreeLoadVersion;
            IsMapLabelTreeLoading = true;
            try
            {
                var service = App.GetService<IMaskMapPointService>();
                if (service == null)
                {
                    MapLabelCategories = [];
                    MapLabelItems = [];
                    SelectedMapLabelCategory = null;
                    return;
                }

                var categories = await service.GetLabelCategoriesAsync();
                if (loadVersion != _mapLabelTreeLoadVersion)
                {
                    return;
                }
                if (categories.Count == 0)
                {
                    MapLabelCategories = [];
                    MapLabelItems = [];
                    SelectedMapLabelCategory = null;
                    return;
                }

                var vms = categories.Select(x => new MapLabelCategoryVm(x)).ToList();
                MapLabelCategories = new ObservableCollection<MapLabelCategoryVm>(vms);
                await SelectMapLabelCategory(MapLabelCategories.FirstOrDefault());
                _isMapLabelTreeLoaded = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "加载地图点位树时发生异常");
            }
            finally
            {
                IsMapLabelTreeLoading = false;
                // if (!_isMapLabelTreeLoaded && IsMapPointPickerOpen && loadVersion != _mapLabelTreeLoadVersion)
                // {
                //     _ = EnsureLabelTreeLoadedAsync();
                // }
            }
        }

        private void StartRefreshSelectedMapPoints()
        {
            var loadVersion = Interlocked.Increment(ref _mapPointsLoadVersion);
            _mapPointListCts?.Cancel();
            _mapPointListCts = new CancellationTokenSource();
            var ct = _mapPointListCts.Token;
            _ = RefreshSelectedMapPointsAsync(loadVersion, ct);
        }

        private async Task RefreshSelectedMapPointsAsync(int loadVersion, CancellationToken ct)
        {
            try
            {
                if (loadVersion == _mapPointsLoadVersion)
                {
                    IsMapPointsLoading = true;
                }

                var selectedItems = await Application.Current.Dispatcher.InvokeAsync(
                    () => SelectedMapLabelItems.ToList(),
                    DispatcherPriority.Background);
                ct.ThrowIfCancellationRequested();
                if (selectedItems.Count == 0)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MapPointLabels = [];
                        MapPoints = [];
                    }, DispatcherPriority.Background);
                    return;
                }

                var service = App.GetService<IMaskMapPointService>();
                if (service == null)
                {
                    return;
                }

                var selectedModels = selectedItems.Select(x => x.ToModel()).ToList();
                var result = await service.GetPointsAsync(selectedModels, ct);
                ct.ThrowIfCancellationRequested();

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MapPointLabels = new ObservableCollection<MaskMapPointLabel>(result.Labels);
                    MapPoints = new ObservableCollection<MaskMapPoint>(result.Points);
                }, DispatcherPriority.Background);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "刷新地图点位列表时发生异常");
            }
            finally
            {
                if (loadVersion == _mapPointsLoadVersion)
                {
                    IsMapPointsLoading = false;
                }
            }
        }

        private Task StartPopulateRightListAsync(MapLabelCategoryVm? category, string? searchText)
        {
            var loadVersion = Interlocked.Increment(ref _mapLabelItemsLoadVersion);
            _mapLabelItemsCts?.Cancel();
            _mapLabelItemsCts?.Dispose();
            _mapLabelItemsCts = new CancellationTokenSource();
            var ct = _mapLabelItemsCts.Token;
            return PopulateRightListAsync(loadVersion, category, searchText, ct);
        }

        private async Task PopulateRightListAsync(int loadVersion, MapLabelCategoryVm? category, string? searchText, CancellationToken ct)
        {
            try
            {
                if (loadVersion == _mapLabelItemsLoadVersion)
                {
                    IsMapLabelItemsLoading = true;
                }
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (loadVersion == _mapLabelItemsLoadVersion)
                    {
                        MapLabelItems = [];
                    }
                }, DispatcherPriority.Background);

                if (category?.Items == null)
                {
                    return;
                }

                var src = category.Items.AsEnumerable();
                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    var q = searchText.Trim();
                    src = src.Where(x => x.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
                }

                const int batchSize = 24;
                var batch = new List<MapLabelItemVm>(batchSize);

                foreach (var item in src)
                {
                    ct.ThrowIfCancellationRequested();
                    if (loadVersion != _mapLabelItemsLoadVersion)
                    {
                        return;
                    }
                    batch.Add(item);
                    if (batch.Count < batchSize)
                    {
                        continue;
                    }

                    var snapshot = batch.ToArray();
                    batch.Clear();
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (loadVersion != _mapLabelItemsLoadVersion)
                        {
                            return;
                        }
                        foreach (var it in snapshot)
                        {
                            MapLabelItems.Add(it);
                        }
                    }, DispatcherPriority.Background);

                    foreach (var it in snapshot)
                    {
                        _ = EnsureIconLoadedAsync(it, ct);
                    }

                    await Task.Delay(1, ct);
                }

                if (batch.Count > 0)
                {
                    var snapshot = batch.ToArray();
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (loadVersion != _mapLabelItemsLoadVersion)
                        {
                            return;
                        }
                        foreach (var it in snapshot)
                        {
                            MapLabelItems.Add(it);
                        }
                    }, DispatcherPriority.Background);

                    foreach (var it in snapshot)
                    {
                        _ = EnsureIconLoadedAsync(it, ct);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (loadVersion == _mapLabelItemsLoadVersion)
                {
                    IsMapLabelItemsLoading = false;
                }
            }
        }

        private async Task EnsureIconLoadedAsync(MapLabelItemVm item, CancellationToken ct)
        {
            if (item.IconImage != null || string.IsNullOrEmpty(item.IconUrl))
            {
                return;
            }


            try
            {
                await _iconLoadSemaphore.WaitAsync(ct);
                try
                {
                    await item.LoadIconAsync(ct);
                }
                finally
                {
                    _iconLoadSemaphore.Release();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "加载地图点位图标失败");
            }
        }

        [RelayCommand]
        private async Task OnPointClick(MaskMapPointClickArgs? args)
        {
            var point = args?.Point;
            if (point == null || !IsInBigMapUi)
            {
                return;
            }
            await PointInfoPopup.ShowAsync(point, args!.AnchorPosition, ResolvePointTitle(point));
        }

        private string ResolvePointTitle(MaskMapPoint point)
        {
            var labelName = MapPointLabels.FirstOrDefault(x => x.LabelId == point.LabelId)?.Name;
            if (!string.IsNullOrWhiteSpace(labelName))
            {
                return labelName;
            }

            return $"点位 {point.Id}";
        }

        [RelayCommand]
        private Task OnPointRightClick(MaskMapPoint? point)
        {
            if (point != null)
            {
                // 自定义右键逻辑
            }

            return Task.CompletedTask;
        }

        [RelayCommand]
        private Task OnPointHover(MaskMapPoint? point)
        {
            if (point != null)
            {
                // 悬停逻辑
            }

            return Task.CompletedTask;
        }
    }

    public partial class MapLabelCategoryVm : ObservableObject
    {
        public string Id { get; }
        public string Name { get; }
        public string IconUrl { get; }
        public ObservableCollection<MapLabelItemVm> Items { get; }

        public MapLabelCategoryVm(MaskMapPointLabel category)
        {
            Id = category.LabelId;
            Name = category.Name;
            IconUrl = category.IconUrl;
            Items = new ObservableCollection<MapLabelItemVm>((category.Children ?? Array.Empty<MaskMapPointLabel>()).Select(x => new MapLabelItemVm(x)));
        }
    }

    public partial class MapLabelItemVm : ObservableObject
    {
        public string Id { get; }
        public IReadOnlyList<string> LabelIds { get; }
        public string Name { get; }
        public string IconUrl { get; }
        public int PointCount { get; }
        public string ParentId { get; }

        [ObservableProperty] private ImageSource? _iconImage;

        public MapLabelItemVm(MaskMapPointLabel item)
        {
            Id = item.LabelId;
            LabelIds = item.LabelIds is { Count: > 0 } ? item.LabelIds : new[] { item.LabelId };
            Name = item.Name;
            IconUrl = item.IconUrl;
            PointCount = item.PointCount;
            ParentId = item.ParentId;
        }

        public MaskMapPointLabel ToModel()
        {
            return new MaskMapPointLabel
            {
                LabelId = Id,
                LabelIds = LabelIds,
                ParentId = ParentId,
                Name = Name,
                IconUrl = IconUrl,
                PointCount = PointCount
            };
        }

        public async Task LoadIconAsync(CancellationToken ct)
        {
            if (IconImage != null || string.IsNullOrEmpty(IconUrl))
            {
                return;
            }

            var img = await MapIconImageCache.GetAsync(IconUrl, ct);
            if (img == null)
            {
                return;
            }

            await Application.Current.Dispatcher.InvokeAsync(() => { IconImage = img; }, DispatcherPriority.Background);
        }
    }
}

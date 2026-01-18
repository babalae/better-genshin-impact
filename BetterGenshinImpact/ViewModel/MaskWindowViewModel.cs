using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service.Model.MihoyoMap.Requests;
using BetterGenshinImpact.Service.Model.MihoyoMap.Responses;
using BetterGenshinImpact.View.Controls.Overlay;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using PresentMonFps;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Model.MaskMap;
using Vanara.PInvoke;
using MaskMapPoint = BetterGenshinImpact.Model.MaskMap.MaskMapPoint;

namespace BetterGenshinImpact.ViewModel
{
    public partial class MaskWindowViewModel : ObservableRecipient
    {
        private readonly ILogger<MaskWindowViewModel> _logger = App.GetLogger<MaskWindowViewModel>();

        [ObservableProperty] private Rect _windowRect;

        [ObservableProperty] private ObservableCollection<StatusItem> _statusList = [];

        public AllConfig? Config { get; set; }

        [ObservableProperty] private string _fps = "0";
        
        public ObservableCollection<MaskMapPoint> Points { get; } = new ObservableCollection<MaskMapPoint>();
        
        [ObservableProperty] private double _maskWindowWidth;

        [ObservableProperty] private double _maskWindowHeight;

        [ObservableProperty] private bool _isInBigMapUi;

        [ObservableProperty] private bool _isMapPointPickerOpen;

        [ObservableProperty] private bool _isMapLabelTreeLoading;

        [ObservableProperty] private string _mapLabelSearchText = string.Empty;

        [ObservableProperty] private ObservableCollection<MapLabelCategoryVm> _mapLabelCategories = [];

        [ObservableProperty] private ObservableCollection<MapLabelItemVm> _mapLabelItems = [];

        [ObservableProperty] private MapLabelCategoryVm? _selectedMapLabelCategory;

        private bool _isMapLabelTreeLoaded;

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

        [RelayCommand]
        private async Task ReloadMapLabelTreeAsync()
        {
            _isMapLabelTreeLoaded = false;
            await EnsureLabelTreeLoadedAsync();
        }

        [RelayCommand]
        private void SelectMapLabelCategory(MapLabelCategoryVm? category)
        {
            SelectedMapLabelCategory = category;
            RebuildRightList(category, MapLabelSearchText);
        }

        [RelayCommand]
        private void SelectMapLabelItem(MapLabelItemVm? item)
        {
            if (item == null)
            {
                return;
            }

            _logger.LogInformation("选择地图点位分类: {Name}({Id}) Count={Count}", item.Name, item.Id, item.PointCount);
        }

        private void RefreshSettings()
        {
            InitConfig();
            if (Config != null)
            {
                OnPropertyChanged(nameof(Config));
            }
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

        private void InitFps()
        {
            if (Config!.MaskWindowConfig.ShowFps)
            {
                nint targetHWnd = TaskContext.Instance().GameHandle;
                _ = User32.GetWindowThreadProcessId(targetHWnd, out var pid);
                Task.Run(async () =>
                {
                    await FpsInspector.StartForeverAsync(new FpsRequest(pid), (result) =>
                    {
                        Fps = $"{result.Fps:0}";
                    });
                });
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
            }
        }

        partial void OnMapLabelSearchTextChanged(string value)
        {
            RebuildRightList(SelectedMapLabelCategory, value);
        }

        private async Task EnsureLabelTreeLoadedAsync()
        {
            if (_isMapLabelTreeLoaded || IsMapLabelTreeLoading)
            {
                return;
            }

            IsMapLabelTreeLoading = true;
            try
            {
                var apiService = App.GetService<IMihoyoMapApiService>();
                ApiResponse<LabelTreeData>? resp = null;

                if (apiService != null)
                {
                    resp = await apiService.GetLabelTreeAsync(new LabelTreeRequest());
                }

                if (resp == null || resp.Retcode != 0 || resp.Data == null)
                {
                    resp = TryLoadLabelTreeFromLocalExample();
                }

                if (resp == null || resp.Retcode != 0 || resp.Data == null)
                {
                    _logger.LogWarning("加载地图点位树失败: {Retcode} {Message}", resp?.Retcode, resp?.Message);
                    MapLabelCategories = [];
                    MapLabelItems = [];
                    SelectedMapLabelCategory = null;
                    return;
                }

                var categories = resp.Data.Tree
                    .OrderBy(x => x.Sort)
                    .ThenBy(x => x.DisplayPriority)
                    .Select(x => new MapLabelCategoryVm(x))
                    .ToList();

                MapLabelCategories = new ObservableCollection<MapLabelCategoryVm>(categories);
                SelectMapLabelCategory(MapLabelCategories.FirstOrDefault());
                _isMapLabelTreeLoaded = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "加载地图点位树时发生异常");
            }
            finally
            {
                IsMapLabelTreeLoading = false;
            }
        }

        private void RebuildRightList(MapLabelCategoryVm? category, string? searchText)
        {
            var query = category?.Items?.AsEnumerable() ?? Enumerable.Empty<MapLabelItemVm>();

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                var q = searchText.Trim();
                query = query.Where(x => x.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            MapLabelItems = new ObservableCollection<MapLabelItemVm>(query);
        }

        private static ApiResponse<LabelTreeData>? TryLoadLabelTreeFromLocalExample()
        {
            try
            {
                var root = AppContext.BaseDirectory;
                var path = Path.Combine(root, ".trae", "documents", "tree.json");
                if (!File.Exists(path))
                {
                    return null;
                }

                var json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<ApiResponse<LabelTreeData>>(json);
            }
            catch
            {
                return null;
            }
        }

        [RelayCommand]
        private void OnPointClick(MaskMapPoint? point)
        {
            if (point != null)
            {
                // 在这里触发 UI 或逻辑（示例：弹窗）
                MessageBox.Show($"点击了点: {point.Id}");
            }
        }

        [RelayCommand]
        private void OnPointRightClick(MaskMapPoint? point)
        {
            if (point != null)
            {
                // 自定义右键逻辑
            }
        }

        [RelayCommand]
        private void OnPointHover(MaskMapPoint? point)
        {
            if (point != null)
            {
                // 悬停逻辑
            }
        }
    }

    public partial class MapLabelCategoryVm : ObservableObject
    {
        public int Id { get; }
        public string Name { get; }
        public string Icon { get; }
        public ObservableCollection<MapLabelItemVm> Items { get; }

        public MapLabelCategoryVm(LabelNode node)
        {
            Id = node.Id;
            Name = node.Name;
            Icon = node.Icon;

            var list = (node.Children != null && node.Children.Count > 0 ? node.Children : [node])
                .OrderBy(x => x.Sort)
                .ThenBy(x => x.DisplayPriority)
                .Select(x => new MapLabelItemVm(x))
                .ToList();

            Items = new ObservableCollection<MapLabelItemVm>(list);
        }
    }

    public partial class MapLabelItemVm : ObservableObject
    {
        public int Id { get; }
        public string Name { get; }
        public string Icon { get; }
        public int PointCount { get; }

        public MapLabelItemVm(LabelNode node)
        {
            Id = node.Id;
            Name = node.Name;
            Icon = node.Icon;
            PointCount = node.PointCount;
        }
    }
}

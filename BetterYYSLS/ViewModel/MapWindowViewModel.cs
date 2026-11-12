using BetterYYSLS.Core.Godlike;
using BetterYYSLS.Core.Config;
using BetterYYSLS.Core.Map;
using BetterYYSLS.Service;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace BetterYYSLS.ViewModel;

/// <summary>
/// 大地图窗口的视图模型：快照、筛选、标记选择和宏观路线。
/// </summary>
public partial class MapWindowViewModel : ObservableObject
{
    private static readonly HttpClient IconHttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly List<MapMarker> _allMarkers = [];
    private readonly Dictionary<string, BitmapImage> _iconCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BitmapImage> _categoryIconCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BitmapImage> _roleAvatarCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _roleAvatarLoading = new(StringComparer.Ordinal);

    /// <summary>
    /// 官方角色面板使用的默认头像，账号绑定列表和 CEF 日志都没有头像时回退到它。
    /// </summary>
    private const string DefaultRoleAvatarUrl =
        "https://img.166.net/gameyw-misc/opd/squash/20210122/112727-wpeodfqr43.png";
    private readonly Dictionary<string, MapMarkerDetails> _detailsCache = new(StringComparer.Ordinal);
    private readonly GodlikeGameLocationService _locationService = new();
    private readonly GodlikeSessionService _sessionService = new();

    /// <summary>从持久化配置恢复地图选项时阻止重复保存。</summary>
    private bool _restoringMapSelection;

    /// <summary>
    /// 创建大地图窗口视图模型。
    /// </summary>
    public MapWindowViewModel()
    {
        _locationService.LocationChanged += OnLocationChanged;
        _locationService.StatusChanged += OnLocationStatusChanged;
        _sessionService.StateChanged += OnSessionChanged;
        _completionDebounceTimer.Tick += (_, _) =>
        {
            _completionDebounceTimer.Stop();
            _ = RefreshCompletionStatesAsync();
        };
        _autoRouteDebounceTimer.Tick += (_, _) =>
        {
            _autoRouteDebounceTimer.Stop();
            _ = UpdateAutoRouteAsync();
        };
    }

    /// <summary>
    /// 启动实时玩家坐标读取。
    /// </summary>
    public void StartLiveTracking()
    {
        if (IsLiveTracking)
        {
            return;
        }

        _locationService.SubType = SelectedRegion?.SubTypes.FirstOrDefault() ?? "1";
        _locationService.Start();
        try
        {
            _sessionService.StartWatching();
        }
        catch (Exception ex)
        {
            SessionStatus = $"登录态监听启动失败：{ex.Message}";
        }
        IsLiveTracking = true;
        LocationStatus = "正在连接游戏进程...";
    }

    /// <summary>
    /// 停止实时玩家坐标读取。
    /// </summary>
    public void StopLiveTracking()
    {
        _locationService.Stop();
        IsLiveTracking = false;
        LocationStatus = "实时坐标已停止";
    }

    private void OnLocationChanged(object? sender, GodlikeGameLocation location)
    {
        RunOnUi(() =>
        {
            PlayerWorldPosition = new MapPoint(location.WorldX, location.WorldY, location.WorldZ);
            PlayerMapPosition = GodlikeProjection.WorldToMap(
                SelectedRegion?.SubTypes.FirstOrDefault() ?? location.SubType,
                location.WorldX,
                location.WorldY);
            TryAutoSwitchToPlayerMap(location.WorldX, location.WorldY);
        });
    }

    private void OnLocationStatusChanged(object? sender, string status)
    {
        RunOnUi(() => LocationStatus = status);
    }

    private void OnSessionChanged(object? sender, GodlikeSessionState state)
    {
        RunOnUi(() => SessionStatus = state.Message);
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
    }

    /// <summary>
    /// 同步回到 UI 线程；角色接口可能在后台线程恢复，集合更新不能跨线程。
    /// </summary>
    private static void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }

    /// <summary>
    /// 当前地图快照。
    /// </summary>
    [ObservableProperty]
    private MapSnapshot? _snapshot;

    /// <summary>
    /// 当前区域。
    /// </summary>
    [ObservableProperty]
    private MapRegion? _selectedRegion;

    /// <summary>
    /// 当前图层。
    /// </summary>
    [ObservableProperty]
    private MapLayer? _selectedLayer;

    /// <summary>
    /// 搜索文本。
    /// </summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>
    /// 地图窗口整体不透明度；降低后游戏画面可以透出。
    /// </summary>
    [ObservableProperty]
    private double _windowOpacity = 1;

    /// <summary>
    /// 是否跟随游戏窗口。
    /// </summary>
    [ObservableProperty]
    private bool _isFollowGame = true;

    /// <summary>
    /// 是否让玩家标记始终位于地图视口正中。
    /// </summary>
    [ObservableProperty]
    private bool _isPlayerCentered;

    /// <summary>
    /// 是否让地图窗口始终显示在游戏窗口之上。
    /// </summary>
    [ObservableProperty]
    private bool _isAlwaysOnTop;

    /// <summary>
    /// 实时坐标状态。
    /// </summary>
    [ObservableProperty]
    private string _locationStatus = "实时坐标未启动";

    /// <summary>
    /// 官方登录态状态。
    /// </summary>
    [ObservableProperty]
    private string _sessionStatus = "未检测 Godlike 登录态";

    /// <summary>
    /// 用于查询宝箱完成状态的游戏角色 ID。
    /// </summary>
    [ObservableProperty]
    private string _roleId = string.Empty;

    /// <summary>
    /// 角色弹窗手动输入的角色 ID；不参与持久化，避免下次打开时自动填入。
    /// </summary>
    [ObservableProperty]
    private string _roleInputId = string.Empty;

    /// <summary>
    /// 用于查询宝箱完成状态的服务器。
    /// </summary>
    [ObservableProperty]
    private string _server = string.Empty;

    /// <summary>
    /// 当前账号绑定的游戏角色列表。
    /// </summary>
    public ObservableCollection<GodlikeRole> Roles { get; } = [];

    /// <summary>
    /// 角色选择弹窗的项目。
    /// </summary>
    public ObservableCollection<MapRoleChoice> RoleChoices { get; } = [];

    /// <summary>
    /// 角色选择弹窗是否打开。
    /// </summary>
    [ObservableProperty]
    private bool _isRoleDialogOpen;

    /// <summary>
    /// 角色选择弹窗当前项目。
    /// </summary>
    [ObservableProperty]
    private MapRoleChoice? _selectedRoleChoice;

    /// <summary>
    /// 当前角色头像。
    /// </summary>
    [ObservableProperty]
    private BitmapImage? _selectedRoleAvatar;

    /// <summary>
    /// 当前用于地图完成度的角色。
    /// </summary>
    [ObservableProperty]
    private GodlikeRole? _selectedRole;

    /// <summary>
    /// 避免角色选择回填 RoleId 时重复触发自动查找。
    /// </summary>
    private bool _syncingRole;

    /// <summary>
    /// 账号绑定接口返回的角色 ID；这些角色不能从列表移除。
    /// </summary>
    private readonly HashSet<string> _boundRoleIds = new(StringComparer.Ordinal);

    /// <summary>
    /// 角色 ID 自动查找的取消令牌。
    /// </summary>
    private CancellationTokenSource? _roleLookupCts;

    /// <summary>
    /// 角色查找请求版本，避免旧请求覆盖用户刚输入的 ID。
    /// </summary>
    private int _roleLookupVersion;

    /// <summary>
    /// 角色列表读取状态。
    /// </summary>
    [ObservableProperty]
    private string _roleStatus = "未加载角色列表";

    /// <summary>
    /// 宝箱完成状态刷新进度。
    /// </summary>
    [ObservableProperty]
    private string _completionStatus = "填写角色后可刷新宝箱完成状态";

    /// <summary>
    /// 路线、快捷入口、收藏、KOL、UGC 等信息摘要。
    /// </summary>
    [ObservableProperty]
    private string _auxiliaryStatus = "尚未加载附加信息";

    /// <summary>
    /// 当前用于完成度刷新的分类；为空表示当前筛选分类。
    /// </summary>
    [ObservableProperty]
    private MapCategoryFilter? _selectedCategory;

    /// <summary>
    /// 是否隐藏已完成标点。
    /// </summary>
    [ObservableProperty]
    private bool _hideCompleted = true;

    /// <summary>
    /// 宝箱完成状态刷新取消源。
    /// </summary>
    private CancellationTokenSource? _completionCts;

    /// <summary>
    /// 是否正在批量读取完成状态。
    /// </summary>
    private bool _completionRefreshRunning;

    /// <summary>
    /// 当前筛选的完成状态刷新是否已经结束；结束后允许显示无法取得状态的点位并继续绘制路线。
    /// </summary>
    private bool _completionRefreshCompleted;

    /// <summary>
    /// 刷新期间角色或筛选再次变化时，等待本轮结束立即补刷一次。
    /// </summary>
    private bool _completionRefreshPending;
    /// <summary>
    /// 角色作用域的完成状态；不写回地图快照。
    /// </summary>
    private readonly MarkerCompletionStore _completionStore = new();

    private readonly System.Windows.Threading.DispatcherTimer _completionDebounceTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(450)
    };

    /// <summary>
    /// 自动路线最多规划的点数；超过时按传送点分组分层抽取，避免 UI 长时间阻塞。
    /// </summary>
    private const int AutoRouteMaxPoints = 2048;

    /// <summary>
    /// 自动路线规划取消源；筛选变化时取消上一轮后台规划。
    /// </summary>
    private CancellationTokenSource? _autoRouteCts;

    /// <summary>
    /// 自动路线防抖计时器，避免连续勾选分类时重复规划。
    /// </summary>
    private readonly System.Windows.Threading.DispatcherTimer _autoRouteDebounceTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(150)
    };

    /// <summary>
    /// 是否正在实时读取玩家坐标。
    /// </summary>
    [ObservableProperty]
    private bool _isLiveTracking;

    /// <summary>
    /// 玩家世界坐标。
    /// </summary>
    [ObservableProperty]
    private MapPoint? _playerWorldPosition;

    /// <summary>
    /// 玩家地图坐标。
    /// </summary>
    [ObservableProperty]
    private MapPoint? _playerMapPosition;

    /// <summary>
    /// 是否处于点击穿透模式。
    /// </summary>
    [ObservableProperty]
    private bool _isClickThrough;

    /// <summary>
    /// 是否处于全屏模式。
    /// </summary>
    [ObservableProperty]
    private bool _isFullScreen;

    /// <summary>
    /// 当前状态提示。
    /// </summary>
    [ObservableProperty]
    private string _status = "请导入地图快照";

    /// <summary>
    /// 当前选中的标记。
    /// </summary>
    [ObservableProperty]
    private MapMarker? _selectedMarker;

    /// <summary>
    /// 当前选中标记的图标。
    /// </summary>
    [ObservableProperty]
    private BitmapImage? _selectedMarkerIcon;

    /// <summary>
    /// 当前选中标记的官方详情。
    /// </summary>
    [ObservableProperty]
    private MapMarkerDetails? _selectedMarkerDetails;

    /// <summary>
    /// 当前选中标记的分类、图层和地图坐标。
    /// </summary>
    public string SelectedMarkerInfo => SelectedMarker is null
        ? string.Empty
        : $"分类 {SelectedMarker.CategoryId} · 图层 {SelectedMarker.LayerId ?? "无"} · " +
          $"地图坐标 {SelectedMarker.MapPosition.X:F0}, {SelectedMarker.MapPosition.Y:F0}";

    /// <summary>
    /// 分类完成度面板。
    /// </summary>
    public ObservableCollection<MapCategoryDashboardItem> Dashboard { get; } = [];

    /// <summary>
    /// 当前分类面板条目。
    /// </summary>
    [ObservableProperty]
    private MapCategoryDashboardItem? _selectedDashboardItem;

    /// <summary>
    /// 当前宏观路线。
    /// </summary>
    [ObservableProperty]
    private MacroRoute? _currentRoute;

    /// <summary>
    /// 路线摘要。
    /// </summary>
    [ObservableProperty]
    private string _routeSummary = "未选择标记";

    /// <summary>
    /// 地图名称。
    /// </summary>
    public string MapName => Snapshot?.Name ?? "未加载地图";

    /// <summary>
    /// 当前区域列表。
    /// </summary>
    public ObservableCollection<MapRegion> Regions { get; } = [];

    /// <summary>
    /// 官方区域下拉选项，包含当前快照没有独立数据的青州、江南。
    /// </summary>
    public ObservableCollection<MapRegionOption> RegionOptions { get; } = [];

    /// <summary>
    /// 区域下拉中的一级区域及其二级子区域。
    /// </summary>
    public ObservableCollection<MapRegionGroup> RegionGroups { get; } = [];

    /// <summary>
    /// 顶部区域按钮显示文本。
    /// </summary>
    [ObservableProperty]
    private string _selectedRegionLabel = "未选择区域";

    /// <summary>
    /// 每次传送的固定时间代价 c；不影响步行距离目标。
    /// 默认 0，表示传送不额外计费，路线由步行距离主导。
    /// </summary>
    [ObservableProperty]
    private double _teleportCost = 0;

    /// <summary>
    /// 选中官方区域后地图聚焦的地图坐标。
    /// </summary>
    [ObservableProperty]
    private MapPoint? _focusPoint;

    /// <summary>
    /// 当前选中的官方区域项。
    /// </summary>
    [ObservableProperty]
    private MapRegionOption? _selectedRegionOption;

    /// <summary>
    /// 当前图层列表。
    /// </summary>
    public ObservableCollection<MapLayer> Layers { get; } = [];

    /// <summary>
    /// 分类筛选列表。
    /// </summary>
    public ObservableCollection<MapCategoryFilter> Categories { get; } = [];

    /// <summary>
    /// 一级玩法分组。
    /// </summary>
    public ObservableCollection<MapCategoryGroup> CategoryGroups { get; } = [];

    /// <summary>
    /// 当前一级玩法分组。
    /// </summary>
    [ObservableProperty]
    private MapCategoryGroup? _selectedCategoryGroup;

    /// <summary>
    /// 当前分组下显示的分类图标。
    /// </summary>
    public ObservableCollection<MapCategoryFilter> VisibleCategoryFilters { get; } = [];

    /// <summary>
    /// 筛选后的标记。
    /// </summary>
    public ObservableCollection<MapMarker> FilteredMarkers { get; } = [];

    /// <summary>
    /// 已选标记标识。
    /// </summary>
    public ObservableCollection<string> SelectedMarkerIds { get; } = [];

    /// <summary>
    /// 已选标记。
    /// </summary>
    public ObservableCollection<MapMarker> SelectedMarkers { get; } = [];

    /// <summary>
    /// 加载默认快照。
    /// </summary>
    public async Task LoadDefaultAsync()
    {
        // 先恢复上次角色，角色输入框和顶栏头像不必等网络角色列表返回。
        if (LoadPersistedRole() is { } persistedRole)
        {
            SelectedRole = persistedRole;
            RoleId = persistedRole.RoleId;
            Server = persistedRole.Server;
        }

        await RefreshSession();
        var path = Path.Combine(AppContext.BaseDirectory, "User", "Map", "snapshot.json");
        if (!File.Exists(path))
        {
            // 新实例没有 User 快照时使用随程序发布的全量地图数据。
            var bundledPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Map", "snapshot.json");
            if (File.Exists(bundledPath))
            {
                path = bundledPath;
                Status = "正在加载内置全量地图数据...";
            }
        }

        if (!File.Exists(path))
        {
            Status = "未找到本地快照，正在从 Godlike 同步...";
            await SyncFromGodlikeAsync();
            return;
        }

        await LoadSnapshotAsync(path);
        _ = RefreshRoles();
    }

    partial void OnSelectedMarkerChanged(MapMarker? value)
    {
        OnPropertyChanged(nameof(SelectedMarkerInfo));
        SelectedMarkerDetails = null;
        _ = LoadSelectedMarkerIconAsync(value?.Icon);
        _ = LoadSelectedMarkerDetailsAsync(value);
    }

    private async Task LoadSelectedMarkerDetailsAsync(MapMarker? marker)
    {
        if (marker is null || Snapshot is null || marker.Id.StartsWith("anchor:", StringComparison.Ordinal))
        {
            return;
        }

        if (_detailsCache.TryGetValue(marker.Id, out var cached))
        {
            SelectedMarkerDetails = cached;
            ApplyMarkerCompletion(marker, cached);
            return;
        }

        try
        {
            var credentials = GodlikeCredentials.Load();
            var details = await GodlikeMapApi.LoadPointDetailsAsync(
                credentials,
                Snapshot.MapId,
                [marker.Id],
                string.IsNullOrWhiteSpace(RoleId) ? null : RoleId,
                string.IsNullOrWhiteSpace(Server) ? null : Server);
            var detail = details.FirstOrDefault();
            if (detail is null)
            {
                return;
            }

            _detailsCache[marker.Id] = detail;
            ApplyMarkerCompletion(marker, detail);
            RefreshDashboard();
            if (SelectedMarker?.Id == marker.Id)
            {
                SelectedMarkerDetails = detail;
                OnPropertyChanged(nameof(SelectedMarkerInfo));
            }

            // 详情里的 roleVisit 可能不同于旧快照，同步重算地图和路线。
            RefreshFilters();
        }
        catch
        {
            // 详情接口失败时保留本地快照字段，不影响地图操作。
        }
    }

    /// <summary>
    /// 把详情中的完成状态同步到地图点位，避免详情显示已完成但路线仍按未完成处理。
    /// </summary>
    private void ApplyMarkerCompletion(MapMarker marker, MapMarkerDetails detail)
    {
        _completionStore.Set(RoleId, marker.Id, new MarkerCompletion(true, detail.IsCompleted));
    }

    private async Task LoadSelectedMarkerIconAsync(string? url)
    {
        SelectedMarkerIcon = null;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (_iconCache.TryGetValue(url, out var cached))
        {
            SelectedMarkerIcon = cached;
            return;
        }

        try
        {
            var bytes = await IconHttpClient.GetByteArrayAsync(url);
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            _iconCache[url] = image;
            if (SelectedMarker?.Icon == url)
            {
                SelectedMarkerIcon = image;
            }
        }
        catch
        {
            // 图标加载失败时保留详情文字，不影响地图交互。
        }
    }

    private async Task LoadCategoryIconAsync(MapCategoryFilter filter)
    {
        try
        {
            var local = new BitmapImage();
            local.BeginInit();
            local.CacheOption = BitmapCacheOption.OnLoad;
            local.UriSource = new Uri(
                $"pack://application:,,,/BetterYYSLS;component/Assets/Map/Categories/{filter.Id}.png");
            local.EndInit();
            local.Freeze();
            filter.IconImage = local;
            return;
        }
        catch
        {
            // 本地资源缺失时回退到官方 URL。
        }

        if (string.IsNullOrWhiteSpace(filter.IconUrl))
        {
            return;
        }

        if (_categoryIconCache.TryGetValue(filter.IconUrl, out var cached))
        {
            filter.IconImage = cached;
            return;
        }

        try
        {
            var bytes = await IconHttpClient.GetByteArrayAsync(filter.IconUrl);
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            _categoryIconCache[filter.IconUrl] = image;
            filter.IconImage = image;
        }
        catch
        {
            // 分类图标失败时保留文字。
        }
    }

    /// <summary>
    /// 从指定文件加载快照。
    /// </summary>
    public async Task LoadSnapshotAsync(string path)
    {
        try
        {
            var snapshot = await MapSnapshotStore.LoadAsync(path);
            if (snapshot.Markers.Count == 0)
            {
                var bundledPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Map", "snapshot.json");
                if (!string.Equals(path, bundledPath, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(bundledPath))
                {
                    snapshot = await MapSnapshotStore.LoadAsync(bundledPath);
                }
            }

            await ApplySnapshotOnUiAsync(snapshot);
            Status = $"已加载 {snapshot.Markers.Count} 个标记。";
        }
        catch (Exception ex)
        {
            Status = $"加载失败：{ex.Message}";
        }
    }

    /// <summary>
    /// 把快照中的集合变更切回 UI 线程，避免异步读取后更新绑定集合。
    /// </summary>
    private async Task ApplySnapshotOnUiAsync(MapSnapshot snapshot)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ApplySnapshot(snapshot);
            return;
        }

        await dispatcher.InvokeAsync(() => ApplySnapshot(snapshot));
    }

    /// <summary>
    /// 读取本机 Godlike 会话；若官方客户端在此期间刷新了登录缓存，则重试一次。
    /// </summary>
    private static async Task<MapSnapshot> LoadGodlikeSnapshotWithRefreshAsync()
    {
        var credentials = GodlikeCredentials.Load();
        try
        {
            return await GodlikeMapApi.LoadSnapshotAsync(credentials);
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("401", StringComparison.Ordinal))
        {
            // 官方客户端重新登录后会把新的 GL-Token 写回缓存，重新读取即可刷新会话。
            await Task.Delay(TimeSpan.FromMilliseconds(300));
            return await GodlikeMapApi.LoadSnapshotAsync(GodlikeCredentials.Load());
        }
    }

    /// <summary>
    /// 从本机 Godlike 会话同步当前地图的全部标记。
    /// </summary>
    [RelayCommand]
    public async Task SyncFromGodlikeAsync()
    {
        Status = "正在从 Godlike 同步地图...";
        try
        {
            var snapshot = await LoadGodlikeSnapshotWithRefreshAsync();
            await ApplySnapshotOnUiAsync(snapshot);
            var path = Path.Combine(AppContext.BaseDirectory, "User", "Map", "snapshot.json");
            await MapSnapshotStore.SaveAsync(path, snapshot);
            SessionStatus = $"已同步 Godlike 登录态 · {snapshot.Markers.Count} 标记";
            Status = $"已同步 {snapshot.Markers.Count} 个标记，{snapshot.Categories.Count} 个分类。";
        }
        catch (Exception ex)
        {
            Status = $"同步失败：{ex.Message}";
        }
    }

    /// <summary>
    /// 按当前区域、图层、分类、搜索和角色选项重新计算结果，不请求 Godlike。
    /// </summary>
    [RelayCommand]
    private void RefreshCurrentResults()
    {
        if (Snapshot is null)
        {
            Status = "没有可计算的地图数据";
            return;
        }

        RefreshDashboard();
        RefreshFilters();
        Status = "已按当前选项重新计算";
    }

    /// <summary>
    /// 选择文件并导入地图快照。
    /// </summary>
    [RelayCommand]
    public async Task ImportSnapshotAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入地图快照",
            Filter = "地图快照 (*.json)|*.json|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await LoadSnapshotAsync(dialog.FileName);
    }

    /// <summary>
    /// 选择或取消选择一个标记。
    /// </summary>
    public void SelectMarker(MapMarker marker)
    {
        SelectedMarker = marker;
        if (!SelectedMarkerIds.Remove(marker.Id))
        {
            SelectedMarkerIds.Add(marker.Id);
        }

        RefreshSelectedMarkers();
        RouteSummary = SelectedMarkerIds.Count == 0
            ? "未选择标记"
            : $"已选 {SelectedMarkerIds.Count} 个标记";
    }

    /// <summary>
    /// 勾选全部玩法分类。
    /// </summary>
    [RelayCommand]
    private void SelectAllCategories()
    {
        SelectedCategory = null;
        SelectedCategoryGroup = null;
        foreach (var category in Categories)
        {
            category.IsVisible = true;
        }

        RefreshFilters();
        ScheduleCompletionRefresh();
    }

    /// <summary>
    /// 关闭右下角点位详情，不改变已选点位集合。
    /// </summary>
    [RelayCommand]
    private void CloseMarkerDetails()
    {
        SelectedMarker = null;
    }

    /// <summary>
    /// 清空点位、分类、搜索和路线选择，恢复地图默认状态。
    /// </summary>
    [RelayCommand]
    private void ClearSelection()
    {
        SelectedMarkerIds.Clear();
        SelectedMarkers.Clear();
        CurrentRoute = null;
        SelectedMarker = null;
        SelectedCategory = null;
        SelectedCategoryGroup = null;
        SearchText = string.Empty;
        HideCompleted = true;
        SelectedLayer = Layers.FirstOrDefault();
        foreach (var category in Categories)
        {
            category.IsVisible = false;
        }

        RouteSummary = "未选择标记";
        RefreshFilters();
    }

    /// <summary>
    /// 选择官方区域下拉项。
    /// </summary>
    public void SelectRegionOption(MapRegionOption option, bool userInitiated = true)
    {
        ArgumentNullException.ThrowIfNull(option);
        if (userInitiated)
        {
            // 手动切图时关闭“角色始终居中”，避免游戏坐标更新后自动切回玩家地图。
            IsPlayerCentered = false;
        }

        foreach (var regionOption in RegionOptions)
        {
            regionOption.IsSelected = ReferenceEquals(regionOption, option);
        }

        SelectedRegionOption = option;
        SelectedRegionLabel = option.Name;
        if (string.IsNullOrWhiteSpace(option.SubType))
        {
            SaveMapSelection();
            return;
        }

        var region = Regions.FirstOrDefault(candidate =>
            candidate.SubTypes.Contains(option.SubType, StringComparer.Ordinal));
        if (region is not null)
        {
            // 自动跟随切图时 OnSelectedRegionChanged 会关闭居中，这里记录并在切图后恢复；
            // 手动切图已在方法入口关闭居中，不会被恢复。
            var keepPlayerCentered = IsPlayerCentered;
            // 切图时先丢弃旧区域路线，避免规划完成前仍显示上一张地图的路线。
            CancelAutoRoute();
            CurrentRoute = null;
            RouteSummary = "区域已切换，正在规划路线...";
            SelectedRegion = region;
            FocusPoint = GodlikeProjection.WorldToMap(option.SubType, option.WorldX, option.WorldY);
            SelectedRegionLabel = option.Name;
            // 同一 mapSubType 下的清河/开封/不见山/青州/江南也必须重新按区域中心筛选。
            RefreshFilters();
            ScheduleCompletionRefresh();
            IsPlayerCentered = keepPlayerCentered;
            SaveMapSelection();
        }
    }

    /// <summary>
    /// 从持久化配置恢复区域和位面。
    /// </summary>
    private void RestoreMapSelection()
    {
        _restoringMapSelection = true;
        try
        {
            var saved = ConfigService.Config?.MapWindowConfig;
            var option = RegionOptions.FirstOrDefault(item =>
                             !string.IsNullOrWhiteSpace(saved?.SelectedRegionName) &&
                             item.Name == saved.SelectedRegionName)
                         ?? RegionOptions.FirstOrDefault();
            if (option is not null)
            {
                SelectRegionOption(option, userInitiated: false);
            }
            else
            {
                SelectedRegion = Regions.FirstOrDefault();
                SelectedRegionOption = null;
                SelectedRegionLabel = "未选择区域";
            }

            if (!string.IsNullOrWhiteSpace(saved?.SelectedLayerId))
            {
                SelectedLayer = Layers.FirstOrDefault(layer => layer.Id == saved.SelectedLayerId)
                                ?? Layers.FirstOrDefault();
            }
        }
        finally
        {
            _restoringMapSelection = false;
        }
    }

    /// <summary>
    /// 从角色 ID 前缀推断服务器编号；仅在没有绑定列表和日志信息时使用。
    /// </summary>
    private static string InferServerFromRoleId(string roleId)
    {
        if (roleId.Length < 3 || !int.TryParse(roleId[..3], out var prefix) || prefix <= 0)
        {
            return string.Empty;
        }

        return $"100{prefix:D2}";
    }

    /// <summary>
    /// 把持久化角色资料转换为角色对象。
    /// </summary>
    private static GodlikeRole ToGodlikeRole(MapRoleProfile profile)
    {
        return new GodlikeRole
        {
            RoleId = profile.RoleId,
            Server = profile.Server,
            ServerName = string.IsNullOrWhiteSpace(profile.ServerName) ? "正式服" : profile.ServerName,
            Nick = profile.Nick,
            Icon = profile.Icon,
            AppKey = "h72"
        };
    }

    /// <summary>
    /// 从配置恢复上次手动选择、可能不在绑定列表中的角色。
    /// </summary>
    private static GodlikeRole? LoadPersistedRole()
    {
        var config = ConfigService.Config?.MapWindowConfig;
        if (config is null ||
            !GodlikeRoleService.IsUsableRoleId(config.SelectedRoleId))
        {
            return null;
        }

        return new GodlikeRole
        {
            RoleId = config.SelectedRoleId,
            Server = config.SelectedRoleServer,
            ServerName = string.IsNullOrWhiteSpace(config.SelectedRoleServerName)
                ? (string.IsNullOrWhiteSpace(config.SelectedRoleServer) ? "未知服务器" : "正式服")
                : config.SelectedRoleServerName,
            Nick = config.SelectedRoleNick,
            Icon = config.SelectedRoleIcon,
            AppKey = "h72"
        };
    }

    /// <summary>
    /// 保存当前角色，包含不在账号绑定列表中的手动角色。
    /// </summary>
    private static void SaveSelectedRole(GodlikeRole? role, bool persistProfile = false)
    {
        if (role is null || ConfigService.Config is not { } config)
        {
            return;
        }

        config.MapWindowConfig.SelectedRoleId = role.RoleId;
        config.MapWindowConfig.SelectedRoleServer = role.Server;
        config.MapWindowConfig.SelectedRoleServerName = role.ServerName;
        config.MapWindowConfig.SelectedRoleNick = role.Nick;
        config.MapWindowConfig.SelectedRoleIcon = role.Icon;

        // 绑定角色只记住“上次选择”，不写入 SavedRoles；
        // 只有用户手动输入并验证过的角色才进入可移除的角色列表。
        if (!persistProfile)
        {
            ConfigService.Config?.OnAnyChangedAction?.Invoke();
            return;
        }

        var profile = config.MapWindowConfig.SavedRoles
            .FirstOrDefault(item => item.RoleId == role.RoleId);
        if (profile is null)
        {
            profile = new MapRoleProfile { RoleId = role.RoleId };
            config.MapWindowConfig.SavedRoles.Add(profile);
        }

        profile.Server = role.Server;
        profile.ServerName = role.ServerName;
        profile.Nick = role.Nick;
        profile.Icon = role.Icon;
        profile.IsManual = true;
        // SavedRoles 是普通 List，集合内容变化不会触发 AllConfig 的属性变更保存。
        ConfigService.Config?.OnAnyChangedAction?.Invoke();
    }

    /// <summary>
    /// 保存当前区域和位面选择。
    /// </summary>
    private void SaveMapSelection()
    {
        if (_restoringMapSelection || ConfigService.Config is not { } config)
        {
            return;
        }

        config.MapWindowConfig.SelectedRegionName = SelectedRegionOption?.Name ?? string.Empty;
        config.MapWindowConfig.SelectedLayerId = SelectedLayer?.Id ?? string.Empty;
    }

    /// <summary>
    /// 有角色坐标时切换到最近的一级地图；滹沱没有一级地图时尝试其子区域。
    /// </summary>
    private void TryAutoSwitchToPlayerMap(double worldX, double worldY)
    {
        if (!IsPlayerCentered ||
            ConfigService.Config?.MapWindowConfig.AutoSwitchToPlayerMap != true ||
            RegionOptions.Count == 0)
        {
            return;
        }

        var candidates = RegionOptions
            .Where(option => !string.IsNullOrWhiteSpace(option.SubType) &&
                             (option.ParentName is null || option.SubType == "3"))
            .ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        var target = candidates
            .OrderBy(option =>
            {
                var dx = option.WorldX - worldX;
                var dy = option.WorldY - worldY;
                return (dx * dx) + (dy * dy);
            })
            .First();
        if (ReferenceEquals(target, SelectedRegionOption))
        {
            return;
        }

        var targetDistance = DistanceSquared(target, worldX, worldY);
        var currentDistance = SelectedRegionOption is null
            ? double.PositiveInfinity
            : DistanceSquared(SelectedRegionOption, worldX, worldY);
        if (targetDistance + 10000 >= currentDistance)
        {
            return;
        }

        SelectRegionOption(target, userInitiated: false);
    }

    private static double DistanceSquared(MapRegionOption option, double x, double y)
    {
        var dx = option.WorldX - x;
        var dy = option.WorldY - y;
        return (dx * dx) + (dy * dy);
    }

    /// <summary>
    /// 切换点击穿透。
    /// </summary>
    [RelayCommand]
    private void ToggleClickThrough()
    {
        IsClickThrough = !IsClickThrough;
    }

    /// <summary>
    /// 读取账号绑定角色，切换当前完成度角色。
    /// </summary>
    [RelayCommand]
    private async Task RefreshRoles()
    {
        RoleStatus = "正在读取角色列表...";
        var savedRole = LoadPersistedRole();
        var previousRoleId = string.IsNullOrWhiteSpace(RoleId)
            ? savedRole?.RoleId ?? string.Empty
            : RoleId;
        var previousSelectedRole = SelectedRole ?? savedRole;
        try
        {
            var credentials = GodlikeCredentials.Load();
            var roles = await GodlikeRoleService.LoadRolesAsync(credentials);
            _boundRoleIds.Clear();
            foreach (var role in roles)
            {
                _boundRoleIds.Add(role.RoleId);
            }

            Roles.Clear();
            foreach (var role in roles)
            {
                Roles.Add(role);
            }

            // 绑定接口可能只返回部分角色；客户端角色列表里有但接口缺失的角色也要补回，
            // 否则同一个账号会出现“绑定角色少一个”的情况。
            foreach (var cached in GodlikeRoleService.LoadCachedRoles(credentials))
            {
                _boundRoleIds.Add(cached.RoleId);
                if (!Roles.Any(role => role.RoleId == cached.RoleId))
                {
                    Roles.Add(cached);
                }
            }

            // 从配置恢复手动验证过的角色；绑定列表刷新不会影响这些角色。
            var config = ConfigService.Config?.MapWindowConfig;
            if (config is not null)
            {
                // 清掉旧版本写入的 *** 脱敏角色；它不能查询完成度，也不应出现在角色列表。
                config.SavedRoles.RemoveAll(profile =>
                    !GodlikeRoleService.IsUsableRoleId(profile.RoleId));
                foreach (var profile in config.SavedRoles)
                {
                    if (GodlikeRoleService.IsUsableRoleId(profile.RoleId) &&
                        !Roles.Any(role => role.RoleId == profile.RoleId))
                    {
                        Roles.Add(ToGodlikeRole(profile));
                    }
                }
            }

            // 刷新期间选中的手动角色也不能丢。
            if (previousSelectedRole is not null &&
                GodlikeRoleService.IsUsableRoleId(previousSelectedRole.RoleId) &&
                !Roles.Any(role => role.RoleId == previousSelectedRole.RoleId))
            {
                Roles.Add(previousSelectedRole);
            }

            // 接口、缓存和本地手动角色可能来自多个来源；按角色 ID 去重后再生成选择项。
            var uniqueRoles = Roles
                .Where(role => GodlikeRoleService.IsUsableRoleId(role.RoleId))
                .GroupBy(role => role.RoleId, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();
            Roles.Clear();
            foreach (var role in uniqueRoles)
            {
                Roles.Add(role);
            }

            var current = Roles.FirstOrDefault(role =>
                role.RoleId == previousRoleId ||
                (role.Server == Server && role.RoleId == previousRoleId)) ??
                Roles.FirstOrDefault(role => role.RoleId == previousSelectedRole?.RoleId) ??
                Roles.FirstOrDefault();
            SelectedRole = current;
            BuildRoleChoices();
            RoleStatus = Roles.Count == 0
                ? "当前账号没有可用角色"
                : $"已加载 {Roles.Count} 个角色";
            if (current is not null)
            {
                RefreshCompletionNow();
            }
        }
        catch (Exception ex)
        {
            var credentials = GodlikeCredentials.Load();
            var cachedRoles = GodlikeRoleService.LoadCachedRoles(credentials);
            if (cachedRoles.Count > 0)
            {
                _boundRoleIds.Clear();
                Roles.Clear();
                foreach (var cached in cachedRoles)
                {
                    _boundRoleIds.Add(cached.RoleId);
                    Roles.Add(cached);
                }

                var cachedSavedRole = LoadPersistedRole();
                if (cachedSavedRole is not null &&
                    !Roles.Any(role => role.RoleId == cachedSavedRole.RoleId))
                {
                    Roles.Add(cachedSavedRole);
                }

                SelectedRole = Roles.FirstOrDefault(role => role.RoleId == RoleId) ??
                               Roles.FirstOrDefault(role => role.RoleId == cachedSavedRole?.RoleId) ??
                               Roles.FirstOrDefault();
                BuildRoleChoices();
                RoleStatus = $"角色接口不可用，已使用客户端缓存 {Roles.Count} 个角色";
            }
            else
            {
                RoleStatus = $"角色列表读取失败：{ex.Message}";
            }
        }
    }

    /// <summary>
    /// 打开角色选择弹窗。
    /// </summary>
    [RelayCommand]
    private async Task OpenRoleDialog()
    {
        // 输入框不持久化，每次打开只显示已选角色行，不自动填入上次输入的 ID。
        ClearRoleInput();
        BuildRoleChoices();
        IsRoleDialogOpen = true;
        // 首次打开时补拉一次角色列表，避免初始化阶段接口失败后弹窗长期为空。
        if (Roles.Count == 0)
        {
            await RefreshRoles();
        }
    }

    /// <summary>
    /// 关闭角色选择弹窗。
    /// </summary>
    [RelayCommand]
    private void CloseRoleDialog()
    {
        IsRoleDialogOpen = false;
    }

    /// <summary>
    /// 选择弹窗中的角色。
    /// </summary>
    [RelayCommand]
    private void SelectRoleChoice(MapRoleChoice? choice)
    {
        if (choice is null)
        {
            return;
        }

        foreach (var item in RoleChoices)
        {
            item.IsSelected = ReferenceEquals(item, choice);
        }

        SelectedRoleChoice = choice;
        SelectedRoleAvatar = choice.Avatar;
    }

    /// <summary>
    /// 从角色列表移除角色；手动角色同步从配置移除，绑定角色记录移除 ID 防止刷新后自动加回。
    /// </summary>
    [RelayCommand]
    private void RemoveRole(MapRoleChoice? choice)
    {
        if (choice is null)
        {
            return;
        }

        var roleId = choice.RoleId;
        if (!choice.CanRemove || _boundRoleIds.Contains(roleId))
        {
            // 只有用户手动输入的角色允许移除；绑定角色始终由官方列表维护。
            return;
        }

        Roles.Remove(choice.Role);
        RoleChoices.Remove(choice);
        if (ReferenceEquals(SelectedRoleChoice, choice))
        {
            SelectedRoleChoice = null;
        }

        if (ConfigService.Config is { } config)
        {
            config.MapWindowConfig.SavedRoles.RemoveAll(profile => profile.RoleId == roleId);
            config.OnAnyChangedAction?.Invoke();
        }

        if (SelectedRole?.RoleId == roleId)
        {
            SelectedRole = null;
            SelectedRoleAvatar = null;
            RoleId = string.Empty;
            Server = string.Empty;
            RoleInputId = string.Empty;
            CompletionStatus = "未选择角色";
            if (ConfigService.Config is { } selectedConfig)
            {
                selectedConfig.MapWindowConfig.SelectedRoleId = string.Empty;
                selectedConfig.MapWindowConfig.SelectedRoleServer = string.Empty;
                selectedConfig.MapWindowConfig.SelectedRoleServerName = string.Empty;
                selectedConfig.MapWindowConfig.SelectedRoleNick = string.Empty;
                selectedConfig.MapWindowConfig.SelectedRoleIcon = string.Empty;
                selectedConfig.OnAnyChangedAction?.Invoke();
            }

            ResetCompletionStatesForRoleChange();
        }
    }

    /// <summary>
    /// 确认角色切换。
    /// </summary>
    [RelayCommand]
    private async Task ConfirmRoleDialog()
    {
        var roleId = RoleInputId.Trim();
        // 用户可能输入 ID 后立刻点确定；此时自动查找尚未完成，先等待/执行这次查找。
        if (roleId.Length >= 5 &&
            !string.Equals(SelectedRole?.RoleId, roleId, StringComparison.Ordinal))
        {
            _roleLookupCts?.Cancel();
            _roleLookupCts = new CancellationTokenSource();
            if (!await LookupRoleAsync(roleId, _roleLookupCts.Token, true))
            {
                return;
            }
        }

        if (SelectedRoleChoice is not null)
        {
            SelectedRole = SelectedRoleChoice.Role;
            SaveSelectedRole(SelectedRole, SelectedRoleChoice.IsManual);
        }

        IsRoleDialogOpen = false;
        // 角色切换后立即按新角色刷新完成状态，不再等待防抖计时器。
        RefreshCompletionNow();
    }

    /// <summary>
    /// 根据角色列表重建弹窗项目并加载头像。
    /// </summary>
    private void BuildRoleChoices()
    {
        var selectedId = SelectedRole?.RoleId ?? RoleId;
        SelectedRoleChoice = null;
        RoleChoices.Clear();
        var manualIds = ConfigService.Config?.MapWindowConfig.SavedRoles
            .Where(profile => profile.IsManual)
            .Select(profile => profile.RoleId)
            .ToHashSet(StringComparer.Ordinal) ?? [];
        foreach (var role in Roles
                     .Where(role => GodlikeRoleService.IsUsableRoleId(role.RoleId))
                     .GroupBy(role => role.RoleId, StringComparer.Ordinal)
                     .Select(group => group.First()))
        {
            var isManual = manualIds.Contains(role.RoleId);
            var choice = new MapRoleChoice(role)
            {
                IsSelected = role.RoleId == selectedId,
                IsManual = isManual,
                // 只有用户手动输入的角色才允许移除；绑定角色永不显示移除按钮。
                CanRemove = isManual && !_boundRoleIds.Contains(role.RoleId)
            };
            RoleChoices.Add(choice);
            var avatarUrl = GetRoleAvatarUrl(role);
            if (_roleAvatarCache.TryGetValue(avatarUrl, out var cachedAvatar))
            {
                choice.Avatar = cachedAvatar;
            }

            if (choice.IsSelected)
            {
                SelectedRoleChoice = choice;
                // 先用缓存头像占位，避免从上一位角色头像切换到空白或延迟闪烁。
                SelectedRoleAvatar = choice.Avatar;
            }

            if (choice.Avatar is null)
            {
                _ = LoadRoleAvatarAsync(choice);
            }
        }
    }

    /// <summary>
    /// 获取角色头像地址；没有头像时使用官方默认头像。
    /// </summary>
    private static string GetRoleAvatarUrl(GodlikeRole role)
    {
        return string.IsNullOrWhiteSpace(role.Icon) ? DefaultRoleAvatarUrl : role.Icon;
    }

    /// <summary>
    /// 下载角色头像；同一 URL 只下载一次，避免切换角色时反复等待网络。
    /// </summary>
    private async Task LoadRoleAvatarAsync(MapRoleChoice choice)
    {
        var url = GetRoleAvatarUrl(choice.Role);
        if (_roleAvatarCache.TryGetValue(url, out var cached))
        {
            ApplyRoleAvatar(choice, cached);
            return;
        }

        if (!_roleAvatarLoading.Add(url))
        {
            return;
        }

        try
        {
            var bytes = await IconHttpClient.GetByteArrayAsync(url);
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            _roleAvatarCache[url] = image;
            foreach (var item in RoleChoices.Where(item => GetRoleAvatarUrl(item.Role) == url).ToList())
            {
                ApplyRoleAvatar(item, image);
            }

            if (SelectedRole is not null && GetRoleAvatarUrl(SelectedRole) == url)
            {
                SelectedRoleAvatar = image;
            }
        }
        catch
        {
            // 头像失败时保留角色文字和默认占位。
        }
        finally
        {
            _roleAvatarLoading.Remove(url);
        }
    }

    /// <summary>
    /// 应用角色头像；选中角色同时更新顶栏头像。
    /// </summary>
    private void ApplyRoleAvatar(MapRoleChoice choice, BitmapImage image)
    {
        choice.Avatar = image;
        if (SelectedRole?.RoleId == choice.Role.RoleId)
        {
            SelectedRoleAvatar = image;
        }
    }

    /// <summary>
    /// 清空输入框但不触发自动查找/状态回写。
    /// </summary>
    private void ClearRoleInput()
    {
        _syncingRole = true;
        try
        {
            RoleInputId = string.Empty;
        }
        finally
        {
            _syncingRole = false;
        }
    }

    /// <summary>
    /// 输入角色 ID 后自动查找服务器并接入完成度数据。
    /// </summary>
    partial void OnRoleInputIdChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _roleLookupCts?.Cancel();
            if (!_syncingRole)
            {
                RoleStatus = "输入角色 ID 自动查找服务器";
            }

            return;
        }

        if (_syncingRole || value.Trim().Length < 5)
        {
            return;
        }

        _roleLookupCts?.Cancel();
        _roleLookupCts = new CancellationTokenSource();
        var normalized = value.Trim();
        _ = LookupRoleAsync(normalized, _roleLookupCts.Token, normalized.Length >= 10);
    }

    /// <summary>
    /// 手动查找角色；输入框按回车或点击“查找”时跳过防抖。
    /// </summary>
    [RelayCommand]
    private void LookupRole()
    {
        var roleId = RoleInputId.Trim();
        if (roleId.Length < 5)
        {
            RoleStatus = "请输入完整的角色 ID";
            return;
        }

        _roleLookupCts?.Cancel();
        _roleLookupCts = new CancellationTokenSource();
        _ = LookupRoleAsync(roleId, _roleLookupCts.Token, true);
    }

    private async Task<bool> LookupRoleAsync(string roleId, CancellationToken cancellationToken, bool skipDelay = false)
    {
        var requestVersion = ++_roleLookupVersion;
        try
        {
            if (!skipDelay)
            {
                await Task.Delay(450, cancellationToken);
            }

            RoleStatus = $"正在查找角色 {roleId} 的服务器...";
            var credentials = GodlikeCredentials.Load();
            var role = await GodlikeRoleService.FindRoleAsync(
                credentials,
                roleId,
                cancellationToken);
            if (role is null)
            {
                // 绑定列表和 CEF 日志都没有服务器时，先允许按角色 ID 直接查询；
                // 点位详情接口可以在 server 为空时返回公共数据。
                // 官方角色 ID 的前三位通常对应服务器编号，例如 026 -> 10026、040 -> 10040。
                var inferredServer = InferServerFromRoleId(roleId);
                role = new GodlikeRole
                {
                    RoleId = roleId,
                    Server = inferredServer,
                    ServerName = string.IsNullOrWhiteSpace(inferredServer) ? "未知服务器" : "正式服",
                    AppKey = "h72"
                };
            }

            if (requestVersion != _roleLookupVersion)
            {
                return false;
            }

            RunOnUiThread(() =>
            {
                if (requestVersion != _roleLookupVersion)
                {
                    return;
                }

                _syncingRole = true;
                try
                {
                    if (!Roles.Any(item => item.RoleId == role.RoleId))
                    {
                        Roles.Add(role);
                    }

                    SelectedRole = role;
                    RoleId = role.RoleId;
                    Server = role.Server;
                }
                finally
                {
                    _syncingRole = false;
                }

                // 先保存手动输入的角色，再重建列表，只有这类角色显示移除按钮。
                SaveSelectedRole(role, persistProfile: true);
                // 手动输入的角色可能不在绑定列表中，也要出现在角色选择弹窗里。
                BuildRoleChoices();
                RoleStatus = string.IsNullOrWhiteSpace(role.Server)
                    ? $"未找到服务器，按角色 ID {role.RoleId} 直接查询"
                    : $"已找到角色服务器：{role.Server}";
                CompletionStatus = $"当前角色：{role.DisplayText}";
                // 输入框只用于输入，不保留/回显已经生效的角色 ID。
                ClearRoleInput();
                RefreshCompletionNow();
            });
            return true;
        }
        catch (OperationCanceledException)
        {
            // 用户继续输入时取消上一次查找。
            return false;
        }
        catch (Exception ex)
        {
            RunOnUiThread(() => RoleStatus = $"角色查找失败：{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 切换角色时同步角色 ID 和服务器。
    /// </summary>
    partial void OnSelectedRoleChanged(GodlikeRole? value)
    {
        if (value is null)
        {
            return;
        }

        _syncingRole = true;
        RoleId = value.RoleId;
        Server = value.Server;
        _syncingRole = false;
        CompletionStatus = $"当前角色：{value.DisplayText}";
        var avatarUrl = GetRoleAvatarUrl(value);
        SelectedRoleAvatar = _roleAvatarCache.TryGetValue(avatarUrl, out var cachedAvatar)
            ? cachedAvatar
            : null;
        // 角色切换后先清空旧角色的完成状态，避免旧数据把新角色的点位提前隐藏。
        ResetCompletionStatesForRoleChange();
    }

    /// <summary>
    /// 清空旧角色的点位完成状态，让新角色数据返回前地图仍能正常显示点位和路线。
    /// </summary>
    private void ResetCompletionStatesForRoleChange()
    {
        // 角色切换后先允许当前分类点位立即显示并生成路线；
        // 新角色的完成状态返回后再隐藏已完成点位。
        _completionRefreshCompleted = true;
        _detailsCache.Clear();
        // 完成状态属于角色：切换角色只切换查询作用域，不再改写地图快照。
        RefreshFilters();
    }

    /// <summary>
    /// 重新读取官方客户端登录态。
    /// </summary>
    [RelayCommand]
    private async Task RefreshSession()
    {
        SessionStatus = "正在读取官方客户端登录态...";
        var state = await _sessionService.RefreshAsync();
        SessionStatus = state.Message;
    }

    /// <summary>
    /// 启动官方客户端，由用户完成登录。
    /// </summary>
    [RelayCommand]
    private void OpenOfficialClient()
    {
        var path = Environment.GetEnvironmentVariable("GODLIKE_CLIENT_DIR");
        if (string.IsNullOrWhiteSpace(path))
        {
            path = @"D:\GodlikeClient";
        }

        var executable = Path.Combine(path, "Start.exe");
        if (!File.Exists(executable))
        {
            SessionStatus = $"未找到官方客户端：{executable}";
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                WorkingDirectory = path
            });
            SessionStatus = "已启动官方客户端，登录后可刷新登录态";
            _sessionService.StartWatching();
        }
        catch (Exception ex)
        {
            SessionStatus = $"启动官方客户端失败：{ex.Message}";
        }
    }

    /// <summary>
    /// 按 10 个一批刷新当前筛选分类的完成状态。
    /// </summary>
    [RelayCommand]
    private async Task RefreshCategoryCompletion()
    {
        if (Snapshot is null)
        {
            CompletionStatus = "请先加载地图快照";
            return;
        }

        if (string.IsNullOrWhiteSpace(RoleId) || string.IsNullOrWhiteSpace(Server))
        {
            CompletionStatus = "请填写角色 ID 和服务器";
            return;
        }

        _completionCts?.Cancel();
        _completionCts = new CancellationTokenSource();
        var cancellationToken = _completionCts.Token;
        var targets = FilteredMarkers.ToList();
        if (targets.Count == 0)
        {
            CompletionStatus = "当前分类没有可刷新点位";
            return;
        }

        CompletionStatus = $"正在刷新 {targets.Count} 个点位...";
        try
        {
            var credentials = GodlikeCredentials.Load();
            var completed = 0;
            for (var offset = 0; offset < targets.Count; offset += 10)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = targets.Skip(offset).Take(10).ToList();
                var details = await GodlikeMapApi.LoadPointDetailsAsync(
                    credentials,
                    Snapshot.MapId,
                    batch.Select(marker => marker.Id).ToList(),
                    RoleId.Trim(),
                    Server.Trim(),
                    cancellationToken);
                var detailById = details.ToDictionary(detail => detail.PointId, StringComparer.Ordinal);
                foreach (var marker in batch)
                {
                    if (detailById.TryGetValue(marker.Id, out var detail))
                    {
                        ApplyMarkerCompletion(marker, detail);
                        if (detail.IsCompleted)
                        {
                            completed++;
                        }
                    }
                }

                CompletionStatus = $"完成状态 {Math.Min(offset + batch.Count, targets.Count)}/{targets.Count}";
            }

            CompletionStatus = $"完成状态已刷新：{completed}/{targets.Count} 已完成";
            RefreshFilters();
            RefreshDashboard();
        }
        catch (OperationCanceledException)
        {
            CompletionStatus = "完成状态刷新已取消";
        }
        catch (Exception ex)
        {
            CompletionStatus = $"完成状态刷新失败：{ex.Message}";
        }
    }

    /// <summary>
    /// 刷新路线、快捷入口、收藏、KOL、UGC 等附加信息。
    /// </summary>
    [RelayCommand]
    private async Task RefreshAuxiliaryInfo()
    {
        if (Snapshot is null)
        {
            AuxiliaryStatus = "请先加载地图快照";
            return;
        }

        AuxiliaryStatus = "正在加载附加信息...";
        try
        {
            var credentials = GodlikeCredentials.Load();
            AuxiliaryStatus = await GodlikeMapApi.LoadAuxiliaryInfoAsync(
                credentials,
                Snapshot.MapId,
                SearchText);
        }
        catch (Exception ex)
        {
            AuxiliaryStatus = $"附加信息加载失败：{ex.Message}";
        }
    }

    /// <summary>
    /// 清除完成度分类过滤。
    /// </summary>
    [RelayCommand]
    private void ClearCategory()
    {
        SelectedCategory = null;
    }

    partial void OnSelectedDashboardItemChanged(MapCategoryDashboardItem? value)
    {
        if (value is not null)
        {
            SelectedCategory = value.Filter;
        }
    }

    /// <summary>
    /// 重建分类完成度面板。
    /// </summary>
    private void RefreshDashboard()
    {
        if (Snapshot is null)
        {
            Dashboard.Clear();
            SelectedDashboardItem = null;
            return;
        }

        var selectedId = SelectedDashboardItem?.Id;
        var categoryById = Snapshot.Categories
            .GroupBy(category => category.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var items = new List<MapCategoryDashboardItem>();
        foreach (var filter in Categories)
        {
            if (!categoryById.TryGetValue(filter.Id, out var category))
            {
                continue;
            }

            var categoryIds = MarkerScope.GetCategoryIds(Snapshot.Categories, filter.Id);
            var markers = _allMarkers
                .Where(marker => (marker.CategoryId is { } primary && categoryIds.Contains(primary)) ||
                                 marker.CategoryIds.Any(categoryIds.Contains))
                .ToList();
            var groupName = category.ParentId is { } parentId &&
                            categoryById.TryGetValue(parentId, out var parent)
                ? parent.Name
                : category.Name;
            items.Add(new MapCategoryDashboardItem
            {
                Id = filter.Id,
                Name = filter.Name,
                GroupName = groupName,
                Filter = filter,
                TotalCount = markers.Count,
                CompletedCount = markers.Count(marker => GetCompletion(marker).IsCompleted)
            });
        }

        Dashboard.Clear();
        foreach (var item in items.OrderBy(item => item.DisplayText, StringComparer.Ordinal))
        {
            Dashboard.Add(item);
        }

        // 重新计算时保留当前分类选项，避免刷新后选中项丢失。
        SelectedDashboardItem = selectedId is null
            ? null
            : Dashboard.FirstOrDefault(item => item.Id == selectedId);
    }

    /// <summary>
    /// 快速定位到玩家当前位置，并持续保持玩家标记居中。
    /// </summary>
    [RelayCommand]
    private void QuickLocate()
    {
        if (PlayerWorldPosition is not { } world)
        {
            LocationStatus = "尚未读取到角色位置";
            return;
        }

        // 按世界坐标找到最近的官方区域，先切换地图，再进入玩家居中跟随。
        var target = RegionOptions
            .Where(option => !string.IsNullOrWhiteSpace(option.SubType))
            .OrderBy(option =>
            {
                var dx = option.WorldX - world.X;
                var dy = option.WorldY - world.Y;
                return (dx * dx) + (dy * dy);
            })
            .FirstOrDefault();
        if (target is not null && !ReferenceEquals(target, SelectedRegionOption))
        {
            SelectRegionOption(target);
        }

        if (PlayerMapPosition is not { } position)
        {
            LocationStatus = "角色坐标不属于当前地图";
            return;
        }

        IsPlayerCentered = true;
        FocusPoint = position;
        LocationStatus = target is null
            ? "已定位到角色当前位置"
            : $"已定位到 {target.Name}";
    }

    /// <summary>
    /// 切换全屏模式。
    /// </summary>
    [RelayCommand]
    private void ToggleFullScreen()
    {
        IsFullScreen = !IsFullScreen;
    }

    /// <summary>
    /// 切换跟随游戏窗口。
    /// </summary>
    [RelayCommand]
    private void ToggleFollowGame()
    {
        IsFollowGame = !IsFollowGame;
    }

    /// <summary>
    /// 应用快照并重建筛选数据。
    /// </summary>
    private void ApplySnapshot(MapSnapshot snapshot)
    {
        Snapshot = snapshot;
        MapLayerOverlayCatalog.Apply(snapshot);
        OnPropertyChanged(nameof(MapName));
        _allMarkers.Clear();
        foreach (var marker in snapshot.Markers)
        {
            // 旧快照可能保存了错误的滹沱投影方向，按当前投影重新计算。
            if (!string.IsNullOrWhiteSpace(marker.MapSubType))
            {
                marker.MapPosition = GodlikeProjection.WorldToMap(
                    marker.MapSubType,
                    marker.WorldPosition.X,
                    marker.WorldPosition.Y);
            }

            _allMarkers.Add(marker);
        }

        SelectedMarkerIds.Clear();
        SelectedMarkers.Clear();
        CurrentRoute = null;
        SelectedMarker = null;

        Regions.Clear();
        foreach (var region in snapshot.Regions)
        {
            // 旧快照可能保存过期的瓦片模板；按当前官方投影配置纠正。
            var subType = region.SubTypes.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(subType))
            {
                var projection = GodlikeProjection.Get(subType);
                region.MapZone = projection.MapZone;
                region.TileUrlTemplate = projection.TileUrlTemplate;
                region.TileGroup = projection.TileGroup;
                region.TileExtent = projection.TileExtent;
            }

            for (var index = 0; index < region.Layers.Count; index++)
            {
                var layer = region.Layers[index];
                layer.Name = FormatLayerName(layer.Id, index, subType);
                layer.IconOnly = subType == "2";
                if (subType == "2" && (layer.Id is "0" or "99"))
                {
                    // 河西前后位面使用官方时间图标。
                    var icon = layer.Id == "99" ? "hexi_back.png" : "hexi_front.png";
                    layer.IconUrl = $"pack://application:,,,/BetterYYSLS;component/Assets/Map/{icon}";
                    layer.ActiveIconUrl = layer.IconUrl;
                }
                else
                {
                    layer.IconUrl = layer.Id == "0"
                        ? "pack://application:,,,/BetterYYSLS;component/Assets/Map/layer-floor.png"
                        : "pack://application:,,,/BetterYYSLS;component/Assets/Map/layer-icon.png";
                    layer.ActiveIconUrl = layer.Id == "0"
                        ? "pack://application:,,,/BetterYYSLS;component/Assets/Map/layer-floor-active.png"
                        : "pack://application:,,,/BetterYYSLS;component/Assets/Map/layer-icon-active.png";
                }
            }

            Regions.Add(region);
        }

        // 旧快照可能缺少个别 series；保留这些点位并放入“未分类”，避免数据静默丢失。
        var knownCategoryIds = snapshot.Categories
            .Select(category => category.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var categoryId in snapshot.Markers
                     .SelectMany(marker => marker.CategoryIds.Append(marker.CategoryId))
                     .Where(id => !string.IsNullOrWhiteSpace(id) && !knownCategoryIds.Contains(id))
                     .Distinct(StringComparer.Ordinal))
        {
            snapshot.Categories.Add(new MapCategory
            {
                Id = categoryId,
                Name = "未分类",
                Order = int.MaxValue,
                IsVisibleByDefault = true
            });
            knownCategoryIds.Add(categoryId);
        }

        RegionOptions.Clear();
        foreach (var option in new[]
                 {
                     new MapRegionOption { Name = "清河", SubType = "1", WorldX = -1297.0112, WorldY = -2989.876 },
                     new MapRegionOption { Name = "开封", SubType = "1", WorldX = 207.0, WorldY = -850.0 },
                     new MapRegionOption { Name = "皇宫", SubType = "4", WorldX = 214.3280182, WorldY = -1841.783691 },
                     new MapRegionOption { Name = "河西", SubType = "2", WorldX = -1751.0, WorldY = 1426.0 },
                     new MapRegionOption { Name = "不见山", SubType = "1", WorldX = -1837.0, WorldY = -5092.0 },
                     new MapRegionOption { Name = "青州", SubType = "1", WorldX = -3724.05, WorldY = -4338.82 },
                     new MapRegionOption { Name = "江南", SubType = "1", WorldX = -4038.0690166338, WorldY = 4939.499848543 },
                     new MapRegionOption { Name = "未央城", SubType = "5", WorldX = -10435.466796875, WorldY = 3629.430908203125, IsNew = true },
                     // 子区域使用官方锚点坐标；同一 mapSubType 内按最近区域中心筛选。
                     new MapRegionOption { Name = "百草野", SubType = "1", WorldX = -1918.722046, WorldY = -2808.292969, ParentName = "清河" },
                     new MapRegionOption { Name = "隐月山", SubType = "1", WorldX = -847.6376343, WorldY = -2785.507568, ParentName = "清河" },
                     new MapRegionOption { Name = "善妙洲", SubType = "1", WorldX = -1224.051392, WorldY = -3543.760254, ParentName = "清河" },
                     new MapRegionOption { Name = "开封城", SubType = "1", WorldX = 208.6117401, WorldY = -1595.603027, ParentName = "开封" },
                     new MapRegionOption { Name = "太仓粟", SubType = "1", WorldX = -384.3171082, WorldY = -352.2505493, ParentName = "开封" },
                     new MapRegionOption { Name = "琼林苑", SubType = "1", WorldX = 640.87323, WorldY = -629.3135986, ParentName = "开封" },
                     new MapRegionOption { Name = "博浪沙", SubType = "1", WorldX = 1080.785767, WorldY = -2089.277588, ParentName = "开封" },
                     new MapRegionOption { Name = "内诸司", SubType = "4", WorldX = 53.4326515197753, WorldY = -2205.76440429687, ParentName = "皇宫" },
                     new MapRegionOption { Name = "后寝", SubType = "4", WorldX = 460.056457519531, WorldY = -2381.92163085937, ParentName = "皇宫" },
                     new MapRegionOption { Name = "前朝", SubType = "4", WorldX = 290.80679321289, WorldY = -1760.35656738281, ParentName = "皇宫" },
                     new MapRegionOption { Name = "玉门关", SubType = "2", WorldX = 62.84661102, WorldY = 407.5228577, ParentName = "河西" },
                     new MapRegionOption { Name = "凉州", SubType = "2", WorldX = -1621.38049316406, WorldY = 1261.39916992187, ParentName = "河西" },
                     new MapRegionOption { Name = "秦川", SubType = "2", WorldX = -3468.952051, WorldY = 3397.958008, ParentName = "河西" },
                     new MapRegionOption { Name = "深泽", SubType = "1", WorldX = -1737.19348144531, WorldY = -4290.693359375, ParentName = "不见山" },
                     new MapRegionOption { Name = "绥乡", SubType = "1", WorldX = -1722.68017578125, WorldY = -4897.4912109375, ParentName = "不见山" },
                     new MapRegionOption { Name = "天陉", SubType = "1", WorldX = -1781.83764648437, WorldY = -5623.939453125, ParentName = "不见山" },
                     // 滹沱使用独立的 map3 瓦片，但菜单层级归属不见山。
                     new MapRegionOption { Name = "滹沱", SubType = "3", WorldX = -2151.07592773437, WorldY = 2884.89526367187, ParentName = "不见山" },
                     new MapRegionOption { Name = "蓬山", SubType = "1", WorldX = -5462.34228515625, WorldY = -4410.666015625, ParentName = "青州" },
                     new MapRegionOption { Name = "东岳", SubType = "1", WorldX = -3596.89892578125, WorldY = -4229.50927734375, ParentName = "青州" },
                     new MapRegionOption { Name = "杭州", SubType = "1", WorldX = -4031.2927867754, WorldY = 5102.1351329668, ParentName = "江南" },
                     new MapRegionOption { Name = "长乐岛", SubType = "5", WorldX = -10435.466796875, WorldY = 3629.430908203125, ParentName = "未央城", IsNew = true },
                     new MapRegionOption { Name = "十二瑶台", SubType = "5", WorldX = -10434.3232421875, WorldY = 4051.00927734375, ParentName = "未央城", IsNew = true }
                 })
        {
            RegionOptions.Add(option);
        }

        RegionGroups.Clear();
        foreach (var topLevel in RegionOptions.Where(option => option.ParentName is null))
        {
            var group = new MapRegionGroup
            {
                Name = topLevel.Name,
                Region = topLevel
            };
            foreach (var child in RegionOptions.Where(option =>
                         option.ParentName == topLevel.Name))
            {
                group.Children.Add(child);
            }

            // 二级子区域默认收起，避免下拉面板一次展开过长。
            group.IsExpanded = false;
            RegionGroups.Add(group);
        }

        Categories.Clear();
        foreach (var category in snapshot.Categories.OrderBy(c => c.Order))
        {
            var filter = new MapCategoryFilter
            {
                Id = category.Id,
                ParentId = category.ParentId,
                Name = category.Name,
                IconUrl = category.Icon,
                IsVisible = false
            };
            filter.PropertyChanged += OnCategoryFilterChanged;
            Categories.Add(filter);
            _ = LoadCategoryIconAsync(filter);
        }

        CategoryGroups.Clear();
        foreach (var top in Categories.Where(category =>
                     string.IsNullOrWhiteSpace(category.ParentId) &&
                     category.Id != "anchor" &&
                     category.Name != "未分类"))
        {
            var children = Categories
                .Where(category => category.ParentId == top.Id)
                .ToList();
            CategoryGroups.Add(new MapCategoryGroup
            {
                Id = top.Id,
                Name = top.Name,
                Categories = children.Count > 0 ? children : [top]
            });
        }

        SelectedCategoryGroup = null;

        RestoreMapSelection();
        RefreshDashboard();
        RefreshFilters();
    }

    partial void OnSelectedRegionChanged(MapRegion? value)
    {
        // 切换区域后玩家可能不在当前地图，退出玩家居中跟随。
        IsPlayerCentered = false;
        var subType = value?.SubTypes.FirstOrDefault() ?? "1";
        _locationService.SubType = subType;
        if (PlayerWorldPosition is { } world)
        {
            PlayerMapPosition = GodlikeProjection.WorldToMap(subType, world.X, world.Y);
        }

        Layers.Clear();
        if (value is not null)
        {
            foreach (var layer in value.Layers)
            {
                Layers.Add(layer);
            }
        }

        SelectedLayer = Layers.FirstOrDefault();
        RefreshFilters();
        ScheduleCompletionRefresh();
    }

    partial void OnSelectedLayerChanged(MapLayer? value)
    {
        _completionRefreshCompleted = false;
        RefreshFilters();
        // 切位面后需要重新读取该位面点位的完成状态，否则未知状态点位会被隐藏且路线为空。
        ScheduleCompletionRefresh();
        SaveMapSelection();
    }

    partial void OnSearchTextChanged(string value)
    {
        _completionRefreshCompleted = false;
        RefreshFilters();
    }

    partial void OnHideCompletedChanged(bool value)
    {
        RefreshFilters();
        if (value)
        {
            ScheduleCompletionRefresh();
        }
    }

    /// <summary>
    /// 合并短时间内的分类切换，避免每个勾选都立即发起完成状态请求。
    /// </summary>
    private void ScheduleCompletionRefresh()
    {
        if (!HideCompleted)
        {
            return;
        }

        _completionDebounceTimer.Stop();
        _completionDebounceTimer.Start();
    }

    /// <summary>
    /// 立即刷新完成状态；若上一轮仍在执行，则在本轮结束后补刷，确保切换角色后拿到新角色的数据。
    /// </summary>
    private void RefreshCompletionNow()
    {
        _completionDebounceTimer.Stop();
        if (_completionRefreshRunning)
        {
            _completionRefreshPending = true;
            return;
        }

        _ = RefreshCompletionStatesAsync();
    }

    /// <summary>
    /// 获取当前区域、分类和搜索条件下待读取完成状态的点位。
    /// 不应用 HideCompleted，因为未知状态也需要先请求。
    /// </summary>
    private List<MapMarker> GetCompletionCandidates()
    {
        if (Snapshot is null)
        {
            return [];
        }

        return MarkerScope.CompletionCandidates(CreateMarkerScopeRequest());
    }

    /// <summary>
    /// 批量读取当前筛选点位的完成状态，供“隐藏已完成”使用。
    /// </summary>
    private async Task RefreshCompletionStatesAsync()
    {
        if (_completionRefreshRunning || Snapshot is null)
        {
            return;
        }

        var targets = GetCompletionCandidates();
        if (targets.Count == 0)
        {
            _completionRefreshCompleted = true;
            RefreshFilters();
            return;
        }

        _completionRefreshRunning = true;
        try
        {
            var credentials = GodlikeCredentials.Load();
            var detailsById = new ConcurrentDictionary<string, MapMarkerDetails>(StringComparer.Ordinal);
            using var semaphore = new SemaphoreSlim(6);
            var batches = targets.Chunk(20).ToList();
            var finished = 0;
            await Task.WhenAll(batches.Select(async batch =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var details = await GodlikeMapApi.LoadPointDetailsAsync(
                        credentials,
                        Snapshot.MapId,
                        batch.Select(marker => marker.Id).ToList(),
                        string.IsNullOrWhiteSpace(RoleId) ? null : RoleId.Trim(),
                        string.IsNullOrWhiteSpace(Server) ? null : Server.Trim());
                    foreach (var detail in details)
                    {
                        detailsById[detail.PointId] = detail;
                    }
                }
                catch
                {
                    // 单个批次失败时继续刷新其他批次。
                }
                finally
                {
                    semaphore.Release();
                    var current = Interlocked.Increment(ref finished);
                    if (current % 10 == 0 || current == batches.Count)
                    {
                        CompletionStatus = $"完成状态 {current * 20}/{targets.Count}";
                    }
                }
            }));

            var completed = 0;
            foreach (var marker in targets)
            {
                if (detailsById.TryGetValue(marker.Id, out var detail))
                {
                    ApplyMarkerCompletion(marker, detail);
                    if (detail.IsCompleted)
                    {
                        completed++;
                    }
                }
            }

            CompletionStatus = $"完成状态已刷新：{completed}/{targets.Count} 已完成";
            RefreshFilters();
        }
        catch (Exception ex)
        {
            CompletionStatus = $"完成状态刷新失败：{ex.Message}";
        }
        finally
        {
            _completionRefreshRunning = false;
            _completionRefreshCompleted = true;
            // 接口失败或部分点位没有详情时，刷新后允许显示这些点位并继续生成路线。
            RefreshFilters();
            if (_completionRefreshPending)
            {
                _completionRefreshPending = false;
                _ = RefreshCompletionStatesAsync();
            }
        }
    }

    partial void OnSelectedCategoryChanged(MapCategoryFilter? value)
    {
        RefreshFilters();
    }

    partial void OnSelectedCategoryGroupChanged(MapCategoryGroup? value)
    {
        VisibleCategoryFilters.Clear();
        if (value is null)
        {
            return;
        }

        // 切换一级玩法时只保留当前玩法的分类，避免上一个玩法仍可见并参与路线规划。
        SelectedCategory = null;
        foreach (var category in Categories)
        {
            category.IsVisible = false;
        }

        // 一级玩法只负责展示其下分类，不默认勾选全部子分类；
        // 否则选中“探索收集”等大分组会立即把几十个分类一起纳入路线，看起来像全图路线。
        foreach (var category in value.Categories)
        {
            VisibleCategoryFilters.Add(category);
        }
    }

    private void OnCategoryFilterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MapCategoryFilter.IsVisible))
        {
            // 用户勾选分类卡片后由可见分类集合决定筛选，不再沿用仪表盘的单分类选择。
            SelectedCategory = null;
            _completionRefreshCompleted = false;
            RefreshFilters();
            ScheduleCompletionRefresh();
        }
    }

    /// <summary>
    /// 官方位面显示名称：普通地图 0 为地面、1-4 为一层到四层；
    /// 河西使用前/后两个历史位面（layer 0/99）。
    /// </summary>
    private static string FormatLayerName(string layerId, int fallbackIndex, string? subType = null)
    {
        if (subType == "2")
        {
            return layerId switch
            {
                "0" => "前",
                "99" => "后",
                _ => $"位面 {layerId}"
            };
        }

        if (subType == "5")
        {
            return layerId switch
            {
                "0" => "地面",
                "1" => "海底",
                _ => $"位面 {layerId}"
            };
        }

        if (layerId == "0")
        {
            return "地面";
        }

        var number = int.TryParse(layerId, out var parsed) ? parsed : fallbackIndex;
        var names = new[] { "零", "一", "二", "三", "四", "五", "六", "七", "八", "九" };
        return number >= 0 && number < names.Length ? $"{names[number]}层" : $"{layerId}层";
    }

    /// <summary>
    /// 地图渲染使用的完成状态查询。
    /// </summary>
    public Func<MapMarker, MarkerCompletion> CompletionLookup => GetCompletion;

    /// <summary>
    /// 查询当前角色的标记完成状态。
    /// </summary>
    private MarkerCompletion GetCompletion(MapMarker marker)
    {
        return _completionStore.Get(RoleId, marker.Id);
    }

    /// <summary>
    /// 构造标记范围请求；筛选、完成状态和路线共享同一份选择状态。
    /// </summary>
    private MarkerScopeRequest CreateMarkerScopeRequest()
    {
        return new MarkerScopeRequest
        {
            Markers = _allMarkers,
            SelectedRegionName = SelectedRegionOption?.Name,
            SelectedRegionParentName = SelectedRegionOption?.ParentName,
            SelectedRegionSubType = SelectedRegionOption?.SubType,
            SelectedRegionId = SelectedRegion?.Id,
            RegionAnchors = RegionOptions
                .Select(option => new MarkerRegionAnchor(
                    option.Name,
                    option.ParentName,
                    option.WorldX,
                    option.WorldY))
                .ToList(),
            SelectedLayerId = SelectedLayer?.Id,
            VisibleCategoryIds = Categories
                .Where(category => category.IsVisible)
                .Select(category => category.Id)
                .ToHashSet(StringComparer.Ordinal),
            SelectedCategoryId = SelectedCategory?.Id,
            Categories = Snapshot?.Categories ?? [],
            SearchText = SearchText,
            HideCompleted = HideCompleted,
            ShowUnknownCompletion = string.IsNullOrWhiteSpace(RoleId) || _completionRefreshCompleted,
            CompletionOf = GetCompletion
        };
    }

    /// <summary>
    /// 按区域、图层、分类和搜索重建标记列表。
    /// </summary>
    private void RefreshFilters()
    {
        if (Snapshot is null)
        {
            return;
        }

        var markers = MarkerScope.Visible(CreateMarkerScopeRequest());

        FilteredMarkers.Clear();
        foreach (var marker in markers)
        {
            FilteredMarkers.Add(marker);
        }

        // 筛选结果变化时先撤掉上一轮路线，否则新路线计算完成前会暂时画出旧筛选的线。
        CancelAutoRoute();
        CurrentRoute = null;
        RouteSummary = "正在按当前筛选规划路线...";
        ScheduleAutoRoute();
        RefreshSelectedMarkers();
    }

    /// <summary>
    /// 防抖调度自动路线规划，避免连续筛选时反复规划。
    /// </summary>
    private void ScheduleAutoRoute()
    {
        _autoRouteDebounceTimer.Stop();
        _autoRouteDebounceTimer.Start();
    }

    /// <summary>
    /// 取消上一轮自动路线规划。
    /// </summary>
    private void CancelAutoRoute()
    {
        var cts = Interlocked.Exchange(ref _autoRouteCts, null);
        if (cts is null)
        {
            return;
        }

        cts.Cancel();
        cts.Dispose();
    }

    /// <summary>
    /// 在后台根据当前区域和已勾选玩法自动生成路线。
    /// </summary>
    private async Task UpdateAutoRouteAsync()
    {
        var snapshot = Snapshot;
        if (snapshot is null)
        {
            CancelAutoRoute();
            return;
        }

        // 路线目标与筛选、完成状态共享同一标记范围规则。
        var scope = CreateMarkerScopeRequest();
        var targets = MarkerScope.RouteTargets(scope);
        if (targets.Count == 0)
        {
            CancelAutoRoute();
            CurrentRoute = null;
            RouteSummary = "当前分类没有可规划点位";
            return;
        }

        var maxPoints = Math.Min(targets.Count, AutoRouteMaxPoints);
        var targetIds = targets.Select(marker => marker.Id).ToList();
        var start = PlayerWorldPosition;
        var layerId = string.IsNullOrWhiteSpace(SelectedLayer?.Id) ? null : SelectedLayer.Id;
        var teleportCost = TeleportCost;
        var areaTeleports = snapshot.Markers
            .Where(marker => marker.IsTeleport &&
                             MarkerScope.InSelectedRegion(marker, scope))
            .Select(marker => marker.Id)
            .ToHashSet(StringComparer.Ordinal);
        // 位面与跨层策略由传送节点选择 module 负责，ViewModel 只提供区域候选集合。
        var allowedTeleportIds = areaTeleports;

        var cts = new CancellationTokenSource();
        var previousCts = Interlocked.Exchange(ref _autoRouteCts, cts);
        previousCts?.Cancel();
        previousCts?.Dispose();

        try
        {
            var route = await Task.Run(
                () => MacroRouteAssembler.PlanTeleportClusteredRoute(
                    snapshot,
                    targetIds,
                    maxPoints,
                    start,
                    layerId,
                    teleportCost,
                    600d,
                    allowedTeleportIds,
                    cancellationToken: cts.Token),
                cts.Token);
            if (cts.IsCancellationRequested || !ReferenceEquals(_autoRouteCts, cts))
            {
                return;
            }

            CurrentRoute = route;
            if (route.OrderedMarkers.Count == 0)
            {
                RouteSummary = "当前区域没有可规划玩法点；若为新区域，请先同步 Godlike 数据";
                return;
            }

            var truncated = targets.Count > maxPoints
                ? $"（已按传送点分层抽取 {maxPoints} 点）"
                : string.Empty;
            RouteSummary = $"自动路线：{route.OrderedMarkers.Count} 点 · " +
                           $"{route.ClusterCount} 簇 · " +
                           $"步行 {route.WalkingDistance:F0} · " +
                           $"传送 {route.TeleportCount} 次 · 总代价 {route.TotalCost:F0} · " +
                           $"c={teleportCost:F0}{truncated}";
        }
        catch (OperationCanceledException)
        {
            // 筛选变化导致的取消不需要提示。
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_autoRouteCts, cts))
            {
                CurrentRoute = null;
                RouteSummary = $"自动路线失败：{ex.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(_autoRouteCts, cts))
            {
                _autoRouteCts = null;
                cts.Dispose();
            }
        }
    }

    /// <summary>
    /// 根据选中标识重建选中标记。
    /// </summary>
    private void RefreshSelectedMarkers()
    {
        var selected = _allMarkers
            .Where(marker => SelectedMarkerIds.Contains(marker.Id))
            .ToList();
        SelectedMarkers.Clear();
        foreach (var marker in selected)
        {
            SelectedMarkers.Add(marker);
        }
    }
}

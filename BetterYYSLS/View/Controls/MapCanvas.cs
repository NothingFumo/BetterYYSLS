using BetterYYSLS.Core.Godlike;
using BetterYYSLS.Core.Map;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BetterYYSLS.View.Controls;

/// <summary>
/// 地图标记选择事件参数。
/// </summary>
public sealed class MapMarkerEventArgs(MapMarker marker) : EventArgs
{
    /// <summary>
    /// 被点击的标记。
    /// </summary>
    public MapMarker Marker { get; } = marker;
}

/// <summary>
/// 使用 WPF 原生绘制的地图视口。
/// 负责网格、标记、宏观路线、缩放、拖动和点击选择。
/// </summary>
public sealed class MapCanvas : FrameworkElement
{
    private static readonly Brush BackgroundBrush = new SolidColorBrush(Color.FromRgb(12, 15, 21));
    private static readonly Dictionary<int, Pen> TripPens = [];
    private static readonly Pen SelectedPen = CreatePen(Color.FromArgb(240, 255, 255, 255), 2);
    private static readonly Typeface LabelTypeface = new("Microsoft YaHei UI");
    private static readonly Brush LabelBrush = new SolidColorBrush(Color.FromArgb(230, 240, 240, 240));
    private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromArgb(180, 180, 190, 200));
    private static readonly BitmapSource? PlayerMarkerIcon = LoadPlayerMarkerIcon();
    private static readonly HttpClient TileHttpClient = CreateHttpClient(16);
    private static readonly HttpClient MarkerIconHttpClient = CreateHttpClient(4);
    private readonly Dictionary<string, BitmapSource> _tileImages = [];
    private readonly HashSet<string> _tileLoading = [];
    private readonly Dictionary<string, int> _tileRetryCount = [];
    private readonly Dictionary<string, BitmapSource> _markerIcons = [];
    private readonly HashSet<string> _markerIconLoading = [];
    private readonly Dictionary<string, BitmapSource> _layerImages = [];
    private readonly HashSet<string> _layerImageLoading = [];
    private static readonly Color[] MarkerColors =
    [
        Color.FromRgb(235, 190, 80),
        Color.FromRgb(100, 180, 255),
        Color.FromRgb(120, 210, 150),
        Color.FromRgb(230, 130, 160),
        Color.FromRgb(190, 150, 245),
        Color.FromRgb(245, 160, 90)
    ];

    private readonly List<(MapMarker Marker, Point Point)> _screenMarkers = [];
    private NotifyCollectionChangedEventHandler? _markersHandler;
    private NotifyCollectionChangedEventHandler? _selectedHandler;
    private bool _isDragging;
    private bool _hasDragged;
    private Point _dragStart;
    private Vector _pan;
    private double _zoom = 16;
    private MapPoint? _lastPlayerPosition;
    private double _playerHeading;

    static MapCanvas()
    {
        FocusableProperty.OverrideMetadata(typeof(MapCanvas), new FrameworkPropertyMetadata(true));
    }

    /// <summary>
    /// 地图标记集合。
    /// </summary>
    public static readonly DependencyProperty MarkersProperty = DependencyProperty.Register(
        nameof(Markers),
        typeof(IEnumerable<MapMarker>),
        typeof(MapCanvas),
        new FrameworkPropertyMetadata(null, OnMarkersChanged));

    /// <summary>
    /// 当前选中标记标识。
    /// </summary>
    public static readonly DependencyProperty SelectedIdsProperty = DependencyProperty.Register(
        nameof(SelectedIds),
        typeof(IList<string>),
        typeof(MapCanvas),
        new FrameworkPropertyMetadata(null, OnSelectedIdsChanged));

    /// <summary>
    /// 当前区域，用于世界坐标到地图坐标的标定。
    /// </summary>
    public static readonly DependencyProperty RegionProperty = DependencyProperty.Register(
        nameof(Region),
        typeof(MapRegion),
        typeof(MapCanvas),
        new FrameworkPropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// 当前位面层级，0 为地面。
    /// </summary>
    public static readonly DependencyProperty LayerLevelProperty = DependencyProperty.Register(
        nameof(LayerLevel),
        typeof(int),
        typeof(MapCanvas),
        new FrameworkPropertyMetadata(0, OnVisualPropertyChanged));

    /// <summary>
    /// 当前区域聚焦点。
    /// </summary>
    public static readonly DependencyProperty FocusPointProperty = DependencyProperty.Register(
        nameof(FocusPoint),
        typeof(MapPoint?),
        typeof(MapCanvas),
        new FrameworkPropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// 当前选中位面 ID，用于绘制官方多位面覆盖素材。
    /// </summary>
    public static readonly DependencyProperty LayerIdProperty = DependencyProperty.Register(
        nameof(LayerId),
        typeof(string),
        typeof(MapCanvas),
        new FrameworkPropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// 标记完成状态查询。
    /// </summary>
    public static readonly DependencyProperty CompletionOfProperty = DependencyProperty.Register(
        nameof(CompletionOf),
        typeof(Func<MapMarker, MarkerCompletion>),
        typeof(MapCanvas),
        new FrameworkPropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// 当前宏观路线。
    /// </summary>
    public static readonly DependencyProperty RouteProperty = DependencyProperty.Register(
        nameof(Route),
        typeof(MacroRoute),
        typeof(MapCanvas),
        new FrameworkPropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// 当前玩家地图坐标。
    /// </summary>
    public static readonly DependencyProperty PlayerPositionProperty = DependencyProperty.Register(
        nameof(PlayerPosition),
        typeof(MapPoint?),
        typeof(MapCanvas),
        new FrameworkPropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// 是否让玩家标记始终位于视口正中。
    /// </summary>
    public static readonly DependencyProperty FollowPlayerProperty = DependencyProperty.Register(
        nameof(FollowPlayer),
        typeof(bool),
        typeof(MapCanvas),
        new FrameworkPropertyMetadata(false, OnVisualPropertyChanged));

    /// <summary>
    /// 地图底图和瓦片的不透明度；路线与标记始终保持不透明。
    /// </summary>
    public static readonly DependencyProperty BaseOpacityProperty = DependencyProperty.Register(
        nameof(BaseOpacity),
        typeof(double),
        typeof(MapCanvas),
        new FrameworkPropertyMetadata(1d, OnVisualPropertyChanged));

    /// <summary>
    /// 地图缩放倍率，可供外部滑杆双向绑定。
    /// </summary>
    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom),
        typeof(double),
        typeof(MapCanvas),
        new FrameworkPropertyMetadata(
            16d,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnZoomChanged));

    /// <summary>
    /// 标记点击事件。
    /// </summary>
    public event EventHandler<MapMarkerEventArgs>? MarkerSelected;

    /// <summary>
    /// 用户开始拖动地图时触发，用于退出玩家居中跟随。
    /// </summary>
    public event EventHandler? FollowPlayerCancelled;

    /// <summary>
    /// 地图缩放倍率。
    /// </summary>
    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    /// <summary>
    /// 创建独立连接池的 HTTP 客户端，避免大量标记图标请求占满瓦片下载连接。
    /// </summary>
    private static HttpClient CreateHttpClient(int maxConnections)
    {
        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = maxConnections,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
    }

    /// <summary>
    /// 清空底图瓦片缓存；切图后强制重新加载，避免复用另一区域的空白/失败瓦片。
    /// </summary>
    public void ClearTileCache()
    {
        _tileImages.Clear();
        _tileRetryCount.Clear();
        InvalidateVisual();
    }

    /// <summary>
    /// 地图标记集合。
    /// </summary>
    public IEnumerable<MapMarker>? Markers
    {
        get => (IEnumerable<MapMarker>?)GetValue(MarkersProperty);
        set => SetValue(MarkersProperty, value);
    }

    /// <summary>
    /// 标记完成状态查询。
    /// </summary>
    public Func<MapMarker, MarkerCompletion>? CompletionOf
    {
        get => (Func<MapMarker, MarkerCompletion>?)GetValue(CompletionOfProperty);
        set => SetValue(CompletionOfProperty, value);
    }

    /// <summary>
    /// 当前选中标记标识。
    /// </summary>
    public IList<string>? SelectedIds
    {
        get => (IList<string>?)GetValue(SelectedIdsProperty);
        set => SetValue(SelectedIdsProperty, value);
    }

    /// <summary>
    /// 当前区域。
    /// </summary>
    public MapRegion? Region
    {
        get => (MapRegion?)GetValue(RegionProperty);
        set => SetValue(RegionProperty, value);
    }

    /// <summary>
    /// 当前位面层级。
    /// </summary>
    public int LayerLevel
    {
        get => (int)GetValue(LayerLevelProperty);
        set => SetValue(LayerLevelProperty, value);
    }

    /// <summary>
    /// 当前区域聚焦点。
    /// </summary>
    public MapPoint? FocusPoint
    {
        get => (MapPoint?)GetValue(FocusPointProperty);
        set => SetValue(FocusPointProperty, value);
    }

    /// <summary>
    /// 当前选中位面 ID，用于绘制官方多位面覆盖素材。
    /// </summary>
    public string? LayerId
    {
        get => (string?)GetValue(LayerIdProperty);
        set => SetValue(LayerIdProperty, value);
    }

    /// <summary>
    /// 当前宏观路线。
    /// </summary>
    public MacroRoute? Route
    {
        get => (MacroRoute?)GetValue(RouteProperty);
        set => SetValue(RouteProperty, value);
    }

    /// <summary>
    /// 当前玩家地图坐标。
    /// </summary>
    public MapPoint? PlayerPosition
    {
        get => (MapPoint?)GetValue(PlayerPositionProperty);
        set => SetValue(PlayerPositionProperty, value);
    }

    /// <summary>
    /// 是否让玩家标记始终位于视口正中。
    /// </summary>
    public bool FollowPlayer
    {
        get => (bool)GetValue(FollowPlayerProperty);
        set => SetValue(FollowPlayerProperty, value);
    }

    /// <summary>
    /// 地图底图和瓦片的不透明度；路线与标记始终保持不透明。
    /// </summary>
    public double BaseOpacity
    {
        get => (double)GetValue(BaseOpacityProperty);
        set => SetValue(BaseOpacityProperty, value);
    }

    /// <summary>
    /// 放大地图。
    /// </summary>
    public void ZoomIn()
    {
        Zoom = Math.Clamp(Zoom * 1.2, 0.2, 128);
    }

    /// <summary>
    /// 缩小地图。
    /// </summary>
    public void ZoomOut()
    {
        Zoom = Math.Clamp(Zoom / 1.2, 0.2, 128);
    }

    /// <summary>
    /// 自定义绘制的 FrameworkElement 默认不保证整个矩形命中，显式接管命中测试。
    /// </summary>
    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        return new PointHitTestResult(this, hitTestParameters.HitPoint);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        Zoom = Math.Clamp(Zoom * (e.Delta > 0 ? 1.15 : 0.87), 0.2, 128);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _dragStart = e.GetPosition(this);
        _hasDragged = false;
        _isDragging = true;
        CaptureMouse();
        Focus();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_isDragging)
        {
            return;
        }

        var current = e.GetPosition(this);
        var delta = current - _dragStart;
        if (Math.Abs(delta.X) + Math.Abs(delta.Y) > 3)
        {
            if (FollowPlayer)
            {
                // 手动拖动时退出跟随，否则拖动会被居中逻辑持续忽略。
                FollowPlayerCancelled?.Invoke(this, EventArgs.Empty);
            }

            _hasDragged = true;
            _pan += delta;
            _dragStart = current;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        ReleaseMouseCapture();
        if (_hasDragged)
        {
            e.Handled = true;
            return;
        }

        var position = e.GetPosition(this);
        var hit = _screenMarkers
            .Select(item => (item.Marker, Distance: (item.Point - position).Length))
            .Where(item => item.Distance <= 14)
            .OrderBy(item => item.Distance)
            .FirstOrDefault();
        if (hit.Marker is not null)
        {
            MarkerSelected?.Invoke(this, new MapMarkerEventArgs(hit.Marker));
        }

        e.Handled = true;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        Zoom = 16;
        _pan = default;
        e.Handled = true;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 1 || height <= 1)
        {
            return;
        }

        var markers = Markers?.ToList() ?? [];
        var playerPosition = PlayerPosition;
        if (markers.Count == 0 && !playerPosition.HasValue)
        {
            // 没有勾选玩法时仍显示区域底图，只提示用户勾选分类。
            drawingContext.PushOpacity(Math.Clamp(BaseOpacity, 0d, 1d));
            drawingContext.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, width, height));
            if (Region is { } emptyRegion)
            {
                var extent = GetTileExtent(emptyRegion);
                if (extent <= 0)
                {
                    DrawEmpty(drawingContext, width, height);
                    return;
                }
                var emptyScale = Math.Clamp(
                    Math.Min((width - 96) / extent, (height - 96) / extent) * _zoom,
                    0.0001,
                    1000);
                var emptyCenter = FocusPoint ?? new MapPoint(extent / 2, extent / 2);
                Point EmptyScreen(MapPoint point) => new(
                    ((point.X - emptyCenter.X) * emptyScale) + (width / 2) + _pan.X,
                    ((point.Y - emptyCenter.Y) * emptyScale) + (height / 2) + _pan.Y);
                DrawTilesWithLayer(drawingContext, EmptyScreen, emptyScale, 0, extent, 0, extent);
                DrawLayerOverlays(drawingContext, EmptyScreen, emptyScale);
            }
            drawingContext.Pop();

            DrawEmpty(drawingContext, width, height);
            return;
        }

        var points = markers.Select(GetMapPoint).ToList();
        if (playerPosition.HasValue)
        {
            points.Add(playerPosition.Value);
        }

        var minX = points.Min(point => point.X);
        var maxX = points.Max(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxY = points.Max(point => point.Y);
        var regionExtent = GetTileExtent(Region);
        if (regionExtent > 0)
        {
            // 官方地图以瓦片范围作为视口，而不是按筛选后的标记自动裁切。
            minX = 0;
            maxX = regionExtent;
            minY = 0;
            maxY = regionExtent;
        }

        var rangeX = Math.Max(maxX - minX, 1);
        var rangeY = Math.Max(maxY - minY, 1);
        var padding = 48d;
        var baseScale = Math.Min((width - padding * 2) / rangeX, (height - padding * 2) / rangeY);
        var scale = Math.Clamp(baseScale * _zoom, 0.0001, 1000);
        var followPlayer = FollowPlayer && playerPosition.HasValue;
        var centerX = followPlayer
            ? playerPosition!.Value.X
            : FocusPoint?.X ?? ((minX + maxX) / 2);
        var centerY = followPlayer
            ? playerPosition!.Value.Y
            : FocusPoint?.Y ?? ((minY + maxY) / 2);

        // 跟随玩家时忽略拖动偏移，角色标记必须严格落在视口中心。
        var panX = followPlayer ? 0 : _pan.X;
        var panY = followPlayer ? 0 : _pan.Y;
        Point ToScreen(MapPoint point) => new(
            ((point.X - centerX) * scale) + (width / 2) + panX,
            ((point.Y - centerY) * scale) + (height / 2) + panY);

        // 只降低底图和瓦片透明度，路线与标记保持清晰。
        drawingContext.PushOpacity(Math.Clamp(BaseOpacity, 0d, 1d));
        drawingContext.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, width, height));
        DrawTilesWithLayer(drawingContext, ToScreen, scale, minX, maxX, minY, maxY);
        drawingContext.Pop();
        DrawLayerOverlays(drawingContext, ToScreen, scale);
        DrawRoute(drawingContext, ToScreen);

        _screenMarkers.Clear();
        var showAllLabels = markers.Count <= 500;
        var selectedIds = SelectedIds;
        var clusterSize = 38d;
        var clusters = new Dictionary<(int X, int Y), List<(MapMarker Marker, Point Point)>>();
        foreach (var marker in markers)
        {
            var screen = ToScreen(GetMapPoint(marker));
            _screenMarkers.Add((marker, screen));
            if (selectedIds?.Contains(marker.Id) == true)
            {
                DrawMarker(drawingContext, marker, screen, true);
                continue;
            }

            if (_zoom < 8)
            {
                var key = ((int)Math.Floor(screen.X / clusterSize),
                    (int)Math.Floor(screen.Y / clusterSize));
                if (!clusters.TryGetValue(key, out var bucket))
                {
                    bucket = [];
                    clusters[key] = bucket;
                }

                bucket.Add((marker, screen));
                continue;
            }

            DrawMarker(drawingContext, marker, screen,
                showAllLabels || selectedIds?.Contains(marker.Id) == true);
        }

        foreach (var bucket in clusters.Values)
        {
            if (bucket.Count == 1)
            {
                DrawMarker(drawingContext, bucket[0].Marker, bucket[0].Point, false);
                continue;
            }

            var clusterCenterX = bucket.Average(item => item.Point.X);
            var clusterCenterY = bucket.Average(item => item.Point.Y);
            DrawCluster(drawingContext, new Point(clusterCenterX, clusterCenterY), bucket.Count);
        }

        if (playerPosition.HasValue)
        {
            DrawPlayer(drawingContext, ToScreen(playerPosition.Value));
        }

    }

    /// <summary>
    /// 非地面位面先降低底图亮度，模拟官方位面遮罩效果。
    /// </summary>
    private void DrawTilesWithLayer(
        DrawingContext drawingContext,
        Func<MapPoint, Point> toScreen,
        double scale,
        double minX,
        double maxX,
        double minY,
        double maxY)
    {
        var hasLayerOverlay = GetSelectedLayer()?.Overlays.Count > 0;
        if (!hasLayerOverlay)
        {
            DrawTiles(drawingContext, toScreen, scale, minX, maxX, minY, maxY);
            return;
        }

        // 官方在位面模式下把主底图压暗到 20%，再叠加位面图片。
        drawingContext.PushOpacity(0.2);
        DrawTiles(drawingContext, toScreen, scale, minX, maxX, minY, maxY);
        drawingContext.Pop();
    }

    /// <summary>
    /// 绘制官方多位面图片覆盖层。
    /// </summary>
    private void DrawLayerOverlays(
        DrawingContext drawingContext,
        Func<MapPoint, Point> toScreen,
        double scale)
    {
        var layer = GetSelectedLayer();
        if (layer is null || layer.Overlays.Count == 0)
        {
            return;
        }

        foreach (var overlay in layer.Overlays)
        {
            if (string.IsNullOrWhiteSpace(overlay.ImageUrl))
            {
                continue;
            }

            EnsureLayerImage(overlay.ImageUrl);
            if (!_layerImages.TryGetValue(overlay.ImageUrl, out var image))
            {
                continue;
            }

            var a = GodlikeProjection.WorldToMap(
                overlay.MapSubType,
                overlay.WorldA.X,
                overlay.WorldA.Y);
            var b = GodlikeProjection.WorldToMap(
                overlay.MapSubType,
                overlay.WorldB.X,
                overlay.WorldB.Y);
            var screenA = toScreen(a);
            var screenB = toScreen(b);
            var left = Math.Min(screenA.X, screenB.X);
            var top = Math.Min(screenA.Y, screenB.Y);
            var width = Math.Abs(screenB.X - screenA.X);
            var height = Math.Abs(screenB.Y - screenA.Y);
            if (width <= 0 || height <= 0)
            {
                continue;
            }

            drawingContext.DrawImage(image, new Rect(left, top, width, height));
        }
    }

    private MapLayer? GetSelectedLayer()
    {
        if (Region is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(LayerId))
        {
            var byId = Region.Layers.FirstOrDefault(layer => layer.Id == LayerId);
            if (byId is not null)
            {
                return byId;
            }
        }

        return Region.Layers.FirstOrDefault(layer => layer.Floor == LayerLevel)
               ?? Region.Layers.FirstOrDefault();
    }

    private void EnsureLayerImage(string url)
    {
        if (_layerImages.ContainsKey(url) || !_layerImageLoading.Add(url))
        {
            return;
        }

        _ = LoadLayerImageAsync(url);
    }

    private async Task LoadLayerImageAsync(string url)
    {
        try
        {
            var bytes = await TileHttpClient.GetByteArrayAsync(url);
            await Dispatcher.InvokeAsync(() =>
            {
                using var stream = new MemoryStream(bytes);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                _layerImages[url] = image;
                InvalidateVisual();
            });
        }
        catch
        {
            // 位面图片加载失败时保留底图。
        }
        finally
        {
            _layerImageLoading.Remove(url);
        }
    }

    /// <summary>
    /// 从区域 subtype 读取官方瓦片范围，避免旧快照缓存错误范围。
    /// </summary>
    private static double GetTileExtent(MapRegion? region)
    {
        if (region is null)
        {
            return 0;
        }

        var subType = region.SubTypes.FirstOrDefault();
        return string.IsNullOrWhiteSpace(subType)
            ? region.TileExtent
            : GodlikeProjection.Get(subType).TileExtent;
    }

    /// <summary>
    /// 从区域 subtype 读取官方瓦片模板，避免旧快照缓存错误模板。
    /// </summary>
    private static string? GetTileUrlTemplate(MapRegion region)
    {
        var subType = region.SubTypes.FirstOrDefault();
        return string.IsNullOrWhiteSpace(subType)
            ? region.TileUrlTemplate
            : GodlikeProjection.Get(subType).TileUrlTemplate;
    }

    private void DrawTiles(
        DrawingContext drawingContext,
        Func<MapPoint, Point> toScreen,
        double scale,
        double minX,
        double maxX,
        double minY,
        double maxY)
    {
        var region = Region;
        if (region is null)
        {
            return;
        }

        var template = GetTileUrlTemplate(region);
        if (string.IsNullOrWhiteSpace(template))
        {
            return;
        }

        var extent = Math.Max(GetTileExtent(region), 1);
        var zoom = Math.Clamp(
            (int)Math.Round(Math.Log2(Math.Max(scale, 1))),
            Math.Max(region.TileMinZoom, 3),
            Math.Min(region.TileMaxZoom, 8));
        // 官方瓦片使用 Leaflet CRS.Simple：每级缩放的地图坐标步长是 256 / 2^zoom。
        // 不能用 extent / 2^zoom；滹沱/河西的 TileExtent 不是 256，否则点位会偏移。
        var tileMapSize = 256d / (1 << zoom);
        var tileCount = Math.Max(1, (int)Math.Ceiling(extent / tileMapSize));
        var width = ActualWidth;
        var height = ActualHeight;
        // 视口中心必须和 ToScreen 使用的中心一致，否则聚焦区域后高倍瓦片会画在屏幕外。
        var followPlayer = FollowPlayer && PlayerPosition.HasValue;
        var viewCenterX = followPlayer
            ? PlayerPosition!.Value.X
            : FocusPoint?.X ?? ((minX + maxX) / 2);
        var viewCenterY = followPlayer
            ? PlayerPosition!.Value.Y
            : FocusPoint?.Y ?? ((minY + maxY) / 2);
        var panX = followPlayer ? 0 : _pan.X;
        var panY = followPlayer ? 0 : _pan.Y;
        var viewLeft = Math.Max(0, ((0 - width / 2 - panX) / scale) + viewCenterX);
        var viewRight = Math.Min(extent, ((width - width / 2 - panX) / scale) + viewCenterX);
        var viewTop = Math.Max(0, ((0 - height / 2 - panY) / scale) + viewCenterY);
        var viewBottom = Math.Min(extent, ((height - height / 2 - panY) / scale) + viewCenterY);
        // 多请求一圈边缘瓦片，避免快速拖动或切换地图时出现空白贴图。
        var startX = Math.Clamp((int)Math.Floor(viewLeft / tileMapSize) - 1, 0, tileCount - 1);
        var endX = Math.Clamp((int)Math.Floor(viewRight / tileMapSize) + 1, 0, tileCount - 1);
        var startY = Math.Clamp((int)Math.Floor(viewTop / tileMapSize) - 1, 0, tileCount - 1);
        var endY = Math.Clamp((int)Math.Floor(viewBottom / tileMapSize) + 1, 0, tileCount - 1);

        for (var x = startX; x <= endX; x++)
        {
            for (var y = startY; y <= endY; y++)
            {
                // 缓存键包含区域，避免多个地图复用同一模板时串图。
                var key = $"{region.Id}|{template}|{zoom}|{x}|{y}";
                EnsureTile(key, template, zoom, x, y);
                var topLeft = toScreen(new MapPoint(x * tileMapSize, y * tileMapSize));
                var size = tileMapSize * scale;
                if (_tileImages.TryGetValue(key, out var image))
                {
                    drawingContext.DrawImage(image, new Rect(topLeft.X, topLeft.Y, size, size));
                    continue;
                }

                // 高倍瓦片尚未返回时先用低倍父瓦片放大对应区域，避免切图后上半张地图出现空白。
                if (TryGetFallbackTile(region, template, zoom, x, y, out var fallback, out var sourceRect))
                {
                    var brush = new ImageBrush(fallback)
                    {
                        Viewbox = sourceRect,
                        ViewboxUnits = BrushMappingMode.Absolute,
                        Stretch = Stretch.Fill
                    };
                    drawingContext.DrawRectangle(brush, null, new Rect(topLeft.X, topLeft.Y, size, size));
                }
            }
        }
    }

    /// <summary>
    /// 查找当前瓦片的低倍父瓦片及对应的裁剪区域，用于高倍瓦片加载期间的兜底绘制。
    /// </summary>
    private bool TryGetFallbackTile(
        MapRegion region,
        string template,
        int zoom,
        int x,
        int y,
        out BitmapSource image,
        out Rect sourceRect)
    {
        image = null!;
        sourceRect = Rect.Empty;
        for (var parentZoom = zoom - 1; parentZoom >= Math.Max(region.TileMinZoom, 0); parentZoom--)
        {
            var factor = 1 << (zoom - parentZoom);
            var parentX = x / factor;
            var parentY = y / factor;
            var parentKey = $"{region.Id}|{template}|{parentZoom}|{parentX}|{parentY}";
            if (!_tileImages.TryGetValue(parentKey, out var parent))
            {
                continue;
            }

            var pixelSize = parent.PixelWidth / (double)factor;
            image = parent;
            sourceRect = new Rect(
                (x % factor) * pixelSize,
                (y % factor) * pixelSize,
                pixelSize,
                pixelSize);
            return true;
        }

        return false;
    }

    private void EnsureTile(string key, string template, int zoom, int x, int y)
    {
        if (_tileImages.ContainsKey(key) ||
            _tileRetryCount.TryGetValue(key, out var retries) && retries >= 3 ||
            !_tileLoading.Add(key))
        {
            return;
        }

        var url = template
            .Replace("{z}", zoom.ToString(CultureInfo.InvariantCulture))
            .Replace("{x}", x.ToString(CultureInfo.InvariantCulture))
            .Replace("{y}", y.ToString(CultureInfo.InvariantCulture));
        _ = LoadTileAsync(key, url);
    }

    private async Task LoadTileAsync(string key, string url)
    {
        try
        {
            var bytes = await TileHttpClient.GetByteArrayAsync(url);
            await Dispatcher.InvokeAsync(() =>
            {
                using var stream = new MemoryStream(bytes);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                _tileImages[key] = image;
                _tileRetryCount.Remove(key);
                InvalidateVisual();
            });
        }
        catch
        {
            // 网络抖动时瓦片会短暂缺失；主动重绘几次，避免半张地图一直停在空白状态。
            var retries = _tileRetryCount.GetValueOrDefault(key) + 1;
            _tileRetryCount[key] = retries;
            if (retries < 3)
            {
                await Dispatcher.InvokeAsync(InvalidateVisual, System.Windows.Threading.DispatcherPriority.Background);
            }
        }
        finally
        {
            _tileLoading.Remove(key);
        }
    }

    private void EnsureMarkerIcon(string url)
    {
        if (_markerIcons.ContainsKey(url) || !_markerIconLoading.Add(url))
        {
            return;
        }

        _ = LoadMarkerIconAsync(url);
    }

    private async Task LoadMarkerIconAsync(string url)
    {
        try
        {
            var bytes = await MarkerIconHttpClient.GetByteArrayAsync(url);
            await Dispatcher.InvokeAsync(() =>
            {
                using var stream = new MemoryStream(bytes);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                _markerIcons[url] = image;
                InvalidateVisual();
            });
        }
        catch
        {
            // 图标失败时回退到分类色点。
        }
        finally
        {
            _markerIconLoading.Remove(url);
        }
    }

    private void DrawRoute(DrawingContext drawingContext, Func<MapPoint, Point> toScreen)
    {
        var route = Route;
        if (route is null || route.Legs.Count == 0)
        {
            return;
        }

        foreach (var leg in route.Legs)
        {
            // 路线绘制策略由宏观路线 module 决定，Canvas 只执行结果。
            var fromMap = ToMap(leg.From);
            var toMap = ToMap(leg.To);
            if (!MacroRoutePolicy.ShouldDraw(leg, ToMap))
            {
                continue;
            }

            var from = toScreen(fromMap);
            var to = toScreen(toMap);
            drawingContext.DrawLine(GetTripPen(leg.TripIndex), from, to);
        }
    }

    /// <summary>
    /// 每个出发批次生成独立色相，避免批次超过调色板长度后颜色重复。
    /// </summary>
    private static Pen GetTripPen(int tripIndex)
    {
        if (TripPens.TryGetValue(tripIndex, out var pen))
        {
            return pen;
        }

        var hue = (tripIndex * 137.508) % 360d;
        var sector = hue / 60d;
        var sectorIndex = (int)Math.Floor(sector) % 6;
        var fraction = sector - Math.Floor(sector);
        const double value = 0.95;
        const double saturation = 0.65;
        var p = value * (1 - saturation);
        var q = value * (1 - (saturation * fraction));
        var t = value * (1 - (saturation * (1 - fraction)));
        var (red, green, blue) = sectorIndex switch
        {
            0 => (value, t, p),
            1 => (q, value, p),
            2 => (p, value, t),
            3 => (p, q, value),
            4 => (t, p, value),
            _ => (value, p, q)
        };
        pen = CreatePen(Color.FromRgb(
            (byte)(red * 255),
            (byte)(green * 255),
            (byte)(blue * 255)), 2);
        TripPens[tripIndex] = pen;
        return pen;
    }

    private void DrawMarker(
        DrawingContext drawingContext,
        MapMarker marker,
        Point screen,
        bool showLabel)
    {
        // 只加载视口内标记的图标，避免上万条标记同时抢瓦片下载连接。
        if (screen.X < -48 || screen.X > ActualWidth + 48 ||
            screen.Y < -48 || screen.Y > ActualHeight + 48)
        {
            return;
        }

        var categoryId = marker.CategoryIds.FirstOrDefault() ?? marker.CategoryId;
        var color = MarkerColors[(int)((uint)categoryId.GetHashCode() % (uint)MarkerColors.Length)];
        var brush = new SolidColorBrush(color);
        var radius = marker.IsTeleport ? 7d : 5d;
        if (SelectedIds?.Contains(marker.Id) == true)
        {
            drawingContext.DrawEllipse(null, SelectedPen, screen, radius + 4, radius + 4);
        }

        var completion = CompletionOf?.Invoke(marker) ?? default;
        if (!string.IsNullOrWhiteSpace(marker.Icon))
        {
            EnsureMarkerIcon(marker.Icon);
            if (_markerIcons.TryGetValue(marker.Icon, out var icon))
            {
                var size = marker.IsTeleport ? 30d : 24d;
                if (SelectedIds?.Contains(marker.Id) == true)
                {
                    size += 4;
                }

                drawingContext.DrawImage(icon,
                    new Rect(screen.X - size / 2, screen.Y - size / 2, size, size));
                if (completion.IsCompleted)
                {
                    drawingContext.DrawEllipse(null,
                        new Pen(new SolidColorBrush(Color.FromArgb(200, 90, 220, 130)), 2),
                        screen, size / 2 + 2, size / 2 + 2);
                }

                if (showLabel)
                {
                    var iconLabel = FormatText(marker.Title, 11, LabelBrush);
                    drawingContext.DrawText(iconLabel, new Point(screen.X + size / 2 + 4,
                        screen.Y - iconLabel.Height / 2));
                }

                return;
            }
        }

        drawingContext.DrawEllipse(brush, null, screen, radius, radius);
        if (completion.IsCompleted)
        {
            drawingContext.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(180, 90, 220, 130)), 1),
                screen, radius + 1.5, radius + 1.5);
        }

        if (!showLabel)
        {
            return;
        }

        var label = FormatText(marker.Title, 11, LabelBrush);
        drawingContext.DrawText(label, new Point(screen.X + radius + 4, screen.Y - label.Height / 2));
    }

    private void DrawCluster(DrawingContext drawingContext, Point screen, int count)
    {
        var radius = count >= 1000 ? 16d : count >= 100 ? 13d : 10d;
        var fill = new SolidColorBrush(Color.FromArgb(220, 40, 35, 28));
        var outline = new Pen(new SolidColorBrush(Color.FromArgb(240, 231, 195, 106)), 2);
        drawingContext.DrawEllipse(fill, outline, screen, radius, radius);
        var text = FormatText(count.ToString(CultureInfo.InvariantCulture), 10, LabelBrush);
        drawingContext.DrawText(text, new Point(screen.X - text.Width / 2, screen.Y - text.Height / 2));
    }

    private void DrawPlayer(DrawingContext drawingContext, Point screen)
    {
        if (PlayerMarkerIcon is not null)
        {
            // 官方实时地图的自身位置箭头，箭头中心与玩家坐标对齐，并朝向移动方向。
            const double size = 30d;
            drawingContext.PushTransform(new RotateTransform(_playerHeading, screen.X, screen.Y));
            drawingContext.DrawImage(
                PlayerMarkerIcon,
                new Rect(screen.X - size / 2, screen.Y - size / 2, size, size));
            drawingContext.Pop();
            return;
        }

        // 素材加载失败时保留可见的圆形回退标记。
        var fill = new SolidColorBrush(Color.FromRgb(235, 190, 80));
        var outline = new Pen(new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)), 2);
        drawingContext.DrawEllipse(new SolidColorBrush(Color.FromArgb(70, 235, 190, 80)), null, screen, 13, 13);
        drawingContext.DrawEllipse(fill, outline, screen, 6, 6);
    }

    /// <summary>
    /// 加载官方实时地图使用的玩家箭头素材。
    /// </summary>
    private static BitmapSource? LoadPlayerMarkerIcon()
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(
                "pack://application:,,,/BetterYYSLS;component/Assets/Map/self-point.png",
                UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private void DrawEmpty(DrawingContext drawingContext, double width, double height)
    {
        var hasRegion = Region is not null;
        var title = FormatText(hasRegion ? "暂无勾选玩法" : "暂无地图快照", 18, LabelBrush);
        var hint = FormatText(
            hasRegion ? "请在上方勾选要显示的玩法分类" : "点击左侧“导入”加载 snapshot.json",
            12,
            MutedBrush);
        drawingContext.DrawText(title, new Point((width - title.Width) / 2, (height / 2) - 24));
        drawingContext.DrawText(hint, new Point((width - hint.Width) / 2, (height / 2) + 8));
    }

    private MapPoint GetMapPoint(MapMarker marker)
    {
        // 滹沱/皇宫使用独立投影；旧快照里的 MapPosition 可能是旧配置算出的，
        // 始终按当前区域的 subtype 从世界坐标重算。
        if (Region?.SubTypes.FirstOrDefault() is { } regionSubType &&
            (regionSubType is "3" or "4" || marker.MapSubType != regionSubType))
        {
            return ToMap(marker.WorldPosition);
        }

        if (marker.MapPosition.X != 0 || marker.MapPosition.Y != 0)
        {
            return marker.MapPosition;
        }

        return ToMap(marker.WorldPosition);
    }

    private MapPoint ToMap(MapPoint world)
    {
        if (Region?.SubTypes.FirstOrDefault() is { } subType)
        {
            return GodlikeProjection.WorldToMap(world, subType);
        }

        return new MapPoint(world.X, world.Y);
    }

    private FormattedText FormatText(string text, double size, Brush brush)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            size,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    private static Pen CreatePen(Color color, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness);
        pen.Freeze();
        return pen;
    }

    private static void OnMarkersChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var canvas = (MapCanvas)dependencyObject;
        canvas.HookCollection(ref canvas._markersHandler, args.OldValue, args.NewValue);
        canvas.Zoom = 16;
        canvas._pan = default;
        canvas.InvalidateVisual();
    }

    private static void OnSelectedIdsChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var canvas = (MapCanvas)dependencyObject;
        canvas.HookCollection(ref canvas._selectedHandler, args.OldValue, args.NewValue);
        canvas.InvalidateVisual();
    }

    private static void OnZoomChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var canvas = (MapCanvas)dependencyObject;
        canvas._zoom = Math.Clamp((double)args.NewValue, 0.2, 128);
        canvas.InvalidateVisual();
    }

    private static void OnVisualPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var canvas = (MapCanvas)dependencyObject;
        if (args.Property == RegionProperty || args.Property == FocusPointProperty)
        {
            canvas._pan = default;
            canvas.Zoom = 16;
        }

        if (args.Property == PlayerPositionProperty)
        {
            canvas.UpdatePlayerHeading(args.NewValue as MapPoint?);
        }

        if ((args.Property == FollowPlayerProperty || args.Property == PlayerPositionProperty) &&
            canvas.FollowPlayer)
        {
            // 跟随玩家时忽略拖动偏移，保证玩家标记严格位于视口中心。
            canvas._pan = default;
        }

        canvas.InvalidateVisual();
    }

    /// <summary>
    /// 根据相邻两次玩家位置计算箭头朝向；静止时保留上一次方向。
    /// </summary>
    private void UpdatePlayerHeading(MapPoint? position)
    {
        if (!position.HasValue)
        {
            _lastPlayerPosition = null;
            return;
        }

        if (_lastPlayerPosition is { } previous)
        {
            var deltaX = position.Value.X - previous.X;
            var deltaY = position.Value.Y - previous.Y;
            if ((deltaX * deltaX) + (deltaY * deltaY) > 1e-8)
            {
                // 地图 Y 轴向下，素材默认朝上，因此以 atan2(dx, -dy) 得到顺时针角度。
                // 采样提高到 100ms 后直接使用最新位移，避免箭头滞后。
                _playerHeading = Math.Atan2(deltaX, -deltaY) * 180d / Math.PI;
            }
        }

        _lastPlayerPosition = position.Value;
    }

    private void HookCollection(
        ref NotifyCollectionChangedEventHandler? handler,
        object? oldValue,
        object? newValue)
    {
        if (oldValue is INotifyCollectionChanged oldCollection && handler is not null)
        {
            oldCollection.CollectionChanged -= handler;
        }

        handler = null;
        if (newValue is INotifyCollectionChanged newCollection)
        {
            handler = (_, _) => InvalidateVisual();
            newCollection.CollectionChanged += handler;
        }
    }
}

using BetterYYSLS.Core.Map;
using BetterYYSLS.GameTask;
using BetterYYSLS.View.Controls;
using BetterYYSLS.ViewModel;
using Fischless.HotkeyCapture;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Keys = System.Windows.Forms.Keys;

namespace BetterYYSLS.View;

/// <summary>
/// 覆盖在游戏上的大地图窗口。
/// 默认浮动，可切换全屏和点击穿透。
/// </summary>
public partial class MapWindow : Window
{
    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExLayered = 0x80000;
    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotTopmost = new(-2);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint MonitorDefaultToNearest = 0x00000002;

    private readonly MapWindowViewModel _viewModel;
    private readonly DispatcherTimer _followTimer;
    private readonly DispatcherTimer _clickThroughTimer;
    private readonly DispatcherTimer _topmostTimer;
    private readonly HotkeyHook _hotkeyHook = new();
    private bool _loaded;
    private bool _sidebarCollapsed;
    private bool _hotkeyRegistered;
    private bool _windowIsTransparent;
    private bool _topmostApplied;
    private bool _fullScreenApplied;
    private Rect _fullScreenRestoreBounds;
    private nint _gameWindowHandle;

    /// <summary>
    /// 创建大地图窗口。
    /// </summary>
    public MapWindow(MapWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        _followTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _followTimer.Tick += (_, _) => FollowGameWindow();
        // 穿透时窗口无法收到鼠标事件，通过轮询光标位置在顶栏临时恢复交互。
        _clickThroughTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _clickThroughTimer.Tick += (_, _) => UpdateClickThroughHitTest();
        // 游戏可能也是置顶窗口，定期重新声明 TOPMOST，避免被游戏重新压到下面。
        _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _topmostTimer.Tick += (_, _) => EnsureTopmost();
        _hotkeyHook.KeyPressed += (_, _) => _viewModel.IsClickThrough = !_viewModel.IsClickThrough;
        MapView.MarkerSelected += OnMarkerSelected;
        MapView.FollowPlayerCancelled += (_, _) => _viewModel.IsPlayerCentered = false;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnLoaded;
        KeyDown += OnKeyDown;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            _viewModel.StartLiveTracking();
            ApplyClickThrough(_viewModel.IsClickThrough);
            ApplyFullScreen(_viewModel.IsFullScreen);
            ApplyAlwaysOnTop(_viewModel.IsAlwaysOnTop);
            _topmostTimer.Start();
            RegisterClickThroughHotkey();
            if (_viewModel.IsFollowGame)
            {
                _followTimer.Start();
                FollowGameWindow();
            }

            await _viewModel.LoadDefaultAsync();
        }
        catch (Exception ex)
        {
            _viewModel.Status = $"地图初始化失败：{ex.Message}";
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _followTimer.Stop();
        _clickThroughTimer.Stop();
        _topmostTimer.Stop();
        SetWindowTransparent(false);
        _viewModel.StopLiveTracking();
        if (_hotkeyRegistered)
        {
            _hotkeyHook.Dispose();
            _hotkeyRegistered = false;
        }
    }

    private void RegisterClickThroughHotkey()
    {
        try
        {
            _hotkeyHook.RegisterHotKey(
                Vanara.PInvoke.User32.HotKeyModifiers.MOD_CONTROL |
                Vanara.PInvoke.User32.HotKeyModifiers.MOD_ALT,
                Keys.M);
            _hotkeyRegistered = true;
        }
        catch (InvalidOperationException)
        {
            // 热键被其他程序占用时保留界面按钮作为退路。
        }
    }

    private void FollowGameWindow()
    {
        if (!_viewModel.IsFollowGame || _viewModel.IsFullScreen)
        {
            return;
        }

        var handle = _gameWindowHandle;
        if (handle == 0 || !IsWindowVisible(handle))
        {
            handle = SystemControl.FindHandleByProcessName("yysls");
            _gameWindowHandle = handle;
        }

        if (handle == 0 || !IsWindowVisible(handle))
        {
            return;
        }

        try
        {
            var rect = SystemControl.GetWindowRect(handle);
            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0)
            {
                _gameWindowHandle = 0;
                return;
            }

            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            Left = rect.Left / dpi.DpiScaleX;
            Top = rect.Top / dpi.DpiScaleY;
            Width = width / dpi.DpiScaleX;
            Height = height / dpi.DpiScaleY;
        }
        catch
        {
            _gameWindowHandle = 0;
            // 游戏窗口切换或关闭时忽略本轮定位。
        }
    }

    private void OnMarkerSelected(object? sender, MapMarkerEventArgs e)
    {
        _viewModel.SelectMarker(e.Marker);
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsFromButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed && !_viewModel.IsFullScreen)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // 拖动过程中窗口状态可能改变，忽略即可。
            }
        }
    }

    private static bool IsFromButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase or
                System.Windows.Controls.ComboBox or
                System.Windows.Controls.TextBox)
            {
                return true;
            }

            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        // 穿透状态由窗口样式控制，隐藏前先恢复交互，避免下次打开仍无法点击。
        _viewModel.IsClickThrough = false;
        Hide();
    }

    /// <summary>
    /// 收起或展开左侧玩法侧栏。
    /// </summary>
    private void OnToggleSidebarClick(object sender, RoutedEventArgs e)
    {
        _sidebarCollapsed = !_sidebarCollapsed;
        SidebarColumn.Width = _sidebarCollapsed ? new GridLength(0) : new GridLength(460);
        SidebarToggleButton.ToolTip = _sidebarCollapsed ? "展开侧栏" : "收起侧栏";
        // 收起后侧栏列宽为 0，按钮不能再用负边距，否则会被窗口左边缘裁掉。
        SidebarToggleButton.Margin = _sidebarCollapsed
            ? new Thickness(10, 10, 0, 0)
            : new Thickness(-20, 10, 0, 0);
        // 收起后把左箭头翻转为右箭头，明确表示可以重新展开。
        SidebarToggleIcon.RenderTransform = new System.Windows.Media.ScaleTransform(
            _sidebarCollapsed ? -1 : 1,
            1);
    }

    /// <summary>
    /// 打开或关闭区域下拉面板。
    /// </summary>
    private void OnRegionSwitchClick(object sender, RoutedEventArgs e)
    {
        RegionPopup.IsOpen = !RegionPopup.IsOpen;
    }

    /// <summary>
    /// 选择区域后切换当前地图并关闭下拉面板。
    /// </summary>
    private void OnRegionOptionClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MapRegionOption option })
        {
            _viewModel.SelectRegionOption(option);
            // 同一主世界的清河/开封共用 region，切图时清掉旧瓦片，避免上半张图复用空白缓存。
            MapView.ClearTileCache();
            RegionPopup.IsOpen = false;
        }
    }

    /// <summary>
    /// 展开或收起区域下的二级子区域。
    /// </summary>
    private void OnRegionGroupExpandClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MapRegionGroup group })
        {
            group.IsExpanded = !group.IsExpanded;
        }
    }

    /// <summary>
    /// 打开或关闭位面图层面板。
    /// </summary>
    private void OnLayerSwitchClick(object sender, RoutedEventArgs e)
    {
        LayerPopup.IsOpen = !LayerPopup.IsOpen;
    }

    /// <summary>
    /// 选择位面后关闭面板。
    /// </summary>
    private void OnLayerSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0)
        {
            LayerPopup.IsOpen = false;
        }
    }

    /// <summary>
    /// 放大地图。
    /// </summary>
    private void OnZoomInClick(object sender, RoutedEventArgs e)
    {
        MapView.ZoomIn();
    }

    /// <summary>
    /// 缩小地图。
    /// </summary>
    private void OnZoomOutClick(object sender, RoutedEventArgs e)
    {
        MapView.ZoomOut();
    }

    /// <summary>
    /// 最小化大地图窗口。
    /// </summary>
    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        // 透明无边框窗口不进入任务栏，最小化时直接隐藏，避免 WindowState 异常。
        _viewModel.IsClickThrough = false;
        Hide();
    }

    /// <summary>
    /// 选中一级分类时把对应分组滚动到可视区域。
    /// </summary>
    private void OnCategoryGroupSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 &&
            CategoryItems.ItemContainerGenerator.ContainerFromItem(e.AddedItems[0]) is FrameworkElement container)
        {
            container.BringIntoView();
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MapWindowViewModel.IsFullScreen))
        {
            ApplyFullScreen(_viewModel.IsFullScreen);
        }
        else if (e.PropertyName == nameof(MapWindowViewModel.IsClickThrough))
        {
            ApplyClickThrough(_viewModel.IsClickThrough);
        }
        else if (e.PropertyName == nameof(MapWindowViewModel.IsAlwaysOnTop))
        {
            ApplyAlwaysOnTop(_viewModel.IsAlwaysOnTop);
        }
        else if (e.PropertyName == nameof(MapWindowViewModel.IsFollowGame))
        {
            if (_viewModel.IsFollowGame)
            {
                _followTimer.Start();
                FollowGameWindow();
            }
            else
            {
                _followTimer.Stop();
            }
        }
    }

    /// <summary>
    /// 进入或退出真正的无边框全屏：直接铺满显示器，不使用 WindowState.Maximized。
    /// </summary>
    private void ApplyFullScreen(bool enabled)
    {
        if (enabled)
        {
            if (_fullScreenApplied)
            {
                return;
            }

            _fullScreenRestoreBounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, Width, Height)
                : RestoreBounds;
            _fullScreenApplied = true;
            WindowState = WindowState.Normal;
            ResizeMode = ResizeMode.NoResize;
            // 全屏时去掉外边距、圆角和边框，避免显示器边缘出现缝隙。
            RootLayout.Margin = new Thickness(0);
            WindowFrame.CornerRadius = new CornerRadius(0);
            WindowFrame.BorderThickness = new Thickness(0);
            MapFrame.CornerRadius = new CornerRadius(0);

            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
                {
                    var dpi = GetDpiForWindow(handle);
                    var scale = dpi > 0 ? dpi / 96d : 1d;
                    Left = info.Monitor.Left / scale;
                    Top = info.Monitor.Top / scale;
                    Width = (info.Monitor.Right - info.Monitor.Left) / scale;
                    Height = (info.Monitor.Bottom - info.Monitor.Top) / scale;
                }
            }

            EnsureTopmost();
            return;
        }

        if (!_fullScreenApplied)
        {
            return;
        }

        _fullScreenApplied = false;
        ResizeMode = ResizeMode.CanResize;
        WindowState = WindowState.Normal;
        RootLayout.Margin = new Thickness(10);
        WindowFrame.CornerRadius = new CornerRadius(10);
        WindowFrame.BorderThickness = new Thickness(1);
        MapFrame.CornerRadius = new CornerRadius(0, 0, 10, 0);
        if (_fullScreenRestoreBounds.Width > 0 && _fullScreenRestoreBounds.Height > 0)
        {
            Left = _fullScreenRestoreBounds.Left;
            Top = _fullScreenRestoreBounds.Top;
            Width = _fullScreenRestoreBounds.Width;
            Height = _fullScreenRestoreBounds.Height;
        }
    }

    /// <summary>
    /// 应用“始终置顶”开关。
    /// </summary>
    private void ApplyAlwaysOnTop(bool enabled)
    {
        Topmost = enabled;
        EnsureTopmost();
    }

    /// <summary>
    /// 重新声明窗口置顶，保证游戏窗口激活后仍显示在地图之上。
    /// </summary>
    private void EnsureTopmost()
    {
        if (!IsVisible)
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        if (!_viewModel.IsAlwaysOnTop)
        {
            if (_topmostApplied)
            {
                // 关闭置顶时只恢复一次普通层级，避免每个计时周期重复调用。
                SetWindowPos(
                    handle,
                    HwndNotTopmost,
                    0,
                    0,
                    0,
                    0,
                    SwpNoMove | SwpNoSize | SwpNoActivate);
                _topmostApplied = false;
            }

            return;
        }

        SetWindowPos(
            handle,
            HwndTopmost,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoActivate);
        _topmostApplied = true;
    }

    /// <summary>
    /// 切换窗口的鼠标穿透样式。
    /// </summary>
    private void ApplyClickThrough(bool enabled)
    {
        _clickThroughTimer.Stop();
        if (!enabled)
        {
            SetWindowTransparent(false);
            return;
        }

        // 先应用穿透，再由轮询在光标进入顶栏时临时恢复交互。
        SetWindowTransparent(true);
        UpdateClickThroughHitTest();
        _clickThroughTimer.Start();
    }

    /// <summary>
    /// 光标进入顶栏时临时关闭穿透，保证关闭、同步和穿透按钮仍可点击。
    /// </summary>
    private void UpdateClickThroughHitTest()
    {
        if (!_viewModel.IsClickThrough)
        {
            return;
        }

        var cursor = System.Windows.Forms.Cursor.Position;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        var localX = (cursor.X / dpi.DpiScaleX) - Left;
        var localY = (cursor.Y / dpi.DpiScaleY) - Top;
        // 顶栏高度 60，窗口外边距 10；进入顶栏后恢复交互。
        var overHeader = localX >= 0 &&
                         localX <= ActualWidth &&
                         localY >= 0 &&
                         localY <= 80;
        SetWindowTransparent(!overHeader);
    }

    /// <summary>
    /// 设置窗口是否完全接收鼠标穿透。
    /// </summary>
    private void SetWindowTransparent(bool transparent)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        // 不能只相信缓存状态：确保每次关闭穿透都真正移除 WS_EX_TRANSPARENT。
        var style = GetWindowLong(handle, GwlExstyle);
        var currentlyTransparent = (style & WsExTransparent) != 0;
        style |= WsExLayered;
        if (transparent)
        {
            style |= WsExTransparent;
        }
        else
        {
            style &= ~WsExTransparent;
        }

        if (currentlyTransparent != transparent)
        {
            SetWindowLong(handle, GwlExstyle, style);
        }

        _windowIsTransparent = transparent;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);
}

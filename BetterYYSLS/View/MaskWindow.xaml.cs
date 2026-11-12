using BetterYYSLS.Core.Config;
using BetterYYSLS.Core.Recognition.OpenCv;
using BetterYYSLS.GameTask;
using BetterYYSLS.Helpers;
using BetterYYSLS.Helpers.DpiAwareness;
using BetterYYSLS.View.Drawable;
using BetterYYSLS.ViewModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Vanara.PInvoke;
using FontFamily = System.Windows.Media.FontFamily;

namespace BetterYYSLS.View;

/// <summary>
/// 一个用于覆盖在游戏窗口上的窗口，用于显示识别结果、显示日志、设置区域位置等
/// 请使用 Instance 方法获取单例
/// </summary>
public partial class MaskWindow : Window
{
    private static MaskWindow? _maskWindow;

    private MaskWindowViewModel? _viewModel;
    private MaskWindowConfig? _maskConfig;

    private static readonly Typeface MyTypeface = new FontFamily("Microsoft Yahei UI").GetTypefaces().First();

    static MaskWindow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(MaskWindow), new FrameworkPropertyMetadata(typeof(MaskWindow)));
    }

    public static MaskWindow Instance()
    {
        if (_maskWindow == null)
        {
            throw new Exception("MaskWindow 未初始化");
        }

        return _maskWindow;
    }

    public bool IsClosed { get; private set; }

    protected override void OnClosed(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (_maskConfig is not null)
        {
            _maskConfig.PropertyChanged -= OnMaskConfigPropertyChanged;
        }

        base.OnClosed(e);
        IsClosed = true;
    }

    public void RefreshPosition(IntPtr hWnd)
    {
        //if (SystemControl.IsFullScreenMode(hWnd))
        //{
        //    Hide();
        //}

        var currentRect = SystemControl.GetCaptureRect(hWnd);
        double dpiScale = DpiHelper.ScaleY;
        RefreshPosition(currentRect, dpiScale);
    }

    public void RefreshPosition(RECT currentRect, double dpiScale)
    {
        Invoke(() =>
        {
            Left = currentRect.Left / dpiScale;
            Top = currentRect.Top / dpiScale;
            Width = currentRect.Width / dpiScale;
            Height = currentRect.Height / dpiScale;

            // 按游戏窗口尺寸调整遮罩内容，避免窗口变小时控件跑出屏幕。
            var contentWidth = Math.Max(240, Width - 40);
            var logHeight = Math.Clamp(Height * 0.22, 120, 280);
            LogTextBoxWrapper.Width = contentWidth;
            LogTextBoxWrapper.Height = logHeight;
            StatusWrapper.Width = contentWidth;
            Canvas.SetTop(LogTextBoxWrapper, Height - logHeight - 65);
            Canvas.SetTop(StatusWrapper, Height - logHeight - 90);
        });
        // 重新计算控件位置
        // shit code 预定了
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "RefreshSettings", new object(), "重新计算控件位置"));
    }

    public MaskWindow()
    {
        _maskWindow = this;
        this.SetResourceReference(StyleProperty, typeof(MaskWindow));
        InitializeComponent();
        this.InitializeDpiAwareness();
        _viewModel = DataContext as MaskWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        Loaded += OnMaskWindowLoaded;
        LogTextBox.TextChanged += LogTextBoxTextChanged;
        //AddAreaSettingsControl("测试识别窗口");
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        // 先设置基础窗口样式，再让 SourceInitialized 事件中的点击穿透行为覆盖最终状态。
        this.SetLayeredWindow();
        this.HideFromAltTab();
        base.OnSourceInitialized(e);
    }

    /// <summary>
    /// 配置就绪后监听遮罩显示开关；配置可能在窗口 Loaded 之后才完成初始化。
    /// </summary>
    private void OnMaskWindowLoaded(object sender, RoutedEventArgs e)
    {
        AttachMaskConfig();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MaskWindowViewModel.Config))
        {
            AttachMaskConfig();
        }
    }

    private void AttachMaskConfig()
    {
        var config = _viewModel?.Config?.MaskWindowConfig;
        if (ReferenceEquals(config, _maskConfig))
        {
            return;
        }

        if (_maskConfig is not null)
        {
            _maskConfig.PropertyChanged -= OnMaskConfigPropertyChanged;
        }

        _maskConfig = config;
        if (_maskConfig is not null)
        {
            _maskConfig.PropertyChanged += OnMaskConfigPropertyChanged;
            if (!_maskConfig.MaskEnabled)
            {
                Hide();
            }
        }
    }

    private void OnMaskConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MaskWindowConfig.MaskEnabled) || _maskConfig is null)
        {
            return;
        }

        if (!_maskConfig.MaskEnabled)
        {
            Hide();
        }
        else if (SystemControl.IsGameActive())
        {
            Show();
        }
    }

    private void LogTextBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        var textRange = new TextRange(LogTextBox.Document.ContentStart, LogTextBox.Document.ContentEnd);
        if (textRange.Text.Length > 10000)
        {
            LogTextBox.Document.Blocks.Clear();
        }

        LogTextBox.ScrollToEnd();
    }

    public void Refresh()
    {
        Dispatcher.Invoke(InvalidateVisual);
    }

    public void Invoke(Action action)
    {
        Dispatcher.Invoke(action);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        try
        {
            var cnt = VisionContext.Instance().DrawContent.RectList.Count + VisionContext.Instance().DrawContent.LineList.Count + VisionContext.Instance().DrawContent.TextList.Count;
            if (cnt == 0)
            {
                return;
            }

            // 先有上方判断的原因是，有可能Render的时候，配置还未初始化
            if (!TaskContext.Instance().Config.MaskWindowConfig.DisplayRecognitionResultsOnMask)
            {
                return;
            }

            foreach (var kv in VisionContext.Instance().DrawContent.RectList)
            {
                foreach (var drawable in kv.Value)
                {
                    if (!drawable.IsEmpty)
                    {
                        drawingContext.DrawRectangle(Brushes.Transparent,
                            new Pen(new SolidColorBrush(drawable.Pen.Color.ToWindowsColor()), drawable.Pen.Width),
                            drawable.Rect);
                    }
                }
            }

            foreach (var kv in VisionContext.Instance().DrawContent.LineList)
            {
                foreach (var drawable in kv.Value)
                {
                    drawingContext.DrawLine(new Pen(new SolidColorBrush(drawable.Pen.Color.ToWindowsColor()), drawable.Pen.Width), drawable.P1, drawable.P2);
                }
            }

            foreach (var kv in VisionContext.Instance().DrawContent.TextList)
            {
                foreach (var drawable in kv.Value)
                {
                    if (!drawable.IsEmpty)
                    {
                        drawingContext.DrawText(new FormattedText(drawable.Text,
                            CultureInfo.GetCultureInfo("zh-cn"),
                            FlowDirection.LeftToRight,
                            MyTypeface,
                            36, Brushes.Black, 1), drawable.Point);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }

        base.OnRender(drawingContext);
    }

    public RichTextBox LogBox => LogTextBox;
}

file static class MaskWindowExtension
{
    /// <summary>
    /// 从 Alt+Tab 列表中隐藏遮罩窗口。
    /// </summary>
    public static void HideFromAltTab(this Window window)
    {
        var hWnd = new WindowInteropHelper(window).Handle;
        var style = User32.GetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE);
        style |= (int)User32.WindowStylesEx.WS_EX_TOOLWINDOW;
        _ = User32.SetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE, style);
    }

    public static void SetLayeredWindow(this Window window, bool isLayered = true)
    {
        SetLayeredWindow(new WindowInteropHelper(window).Handle, isLayered);
    }

    private static void SetLayeredWindow(nint hWnd, bool isLayered = true)
    {
        int style = User32.GetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE);

        if (isLayered)
        {
            style |= (int)User32.WindowStylesEx.WS_EX_TRANSPARENT;
            style |= (int)User32.WindowStylesEx.WS_EX_LAYERED;
        }
        else
        {
            style &= ~(int)User32.WindowStylesEx.WS_EX_TRANSPARENT;
            style &= ~(int)User32.WindowStylesEx.WS_EX_LAYERED;
        }

        _ = User32.SetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE, style);
    }
}
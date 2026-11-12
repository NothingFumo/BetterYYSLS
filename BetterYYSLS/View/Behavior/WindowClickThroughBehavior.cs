using Microsoft.Xaml.Behaviors;
using System;
using System.Windows;
using System.Windows.Interop;
using Vanara.PInvoke;

namespace BetterYYSLS.View.Behavior;

/// <summary>
/// 根据绑定值切换分层窗口的鼠标穿透。
/// 穿透时保留 WS_EX_LAYERED，并添加 WS_EX_TRANSPARENT；关闭时只移除透明样式。
/// </summary>
public sealed class WindowClickThroughBehavior : Behavior<Window>
{
    /// <summary>
    /// 是否让窗口鼠标穿透。
    /// </summary>
    public static readonly DependencyProperty IsClickThroughProperty =
        DependencyProperty.Register(
            nameof(IsClickThrough),
            typeof(bool),
            typeof(WindowClickThroughBehavior),
            new PropertyMetadata(true, OnIsClickThroughChanged));

    /// <summary>
    /// 是否让窗口鼠标穿透。
    /// </summary>
    public bool IsClickThrough
    {
        get => (bool)GetValue(IsClickThroughProperty);
        set => SetValue(IsClickThroughProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.SourceInitialized += OnWindowReady;
        AssociatedObject.Loaded += OnWindowReady;
        Apply();
    }

    protected override void OnDetaching()
    {
        AssociatedObject.SourceInitialized -= OnWindowReady;
        AssociatedObject.Loaded -= OnWindowReady;
        base.OnDetaching();
    }

    private static void OnIsClickThroughChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((WindowClickThroughBehavior)d).Apply();
    }

    private void OnWindowReady(object? sender, EventArgs e)
    {
        Apply();
    }

    private void Apply()
    {
        if (AssociatedObject is null)
        {
            return;
        }

        var handle = new WindowInteropHelper(AssociatedObject).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var style = User32.GetWindowLong(handle, User32.WindowLongFlags.GWL_EXSTYLE);
        style |= (int)User32.WindowStylesEx.WS_EX_LAYERED;
        if (IsClickThrough)
        {
            style |= (int)User32.WindowStylesEx.WS_EX_TRANSPARENT;
        }
        else
        {
            style &= ~(int)User32.WindowStylesEx.WS_EX_TRANSPARENT;
        }

        _ = User32.SetWindowLong(handle, User32.WindowLongFlags.GWL_EXSTYLE, style);
    }
}
